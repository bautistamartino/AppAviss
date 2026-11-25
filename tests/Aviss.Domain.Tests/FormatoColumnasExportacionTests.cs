using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

/// <summary>Spec 007, RF-6 y RF-7: fecha dd/MM/yyyy y hora militar HHmm, en la hora local de la zona dada.</summary>
public class FormatoColumnasExportacionTests
{
    private static DateTime Utc(int a, int m, int d, int h = 0, int min = 0)
        => new(a, m, d, h, min, 0, DateTimeKind.Utc);

    private static TimeZoneInfo Fija(double horas)
        => TimeZoneInfo.CreateCustomTimeZone("z" + horas, TimeSpan.FromHours(horas), "z", "z");

    /// <summary>UTC-3 con horario de verano (+1 h) del 4 de octubre 00:00 al 1 de marzo 01:00 (igual que en DiaLocalTests).</summary>
    private static TimeZoneInfo ConVerano()
    {
        var regla = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 4),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 1));
        return TimeZoneInfo.CreateCustomTimeZone("verano", TimeSpan.FromHours(-3), "verano", "verano", "verano", new[] { regla });
    }

    [Fact]
    public void La_fecha_sale_con_dia_mes_y_anio_de_dos_cifras_con_ceros()
        => Assert.Equal("05/11/26", FormatoColumnasExportacion.Fecha(Utc(2026, 11, 5, 12), TimeZoneInfo.Utc));

    [Theory]
    [InlineData(15, 30, "15:30")]
    [InlineData(9, 5, "09:05")]
    [InlineData(0, 10, "00:10")]
    [InlineData(0, 0, "00:00")]
    [InlineData(23, 59, "23:59")]
    public void La_hora_sale_con_ceros_a_la_izquierda_y_dos_puntos(int h, int min, string esperado)
        => Assert.Equal(esperado, FormatoColumnasExportacion.Hora(Utc(2026, 11, 5, h, min), TimeZoneInfo.Utc));

    [Fact]
    public void Se_convierte_de_UTC_a_la_zona_local()
    {
        // 12:30 UTC = 09:30 en UTC-3.
        var u = Utc(2026, 11, 5, 12, 30);
        Assert.Equal("05/11/26", FormatoColumnasExportacion.Fecha(u, Fija(-3)));
        Assert.Equal("09:30", FormatoColumnasExportacion.Hora(u, Fija(-3)));
    }

    [Fact]
    public void Cerca_de_la_medianoche_la_fecha_es_la_local_no_la_UTC()
    {
        // 02:10 UTC del 6 = 23:10 local del 5.
        var u = Utc(2026, 11, 6, 2, 10);
        Assert.Equal("05/11/26", FormatoColumnasExportacion.Fecha(u, Fija(-3)));
        Assert.Equal("23:10", FormatoColumnasExportacion.Hora(u, Fija(-3)));
        // Y al revés: 23:30 UTC del 5 ya es el día 6 en UTC+2.
        Assert.Equal("06/11/26", FormatoColumnasExportacion.Fecha(Utc(2026, 11, 5, 23, 30), Fija(2)));
    }

    [Fact]
    public void Con_horario_de_verano_se_usa_el_desfase_que_rige_en_cada_fecha()
    {
        var zona = ConVerano();   // UTC-3 en invierno, UTC-2 en verano
        // Verano (noviembre): 12:00 UTC = 10:00 local.
        Assert.Equal("10:00", FormatoColumnasExportacion.Hora(Utc(2026, 11, 5, 12), zona));
        // Invierno (junio): 12:00 UTC = 09:00 local.
        Assert.Equal("09:00", FormatoColumnasExportacion.Hora(Utc(2026, 6, 5, 12), zona));
    }

    [Fact]
    public void Una_fecha_que_no_es_UTC_se_rechaza()
        => Assert.Throws<ArgumentException>(() => FormatoColumnasExportacion.Fecha(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Local), TimeZoneInfo.Utc));
}
