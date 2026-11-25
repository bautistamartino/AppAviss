using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

/// <summary>
/// Spec 011 (RF-1, RF-3, RF-6, RF-8, RF-11, RF-12): qué evento admite un segundo (Evento 1) y cuál puede ser ese segundo
/// (Evento 2). Son reglas puras: reciben los datos del evento, no el catálogo.
/// </summary>
public class ReglasSegundoEventoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Un evento estándar tal como lo siembra la app (con pasos y destino si los tiene).</summary>
    private static DatosEvento Estandar(string clave, bool activo = true)
    {
        var inicial = EstadosIniciales.Todos.Single(x => x.Codigo == clave);
        // El destino real es otro estado; para la regla solo importa que exista (no es nulo).
        var e = EstadoReproductivo.CrearEstandar(inicial, Ahora, inicial.DestinoClave is null ? null : Guid.NewGuid());
        if (!activo) e.Desactivar(Ahora);
        return DatosEvento.De(e);
    }

    /// <summary>Un evento creado a mano (PROSTAG, DIB...): sin clave estándar, tipo Estado, sin pasos.</summary>
    private static DatosEvento APropia(string nombre = "PROSTAG", bool activo = true)
    {
        var e = EstadoReproductivo.Crear(nombre, nombre, nombre, Ahora);
        if (!activo) e.Desactivar(Ahora);
        return DatosEvento.De(e);
    }

    [Theory]
    [InlineData("OK", true)]
    [InlineData("REVISAR_NUEVAMENTE", true)]
    [InlineData("PRENADA", true)]
    [InlineData("VACIA", true)]
    [InlineData("NOINSEM", true)]
    [InlineData("INSEMIN", true)]
    [InlineData("SECA", true)]
    [InlineData("FRESCA", true)]
    [InlineData("ABORTO", false)]                 // tiene pasos
    [InlineData("PRENADA_FECHA_PREVIA", false)]   // tiene pasos y destino
    public void AdmiteSegundo_de_los_estandar(string clave, bool esperado)
        => Assert.Equal(esperado, ReglasSegundoEvento.AdmiteSegundo(Estandar(clave)));

    [Fact]
    public void AdmiteSegundo_un_evento_creado_a_mano_si_y_uno_de_baja_no()
    {
        Assert.True(ReglasSegundoEvento.AdmiteSegundo(APropia()));
        Assert.False(ReglasSegundoEvento.AdmiteSegundo(APropia(activo: false)));
        Assert.False(ReglasSegundoEvento.AdmiteSegundo(Estandar("OK", activo: false)));
    }

    [Theory]
    [InlineData("OK", true)]
    [InlineData("REVISAR_NUEVAMENTE", true)]
    [InlineData("PRENADA", false)]
    [InlineData("VACIA", false)]
    [InlineData("NOINSEM", false)]
    [InlineData("INSEMIN", false)]   // puede ser Evento 1 pero no Evento 2
    [InlineData("SECA", false)]
    [InlineData("FRESCA", false)]
    [InlineData("ABORTO", false)]
    [InlineData("PRENADA_FECHA_PREVIA", false)]
    public void PuedeSerSegundo_de_los_estandar(string clave, bool esperado)
        => Assert.Equal(esperado, ReglasSegundoEvento.PuedeSerSegundo(Estandar(clave)));

    [Fact]
    public void PuedeSerSegundo_un_evento_creado_a_mano_si_salvo_que_este_de_baja()
    {
        Assert.True(ReglasSegundoEvento.PuedeSerSegundo(APropia()));
        Assert.False(ReglasSegundoEvento.PuedeSerSegundo(APropia(activo: false)));
    }

    [Fact]
    public void Un_evento_creado_a_mano_con_pasos_o_destino_no_puede_ser_segundo_ni_admite_uno()
    {
        var conPasos = APropia() with { TienePasos = true };
        var conDestino = APropia() with { TieneDestino = true };
        Assert.False(ReglasSegundoEvento.PuedeSerSegundo(conPasos));
        Assert.False(ReglasSegundoEvento.AdmiteSegundo(conPasos));
        Assert.False(ReglasSegundoEvento.PuedeSerSegundo(conDestino));
        Assert.False(ReglasSegundoEvento.AdmiteSegundo(conDestino));
    }

    [Fact]
    public void Validar_acepta_las_combinaciones_validas_en_los_dos_ordenes()
    {
        var ok = Estandar("OK");
        var prostag = APropia();
        Assert.Null(ReglasSegundoEvento.Validar(ok, prostag, null, null));
        Assert.Null(ReglasSegundoEvento.Validar(prostag, ok, "obs1", "obs2"));
        Assert.Null(ReglasSegundoEvento.Validar(Estandar("VACIA"), ok, null, null));
        Assert.Null(ReglasSegundoEvento.Validar(Estandar("INSEMIN"), prostag, null, null));
        Assert.Null(ReglasSegundoEvento.Validar(ok, Estandar("REVISAR_NUEVAMENTE"), null, new string('x', 300)));
    }

    [Fact]
    public void Validar_rechaza_un_segundo_inexistente()
        => Assert.Equal(MotivoSegundoEvento.SegundoInexistente, ReglasSegundoEvento.Validar(Estandar("OK"), null, null, null));

    [Fact]
    public void Validar_rechaza_el_mismo_evento_dos_veces()
    {
        var ok = Estandar("OK");
        Assert.Equal(MotivoSegundoEvento.SegundoIgualAlPrimero, ReglasSegundoEvento.Validar(ok, ok, null, null));
    }

    [Fact]
    public void Validar_rechaza_un_primero_que_no_admite_segundo()
    {
        var segundo = APropia();
        Assert.Equal(MotivoSegundoEvento.PrimeroNoAdmiteSegundo, ReglasSegundoEvento.Validar(Estandar("ABORTO"), segundo, null, null));
        Assert.Equal(MotivoSegundoEvento.PrimeroNoAdmiteSegundo, ReglasSegundoEvento.Validar(Estandar("PRENADA_FECHA_PREVIA"), segundo, null, null));
        // El RECHAZO fijo no es una fila del catálogo: llega como nulo.
        Assert.Equal(MotivoSegundoEvento.PrimeroNoAdmiteSegundo, ReglasSegundoEvento.Validar(null, segundo, null, null));
    }

    [Fact]
    public void Validar_rechaza_un_segundo_de_baja_y_uno_no_combinable()
    {
        var ok = Estandar("OK");
        Assert.Equal(MotivoSegundoEvento.SegundoDeBaja, ReglasSegundoEvento.Validar(ok, APropia(activo: false), null, null));
        foreach (var clave in new[] { "PRENADA", "VACIA", "NOINSEM", "INSEMIN", "SECA", "FRESCA", "ABORTO", "PRENADA_FECHA_PREVIA" })
            Assert.Equal(MotivoSegundoEvento.SegundoNoCombinable, ReglasSegundoEvento.Validar(ok, Estandar(clave), null, null));
    }

    [Fact]
    public void Validar_rechaza_observaciones_de_mas_de_300_en_cualquiera_de_las_dos()
    {
        var ok = Estandar("OK");
        var prostag = APropia();
        var larga = new string('x', ReglasSegundoEvento.MaximoObservaciones + 1);
        Assert.Equal(MotivoSegundoEvento.ObservacionLarga, ReglasSegundoEvento.Validar(ok, prostag, larga, null));
        Assert.Equal(MotivoSegundoEvento.ObservacionLarga, ReglasSegundoEvento.Validar(ok, prostag, null, larga));
        Assert.Equal(300, ReglasSegundoEvento.MaximoObservaciones);
    }
}
