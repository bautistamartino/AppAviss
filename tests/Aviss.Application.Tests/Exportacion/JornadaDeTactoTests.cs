using Aviss.Application.Exportacion;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Spec 007, RF-12 y RF-22: solo se exporta el día local del registro vigente más reciente del rodeo.</summary>
public class JornadaDeTactoTests
{
    private static TimeZoneInfo Fija(double h) => TimeZoneInfo.CreateCustomTimeZone("z" + h, TimeSpan.FromHours(h), "z", "z");

    [Fact]
    public void Sin_registros_no_hay_jornada()
    {
        var j = JornadaDeTacto.Filtrar([], TimeZoneInfo.Utc);
        Assert.Null(j.Dia);
        Assert.Empty(j.Registros);
    }

    [Fact]
    public void La_jornada_es_el_dia_del_registro_mas_reciente_y_los_de_dias_anteriores_se_descartan()
    {
        var anterior = R.Reg("1", R.Utc(2026, 11, 4, 15));
        var a = R.Reg("2", R.Utc(2026, 11, 5, 9), orden: 2);
        var b = R.Reg("3", R.Utc(2026, 11, 5, 18), orden: 3);

        var j = JornadaDeTacto.Filtrar([anterior, a, b], TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 11, 5), j.Dia);
        Assert.Equal(new[] { "2", "3" }, j.Registros.Select(r => r.CaravanaClave).OrderBy(x => x));
    }

    [Fact]
    public void El_dia_se_calcula_en_hora_local_no_en_UTC()
    {
        // En UTC-3: 02:00 UTC del 6 es 23:00 local del 5, y 12:00 UTC del 5 es 09:00 local del 5: misma jornada.
        var tarde = R.Reg("1", R.Utc(2026, 11, 6, 2), orden: 2);
        var temprano = R.Reg("2", R.Utc(2026, 11, 5, 12), orden: 1);
        var diaAnterior = R.Reg("3", R.Utc(2026, 11, 5, 2), orden: 0); // 23:00 local del 4

        var j = JornadaDeTacto.Filtrar([tarde, temprano, diaAnterior], Fija(-3));

        Assert.Equal(new DateOnly(2026, 11, 5), j.Dia);
        Assert.Equal(2, j.Registros.Count);
        Assert.DoesNotContain(j.Registros, r => r.CaravanaClave == "3");
    }

    [Fact]
    public void Pasada_la_medianoche_de_la_exportacion_la_jornada_sigue_siendo_la_del_ultimo_registro()
    {
        // La jornada no depende de cuándo se exporta: depende de los registros.
        var j = JornadaDeTacto.Filtrar([R.Reg("1", R.Utc(2026, 11, 5, 20))], Fija(-3));
        Assert.Equal(new DateOnly(2026, 11, 5), j.Dia);
        Assert.Single(j.Registros);
    }

    [Fact]
    public void El_limite_de_la_medianoche_local_separa_las_jornadas()
    {
        // 02:59 UTC del 6 = 23:59 local del 5; 03:00 UTC del 6 = 00:00 local del 6.
        var justoAntes = R.Reg("1", R.Utc(2026, 11, 6, 2, 59), orden: 1);
        var justoDespues = R.Reg("2", R.Utc(2026, 11, 6, 3, 0), orden: 2);

        var j = JornadaDeTacto.Filtrar([justoAntes, justoDespues], Fija(-3));

        Assert.Equal(new DateOnly(2026, 11, 6), j.Dia);
        Assert.Equal("2", Assert.Single(j.Registros).CaravanaClave);
    }
}
