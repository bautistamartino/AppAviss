using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasMarcaPasadaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static DatosValidacionMarca Valida() => new(
        AnimalAnulado: false, RodeoActivo: true, EstablecimientoActivo: true,
        FechaHoraUtc: Ahora, AhoraUtc: Ahora, FechaUltimoRegistroVigenteUtc: null);

    [Fact]
    public void Marca_valida_no_tiene_motivo_de_rechazo()
        => Assert.Null(ReglasMarcaPasada.Validar(Valida()));

    [Fact]
    public void Rechaza_animal_anulado()
        => Assert.Equal(MotivoRechazoMarca.AnimalAnulado, ReglasMarcaPasada.Validar(Valida() with { AnimalAnulado = true }));

    [Fact]
    public void Rechaza_rodeo_de_baja()
        => Assert.Equal(MotivoRechazoMarca.RodeoDeBaja, ReglasMarcaPasada.Validar(Valida() with { RodeoActivo = false }));

    [Fact]
    public void Rechaza_establecimiento_de_baja()
        => Assert.Equal(MotivoRechazoMarca.EstablecimientoDeBaja, ReglasMarcaPasada.Validar(Valida() with { EstablecimientoActivo = false }));

    [Fact]
    public void Tolera_hasta_5_minutos_en_el_futuro_y_rechaza_mas()
    {
        Assert.Null(ReglasMarcaPasada.Validar(Valida() with { FechaHoraUtc = Ahora.AddMinutes(5) }));
        Assert.Equal(MotivoRechazoMarca.FechaFutura,
            ReglasMarcaPasada.Validar(Valida() with { FechaHoraUtc = Ahora.AddMinutes(5).AddTicks(1) }));
    }

    [Fact]
    public void Rechaza_una_fecha_anterior_al_ultimo_registro_pero_acepta_la_igual()
    {
        Assert.Equal(MotivoRechazoMarca.FechaAnteriorAlUltimoRegistro,
            ReglasMarcaPasada.Validar(Valida() with { FechaUltimoRegistroVigenteUtc = Ahora.AddSeconds(1) }));
        Assert.Null(ReglasMarcaPasada.Validar(Valida() with { FechaUltimoRegistroVigenteUtc = Ahora }));
        Assert.Null(ReglasMarcaPasada.Validar(Valida() with { FechaUltimoRegistroVigenteUtc = Ahora.AddDays(-1) }));
    }

    [Fact]
    public void Con_varios_problemas_gana_el_primero_del_orden()
    {
        var todo = Valida() with { AnimalAnulado = true, RodeoActivo = false, FechaHoraUtc = Ahora.AddHours(1) };
        Assert.Equal(MotivoRechazoMarca.AnimalAnulado, ReglasMarcaPasada.Validar(todo));
        Assert.Equal(MotivoRechazoMarca.RodeoDeBaja, ReglasMarcaPasada.Validar(todo with { AnimalAnulado = false }));
        Assert.Equal(MotivoRechazoMarca.FechaFutura, ReglasMarcaPasada.Validar(todo with { AnimalAnulado = false, RodeoActivo = true }));
    }
}
