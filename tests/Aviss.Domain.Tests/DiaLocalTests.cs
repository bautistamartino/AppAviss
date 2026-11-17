using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class DiaLocalTests
{
    private static DateTime Utc(int a, int m, int d, int h = 0, int min = 0)
        => new(a, m, d, h, min, 0, DateTimeKind.Utc);

    private static TimeZoneInfo Fija(double horas)
        => TimeZoneInfo.CreateCustomTimeZone("z" + horas, TimeSpan.FromHours(horas), "z", "z");

    /// <summary>
    /// Zona personalizada UTC-3 con horario de verano (+1 h) que entra a las 00:00 del 4 de octubre (la medianoche
    /// no existe: el reloj salta de 00:00 a 01:00) y sale a las 01:00 del 1 de marzo (la medianoche se repite).
    /// </summary>
    private static TimeZoneInfo ConVerano()
    {
        var regla = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 4),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 1));
        return TimeZoneInfo.CreateCustomTimeZone("verano", TimeSpan.FromHours(-3), "verano", "verano", "verano", new[] { regla });
    }

    [Fact]
    public void Utc_menos_3_hoy_empieza_a_las_03_utc()
    {
        // 12:00 UTC del 16 = 09:00 local del 16.
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 11, 16, 12), Fija(-3));
        Assert.Equal(Utc(2026, 11, 16, 3), r.Desde);
        Assert.Equal(Utc(2026, 11, 17, 3), r.Hasta);
    }

    [Fact]
    public void Utc_menos_3_a_las_02_utc_todavia_es_el_dia_anterior()
    {
        // 02:00 UTC del 16 = 23:00 local del 15.
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 11, 16, 2), Fija(-3));
        Assert.Equal(Utc(2026, 11, 15, 3), r.Desde);
        Assert.Equal(Utc(2026, 11, 16, 3), r.Hasta);
    }

    [Fact]
    public void Utc_mas_13_el_dia_local_empieza_el_dia_anterior_en_utc()
    {
        // 12:00 UTC del 16 = 01:00 local del 17 (UTC+13): hoy es el 17 local, que empieza a las 11:00 UTC del 16.
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 11, 16, 12), Fija(13));
        Assert.Equal(Utc(2026, 11, 16, 11), r.Desde);
        Assert.Equal(Utc(2026, 11, 17, 11), r.Hasta);
    }

    [Fact]
    public void El_rango_incluye_el_desde_y_excluye_el_hasta()
    {
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 11, 16, 12), Fija(-3));
        Assert.True(r.Contiene(r.Desde));
        Assert.False(r.Contiene(r.Hasta));
        Assert.False(r.Contiene(r.Desde.AddTicks(-1)));
        Assert.True(r.Contiene(r.Hasta.AddTicks(-1)));
    }

    [Fact]
    public void Dia_normal_con_horario_de_verano_dura_24_horas()
    {
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 12, 15, 12), ConVerano());
        Assert.Equal(TimeSpan.FromHours(24), r.Hasta - r.Desde);
        // En verano (UTC-2) la medianoche local es 02:00 UTC.
        Assert.Equal(Utc(2026, 12, 15, 2), r.Desde);
    }

    [Fact]
    public void Dia_en_que_entra_el_verano_dura_23_horas_y_la_medianoche_no_existe()
    {
        var zona = ConVerano();
        Assert.True(zona.IsInvalidTime(new DateTime(2026, 10, 4, 0, 0, 0)));
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 10, 4, 12), zona);
        Assert.Equal(TimeSpan.FromHours(23), r.Hasta - r.Desde);
        // El día empieza en el salto: 00:00 en UTC-3 = 03:00 UTC (en ese instante el reloj pasa a marcar 01:00).
        Assert.Equal(Utc(2026, 10, 4, 3), r.Desde);
        // Y el día siguiente empieza a las 02:00 UTC (ya con UTC-2).
        Assert.Equal(Utc(2026, 10, 5, 2), r.Hasta);
    }

    [Fact]
    public void Dia_en_que_sale_el_verano_dura_25_horas_y_empieza_en_la_primera_medianoche()
    {
        var zona = ConVerano();
        Assert.True(zona.IsAmbiguousTime(new DateTime(2026, 3, 1, 0, 30, 0)));
        var r = DiaLocal.RangoUtcDeHoy(Utc(2026, 3, 1, 12), zona);
        Assert.Equal(TimeSpan.FromHours(25), r.Hasta - r.Desde);
        // Primera medianoche = con UTC-2 (verano) = 02:00 UTC.
        Assert.Equal(Utc(2026, 3, 1, 2), r.Desde);
        Assert.Equal(Utc(2026, 3, 2, 3), r.Hasta);
        // La segunda pasada por las 00:30 (03:30 UTC) sigue siendo el mismo día.
        Assert.True(r.Contiene(Utc(2026, 3, 1, 3, 30)));
    }

    [Fact]
    public void EsDeDiaAnterior_compara_por_dia_local_no_por_utc()
    {
        var zona = Fija(-3);
        var ahora = Utc(2026, 11, 16, 12);
        Assert.True(DiaLocal.EsDeDiaAnterior(Utc(2026, 11, 16, 2, 59), ahora, zona));   // 23:59 del 15 local
        Assert.False(DiaLocal.EsDeDiaAnterior(Utc(2026, 11, 16, 3), ahora, zona));      // 00:00 del 16 local
        Assert.False(DiaLocal.EsDeDiaAnterior(Utc(2026, 11, 17, 1), ahora, zona));      // futuro: no es anterior
    }

    [Fact]
    public void Cambiar_de_zona_cambia_lo_que_cuenta_como_hoy_sin_tocar_las_fechas()
    {
        var ahora = Utc(2026, 11, 16, 1);      // 22:00 del 15 en UTC-3 · 01:00 del 16 en UTC · 03:00 del 16 en UTC+2
        var marca = Utc(2026, 11, 15, 23, 30); // 20:30 del 15 en UTC-3 · 23:30 del 15 en UTC · 01:30 del 16 en UTC+2
        Assert.True(DiaLocal.EsDelMismoDia(marca, ahora, Fija(-3)));
        Assert.False(DiaLocal.EsDelMismoDia(marca, ahora, Fija(0)));
        Assert.True(DiaLocal.EsDelMismoDia(marca, ahora, Fija(2)));
    }

    [Fact]
    public void Rechaza_una_fecha_que_no_es_utc()
        => Assert.Throws<ArgumentException>(() => DiaLocal.RangoUtcDeHoy(DateTime.SpecifyKind(Utc(2026, 11, 16), DateTimeKind.Local), Fija(-3)));
}
