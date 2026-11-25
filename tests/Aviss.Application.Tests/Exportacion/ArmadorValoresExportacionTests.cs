using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Spec 007, RF-6 a RF-11: los seis valores de una vaca y qué columna va vacía según el evento.</summary>
public class ArmadorValoresExportacionTests
{
    private static TimeZoneInfo Fija(double h) => TimeZoneInfo.CreateCustomTimeZone("z" + h, TimeSpan.FromHours(h), "z", "z");

    [Fact]
    public void Fecha_y_hora_salen_en_hora_local_con_el_formato_del_archivo()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(fecha: R.Utc(2026, 11, 5, 12, 5)), Fija(-3));
        Assert.Equal("05/11/26", v.Fecha);
        Assert.Equal("09:05", v.Hora);
    }

    [Fact]
    public void Cerca_de_la_medianoche_la_fecha_es_la_local()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(fecha: R.Utc(2026, 11, 6, 2, 10)), Fija(-3));
        Assert.Equal("05/11/26", v.Fecha);
        Assert.Equal("23:10", v.Hora);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("VACIA")]
    [InlineData("Preñada fecha correcta")]
    [InlineData("Revisar Nuevamente")]
    [InlineData("RECHAZO")]
    [InlineData("Un evento creado a mano")]
    public void El_evento_sale_por_su_nombre_tal_cual(string nombre)
        => Assert.Equal(nombre, ArmadorValoresExportacion.Armar(R.Reg(evento: nombre), TimeZoneInfo.Utc).Evento);

    [Fact]
    public void Un_evento_sin_pasos_deja_vacias_las_tres_columnas_de_dias_y_resultado()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(evento: "VACIA"), TimeZoneInfo.Utc);
        Assert.Equal("", v.DiasPreniez);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
    }

    [Fact]
    public void Prenada_fecha_previa_lleva_solo_los_dias_de_preniez()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(evento: "Preñada fecha previa", diasPreniez: 130), TimeZoneInfo.Utc);
        Assert.Equal("130", v.DiasPreniez);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
    }

    [Theory]
    [InlineData(ResultadoAborto.VaciaOk, "Vacía OK")]
    [InlineData(ResultadoAborto.Inseminada, "Inseminada")]
    [InlineData(ResultadoAborto.Seca, "Seca")]
    [InlineData(ResultadoAborto.Fresca, "Fresca")]
    [InlineData(ResultadoAborto.Rechazo, "Rechazo")]
    public void Aborto_lleva_los_dias_y_el_resultado_con_sus_cinco_variantes(ResultadoAborto r, string etiqueta)
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(evento: "Aborto", diasAborto: 45, resultado: r), TimeZoneInfo.Utc);
        Assert.Equal("", v.DiasPreniez);
        Assert.Equal("45", v.DiasAborto);
        Assert.Equal(etiqueta, v.ResultadoAborto);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(400, "400")]
    public void Los_dias_de_aborto_pueden_ser_0_y_400(int dias, string esperado)
        => Assert.Equal(esperado, ArmadorValoresExportacion.Armar(R.Reg(evento: "Aborto", diasAborto: dias, resultado: ResultadoAborto.Seca), TimeZoneInfo.Utc).DiasAborto);

    [Fact]
    public void Un_Aborto_viejo_sin_dias_sale_como_Aborto_a_secas()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(evento: "Aborto", tipo: TipoRegistroExportable.Rechazo), TimeZoneInfo.Utc);
        Assert.Equal("Aborto", v.Evento);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
    }

    [Fact]
    public void El_orden_de_los_valores_es_el_de_las_columnas_nuevas()
    {
        var v = ArmadorValoresExportacion.Armar(R.Reg(evento: "Aborto", diasAborto: 7, resultado: ResultadoAborto.Seca), TimeZoneInfo.Utc);
        var enOrden = v.EnOrden();
        Assert.Equal(ColumnasExportacion.Todas.Count, enOrden.Count);
        Assert.Equal(["05/11/26", "12:00", "Aborto", "", "7", "Seca"], enOrden);
    }

    [Fact]
    public void Sin_registro_todo_queda_vacio_sin_ningun_texto()
    {
        Assert.All(ValoresExportacion.Vacios.EnOrden(), t => Assert.Equal("", t));
        Assert.True(ValoresExportacion.Vacios.EsVacio);
        Assert.False(ArmadorValoresExportacion.Armar(R.Reg(), TimeZoneInfo.Utc).EsVacio);
    }

    [Fact]
    public void Las_columnas_nuevas_tienen_nombres_unicos_y_viven_en_un_solo_lugar()
    {
        Assert.Equal(["SCDAT", "SCTIM", "Evento1", "DIASPRE", "DIASABO", "RESABO"], ColumnasExportacion.Todas.Select(c => c.Nombre));
        Assert.Equal(ColumnasExportacion.Todas.Count, ColumnasExportacion.Todas.Select(c => c.Nombre.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public void Ninguna_columna_nueva_nombra_veterinario_ni_tablet()
    {
        // Spec 006: no existen.
        Assert.DoesNotContain(ColumnasExportacion.Todas, c => c.Nombre.Contains("VET", StringComparison.OrdinalIgnoreCase)
            || c.Nombre.Contains("TABLET", StringComparison.OrdinalIgnoreCase) || c.Nombre.Contains("USUARIO", StringComparison.OrdinalIgnoreCase));
    }
}
