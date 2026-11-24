using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Spec 007, RF-19: el nombre nuevo del archivo de vuelta, derivado del original.</summary>
public class NombreArchivoExportadoTests
{
    private static TimeZoneInfo Fija(double h) => TimeZoneInfo.CreateCustomTimeZone("z" + h, TimeSpan.FromHours(h), "z", "z");
    private static readonly DateTime Ahora = new(2026, 11, 24, 18, 5, 0, DateTimeKind.Utc);   // 15:05 en UTC-3

    [Fact]
    public void Sigue_el_formato_original_aviss_fecha_hora_extension()
        => Assert.Equal("BAUTI1_aviss_20261124_1505.csv", NombreArchivoExportado.Crear("BAUTI1.csv", Ahora, Fija(-3), FormatoExportacion.Csv));

    [Fact]
    public void La_extension_es_la_del_formato_pedido_no_la_del_original()
    {
        Assert.Equal("rodeo_aviss_20261124_1505.xls", NombreArchivoExportado.Crear("rodeo.csv", Ahora, Fija(-3), FormatoExportacion.Xls));
        Assert.Equal("rodeo_aviss_20261124_1505.csv", NombreArchivoExportado.Crear("rodeo.xls", Ahora, Fija(-3), FormatoExportacion.Csv));
    }

    [Fact]
    public void La_fecha_y_la_hora_son_las_locales()
        => Assert.Equal("a_aviss_20261125_0100.csv", NombreArchivoExportado.Crear("a.csv", new DateTime(2026, 11, 25, 4, 0, 0, DateTimeKind.Utc), Fija(-3), FormatoExportacion.Csv));

    [Theory]
    [InlineData("vacas.2026.xls", "vacas.2026_aviss_20261124_1505.csv")]   // solo se saca la última extensión
    [InlineData("sin_extension", "sin_extension_aviss_20261124_1505.csv")]
    [InlineData("  con espacios .txt", "con espacios_aviss_20261124_1505.csv")]
    public void Solo_se_saca_la_ultima_extension(string original, string esperado)
        => Assert.Equal(esperado, NombreArchivoExportado.Crear(original, Ahora, Fija(-3), FormatoExportacion.Csv));

    [Theory]
    [InlineData("a/b\\c:d*e.xls")]
    [InlineData("<>|?\".xls")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nunca_trae_caracteres_prohibidos_en_un_nombre_de_archivo_y_nunca_queda_vacio(string? original)
    {
        var n = NombreArchivoExportado.Crear(original, Ahora, Fija(-3), FormatoExportacion.Csv);
        Assert.EndsWith("_aviss_20261124_1505.csv", n);
        Assert.True(n.Length > "_aviss_20261124_1505.csv".Length);
        Assert.DoesNotContain(n, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void Con_un_nombre_larguisimo_el_resultado_sigue_siendo_razonable()
        => Assert.True(NombreArchivoExportado.Crear(new string('x', 500) + ".xls", Ahora, Fija(-3), FormatoExportacion.Xls).Length <= 120);
}
