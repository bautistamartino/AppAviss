using Aviss.Application.Exportacion;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 011, RF-19 y RF-20: lo que la consulta de exportación arma cuando el registro vigente tiene Evento 2, con tactos y rechazos
/// REALES en SQLite. El Evento 2 sale por su nombre de hoy, comparte fecha y hora con el Evento 1, y los días y el resultado quedan vacíos.
/// </summary>
public sealed class ConsultaExportacionDosEventosTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    private BaseDeDatosDePrueba Db => _e.Base;
    public void Dispose() => _e.Dispose();

    private readonly Guid _ok, _prostag, _vacia, _aborto;

    public ConsultaExportacionDosEventosTests()
    {
        _ok = Db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        _prostag = Db.Estado("PROSTAG", "PROSTAG", "PROSTAG");   // creado a mano: sin clave estándar
        _vacia = Db.Estado("VACIA", "VACIA", "VACIA", tipo: "Estado", clave: "VACIA");
        _aborto = Db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
    }

    private Guid Vaca(string caravana) => Db.Animal(_e.RodeoId, caravana, "Importado", datosOriginalesJson: $"{{\"ID\":\"{caravana}\"}}");
    private static DateTime Hora(int h, int min = 0) => new(2026, 11, 5, h, min, 0, DateTimeKind.Utc);
    private Task<DatosExportacion> Consultar() => _e.Servicio<IConsultaExportacion>().ObtenerAsync(_e.RodeoId);

    [Fact]
    public async Task Un_tacto_con_dos_eventos_sale_con_los_dos_nombres_el_mismo_horario_y_los_dias_vacios()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, evento2Id: _prostag, observaciones2: "dosis única", fechaHora: Hora(12, 5));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("VACIA", v.Evento);
        Assert.Equal("PROSTAG", v.Evento2);
        Assert.Equal("05/11/26", v.Fecha);
        Assert.Equal("", v.DiasPreniez);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
        // Las observaciones no se exportan.
        Assert.DoesNotContain("dosis", string.Join("|", v.EnOrden()));
    }

    [Fact]
    public async Task Un_tacto_cuyo_evento_1_es_el_estado_y_trae_Evento_2_sale_con_ambos()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, evento2Id: _ok, fechaHora: Hora(10));   // sin EventoId: el Evento 1 es el propio estado

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("VACIA", v.Evento);
        Assert.Equal("OK", v.Evento2);
    }

    [Fact]
    public async Task Un_tacto_sin_Evento_2_lo_deja_vacio()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, fechaHora: Hora(10));

        Assert.Equal("", (await Consultar()).ValoresPorCaravana["100"].Evento2);
    }

    [Fact]
    public async Task Un_solo_evento_con_Evento_2_sale_con_los_dos_nombres()
    {
        var a = Vaca("100");
        Db.Rechazo(a, eventoId: _ok, evento2Id: _prostag, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("OK", v.Evento);
        Assert.Equal("PROSTAG", v.Evento2);
        Assert.Equal("", v.DiasAborto);
    }

    [Fact]
    public async Task El_rechazo_fijo_sale_como_RECHAZO_y_sin_Evento_2()
    {
        var a = Vaca("100");
        Db.Rechazo(a, "motivo", fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("RECHAZO", v.Evento);
        Assert.Equal("", v.Evento2);
    }

    [Fact]
    public async Task Un_registro_anulado_no_exporta_su_Evento_2_y_sale_el_ultimo_vigente_con_sus_dos_eventos()
    {
        var a = Vaca("100");
        var anulado = Db.Tacto(a, _vacia, evento2Id: _prostag, fechaHora: Hora(10), anulado: true);
        Db.Tacto(a, null, tipo: "Anulacion", tactoAnuladoId: anulado, fechaHora: Hora(11));
        Db.Rechazo(a, eventoId: _ok, evento2Id: _prostag, fechaHora: Hora(12));   // el vigente (corregido): otros dos eventos

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("OK", v.Evento);
        Assert.Equal("PROSTAG", v.Evento2);
    }

    [Fact]
    public async Task Un_registro_anulado_sin_otro_vigente_no_exporta_nada()
    {
        var a = Vaca("100");
        var anulado = Db.Tacto(a, _vacia, evento2Id: _prostag, fechaHora: Hora(10), anulado: true);
        Db.Tacto(a, null, tipo: "Anulacion", tactoAnuladoId: anulado, fechaHora: Hora(11));

        Assert.Empty((await Consultar()).ValoresPorCaravana);
    }

    [Fact]
    public async Task Una_vaca_sin_tacto_no_lleva_valores()
    {
        Vaca("100");
        Assert.Empty((await Consultar()).ValoresPorCaravana);
    }

    [Fact]
    public async Task El_Evento_2_sale_con_el_nombre_de_hoy_aunque_se_haya_renombrado()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, evento2Id: _prostag, fechaHora: Hora(10));
        Db.Ejecutar("UPDATE EstadoReproductivo SET Nombre = 'PROSTAGLANDINA' WHERE Id = $id", ("$id", _prostag));

        Assert.Equal("PROSTAGLANDINA", (await Consultar()).ValoresPorCaravana["100"].Evento2);
    }

    [Fact]
    public async Task El_Evento_2_de_baja_igual_se_exporta_por_su_nombre()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, evento2Id: _prostag, fechaHora: Hora(10));
        Db.Ejecutar("UPDATE EstadoReproductivo SET Activo = 0 WHERE Id = $id", ("$id", _prostag));

        Assert.Equal("PROSTAG", (await Consultar()).ValoresPorCaravana["100"].Evento2);
    }
}
