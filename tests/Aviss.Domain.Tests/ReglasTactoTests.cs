using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasTactoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Estado = Guid.NewGuid();

    // Datos de un tacto perfectamente válido; cada test cambia solo lo que quiere probar.
    private static DatosValidacionTacto Valido() => new(
        AnimalAnulado: false, RodeoActivo: true, EstablecimientoActivo: true, EstadoNuevoActivo: true,
        EstadoActualDelAnimal: Estado, EstadoAnteriorInformado: null,
        FechaHoraUtc: Ahora, AhoraUtc: Ahora, FechaUltimoVigenteUtc: null, DiasPrenez: null);

    [Fact]
    public void Tacto_valido_no_tiene_motivo_de_rechazo()
        => Assert.Null(ReglasTacto.Validar(Valido()));

    [Fact]
    public void Rechaza_animal_anulado()
        => Assert.Equal(MotivoRechazoTacto.AnimalAnulado, ReglasTacto.Validar(Valido() with { AnimalAnulado = true }));

    [Fact]
    public void Rechaza_rodeo_de_baja()
        => Assert.Equal(MotivoRechazoTacto.RodeoDeBaja, ReglasTacto.Validar(Valido() with { RodeoActivo = false }));

    [Fact]
    public void Rechaza_establecimiento_de_baja()
        => Assert.Equal(MotivoRechazoTacto.EstablecimientoDeBaja, ReglasTacto.Validar(Valido() with { EstablecimientoActivo = false }));

    [Fact]
    public void Rechaza_estado_nuevo_de_baja()
        => Assert.Equal(MotivoRechazoTacto.EstadoNuevoDeBaja, ReglasTacto.Validar(Valido() with { EstadoNuevoActivo = false }));

    [Fact]
    public void Estado_anterior_informado_distinto_del_actual_se_rechaza()
    {
        var d = Valido() with { EstadoAnteriorInformado = new EstadoEsperado(Guid.NewGuid()) };
        Assert.Equal(MotivoRechazoTacto.EstadoAnteriorNoCoincide, ReglasTacto.Validar(d));
    }

    [Fact]
    public void Estado_anterior_informado_igual_al_actual_se_acepta()
        => Assert.Null(ReglasTacto.Validar(Valido() with { EstadoAnteriorInformado = new EstadoEsperado(Estado) }));

    [Fact]
    public void Estado_anterior_vacio_informado_solo_coincide_con_animal_sin_estado()
    {
        var vacioInformado = new EstadoEsperado(null);
        Assert.Equal(MotivoRechazoTacto.EstadoAnteriorNoCoincide,
            ReglasTacto.Validar(Valido() with { EstadoAnteriorInformado = vacioInformado }));
        Assert.Null(ReglasTacto.Validar(Valido() with { EstadoActualDelAnimal = null, EstadoAnteriorInformado = vacioInformado }));
    }

    [Fact]
    public void Animal_sin_estado_y_sin_informar_es_valido()
        => Assert.Null(ReglasTacto.Validar(Valido() with { EstadoActualDelAnimal = null }));

    [Fact]
    public void Fecha_futura_hasta_5_minutos_se_acepta_y_mas_no()
    {
        Assert.Null(ReglasTacto.Validar(Valido() with { FechaHoraUtc = Ahora.AddMinutes(5) }));
        Assert.Equal(MotivoRechazoTacto.FechaFutura,
            ReglasTacto.Validar(Valido() with { FechaHoraUtc = Ahora.AddMinutes(5).AddTicks(1) }));
    }

    [Fact]
    public void Fecha_anterior_al_ultimo_vigente_se_rechaza_e_igual_se_acepta()
    {
        var ultimo = Ahora.AddHours(-1);
        Assert.Null(ReglasTacto.Validar(Valido() with { FechaHoraUtc = ultimo, FechaUltimoVigenteUtc = ultimo }));
        Assert.Equal(MotivoRechazoTacto.FechaAnteriorAlUltimoTacto,
            ReglasTacto.Validar(Valido() with { FechaHoraUtc = ultimo.AddTicks(-1), FechaUltimoVigenteUtc = ultimo }));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(-1, false)]
    [InlineData(301, false)]
    public void Dias_de_prenez_entre_0_y_300(int dias, bool valido)
    {
        var motivo = ReglasTacto.Validar(Valido() with { DiasPrenez = dias });
        if (valido) Assert.Null(motivo);
        else Assert.Equal(MotivoRechazoTacto.DiasPrenezFueraDeRango, motivo);
    }

    // ---- último tacto vigente ----

    private static Tacto Normal(Guid animal, DateTime fecha, long orden, bool anulado = false)
    {
        var t = Tacto.Registrar(animal, fecha, orden, null, Guid.NewGuid(), null, null, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        if (anulado) t.MarcarAnulado(Ahora);
        return t;
    }

    [Fact]
    public void Ultimo_vigente_de_lista_vacia_es_nulo()
        => Assert.Null(ReglasTacto.UltimoVigente(Array.Empty<Tacto>()));

    [Fact]
    public void Ultimo_vigente_es_el_de_fecha_mas_reciente()
    {
        var animal = Guid.NewGuid();
        var viejo = Normal(animal, Ahora.AddDays(-5), 1);
        var nuevo = Normal(animal, Ahora.AddDays(-1), 2);
        Assert.Same(nuevo, ReglasTacto.UltimoVigente(new[] { nuevo, viejo }));
    }

    [Fact]
    public void Con_igual_fecha_gana_el_de_mayor_orden_de_insercion()
    {
        var animal = Guid.NewGuid();
        var primero = Normal(animal, Ahora, 7);
        var segundo = Normal(animal, Ahora, 8);
        Assert.Same(segundo, ReglasTacto.UltimoVigente(new[] { segundo, primero }));
        Assert.Same(segundo, ReglasTacto.UltimoVigente(new[] { primero, segundo }));
    }

    [Fact]
    public void Ignora_tactos_anulados_y_tactos_de_anulacion()
    {
        var animal = Guid.NewGuid();
        var vigente = Normal(animal, Ahora.AddDays(-3), 1);
        var anulado = Normal(animal, Ahora.AddDays(-2), 2, anulado: true);
        // la anulación se crea sobre un tacto aún no marcado; acá solo importa que su Tipo sea Anulacion
        var otro = Normal(animal, Ahora.AddDays(-2), 3);
        var anulacion = Tacto.CrearAnulacion(otro, Estado, Ahora, 4, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        Assert.Same(vigente, ReglasTacto.UltimoVigente(new[] { vigente, anulado, anulacion }));
    }

    [Fact]
    public void Vigente_anterior_excluye_el_tacto_indicado()
    {
        var animal = Guid.NewGuid();
        var a = Normal(animal, Ahora.AddDays(-3), 1);
        var b = Normal(animal, Ahora.AddDays(-2), 2);
        var c = Normal(animal, Ahora.AddDays(-1), 3);
        Assert.Same(b, ReglasTacto.VigenteAnteriorA(new[] { a, b, c }, c));
        Assert.Null(ReglasTacto.VigenteAnteriorA(new[] { a }, a));
    }

    // ---- fábricas de Tacto ----

    [Fact]
    public void Tacto_de_anulacion_lleva_los_datos_de_RF13()
    {
        var animal = Guid.NewGuid();
        var anterior = Guid.NewGuid();
        var nuevo = Guid.NewGuid();
        var original = Tacto.Registrar(animal, Ahora.AddDays(-1), 1, anterior, nuevo, 120, "obs", Guid.NewGuid(), Guid.NewGuid(), Ahora);
        var usuario = Guid.NewGuid();
        var dispositivo = Guid.NewGuid();

        var anulacion = Tacto.CrearAnulacion(original, nuevo, Ahora, 2, usuario, dispositivo, Ahora);

        Assert.Equal(TipoTacto.Anulacion, anulacion.Tipo);
        Assert.Equal(original.Id, anulacion.TactoAnuladoId);
        Assert.Equal(animal, anulacion.AnimalId);
        Assert.Equal(nuevo, anulacion.EstadoAnteriorId);   // estado actual del animal
        Assert.Equal(anterior, anulacion.EstadoNuevoId);   // estado anterior del tacto anulado
        Assert.Null(anulacion.DiasPrenez);
        Assert.Equal(usuario, anulacion.UsuarioId);
        Assert.Equal(dispositivo, anulacion.DispositivoId);
        Assert.False(anulacion.Anulado);
    }

    [Fact]
    public void Tacto_de_anulacion_puede_volver_a_animal_sin_estado()
    {
        var original = Tacto.Registrar(Guid.NewGuid(), Ahora, 1, null, Estado, null, null, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        var anulacion = Tacto.CrearAnulacion(original, Estado, Ahora, 2, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        Assert.Null(anulacion.EstadoNuevoId);
    }

    [Fact]
    public void Un_tacto_de_anulacion_no_se_marca_anulado_ni_se_anula()
    {
        var original = Normal(Guid.NewGuid(), Ahora, 1);
        var anulacion = Tacto.CrearAnulacion(original, Estado, Ahora, 2, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        Assert.Throws<DominioException>(() => anulacion.MarcarAnulado(Ahora));
        Assert.Throws<DominioException>(() => Tacto.CrearAnulacion(anulacion, Estado, Ahora, 3, Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Un_tacto_ya_anulado_no_se_anula_de_nuevo()
    {
        var t = Normal(Guid.NewGuid(), Ahora, 1, anulado: true);
        Assert.Throws<DominioException>(() => t.MarcarAnulado(Ahora));
        Assert.Throws<DominioException>(() => Tacto.CrearAnulacion(t, Estado, Ahora, 2, Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Tacto_normal_exige_estado_nuevo_y_dias_en_rango()
    {
        Assert.Throws<DominioException>(() =>
            Tacto.Registrar(Guid.NewGuid(), Ahora, 1, null, Guid.Empty, null, null, Guid.NewGuid(), Guid.NewGuid(), Ahora));
        Assert.Throws<DominioException>(() =>
            Tacto.Registrar(Guid.NewGuid(), Ahora, 1, null, Estado, 301, null, Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }
}
