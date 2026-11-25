using Aviss.Application.Exportacion;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Regla del 2026-11-24 (spec 007): SCDAT y SCTIM se reemplazan si el original las trae; las demás columnas nuevas no.</summary>
public class ColumnasExportacionTests
{
    [Fact]
    public void Solo_SCDAT_y_SCTIM_reemplazan_si_existen()
    {
        Assert.Equal(["SCDAT", "SCTIM"], ColumnasExportacion.Todas.Where(c => c.ReemplazaSiExiste).Select(c => c.Nombre));
        Assert.Equal(["Evento1", "Evento2", "DIASPRE", "DIASABO", "RESABO"], ColumnasExportacion.Todas.Where(c => !c.ReemplazaSiExiste).Select(c => c.Nombre));
    }

    [Fact]
    public void El_aviso_de_columnas_creadas_nombra_solo_las_columnas_fijas()
    {
        Assert.Equal("", MensajesExportacion.ColumnasCreadas([]));
        Assert.Contains("la columna SCTIM", MensajesExportacion.ColumnasCreadas(["SCTIM"]));
        Assert.Contains("las columnas SCDAT y SCTIM", MensajesExportacion.ColumnasCreadas(["SCDAT", "SCTIM"]));
    }

    [Fact]
    public void Un_resultado_correcto_sin_columnas_creadas_trae_la_lista_vacia()
    {
        var r = ResultadoGeneracionArchivo.Correcto([1], "csv", 1, 0);
        Assert.Empty(r.ColumnasCreadas!);
    }
}
