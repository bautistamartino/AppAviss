using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// Spec 010, RF-18: exportar un rodeo cuyo original trae la columna EID la conserva sin tocarla y agrega las columnas nuevas a la
/// derecha; EID no es una columna repetida y el archivo generado pasa la validación. Del archivo real NO se prueba ni se fija nada
/// sobre SCDAT y SCTIM (pendiente aparte, spec 010): por eso la muestra se usa SIN esas dos columnas.
/// </summary>
public class ExportadorEidTests
{
    /// <summary>La muestra con EID, sin las dos últimas columnas de datos (SCDAT y SCTIM), en Windows-1252 tal cual.</summary>
    private static byte[] CsvConEidSinScdat()
    {
        var lineas = Encoding.Latin1.GetString(Muestra("dairy-comp-eid-282-vacas.csv")).Split('\n', StringSplitOptions.RemoveEmptyEntries);   // la muestra termina las líneas con \n
        // Encabezado y filas: ID;EID;CVET;LACT;FPART;DEL;FCELO;UTORO;RPRO;FAPAR;LECHE;SCDAT;SCTIM; -> se sacan las posiciones 11 y 12.
        // La fila Total (Total:;282;;;...) también trae 13 campos: pierde dos como las demás y sigue valiendo 282.
        var sin = lineas.Select(l =>
        {
            var campos = l.Split(';').ToList();
            if (campos.Count >= 13) campos.RemoveRange(11, 2);
            return string.Join(';', campos);
        });
        return Encoding.Latin1.GetBytes(string.Join('\n', sin) + "\n");
    }

    private static byte[] XlsConEid()
        => GeneradorMuestras.Construir(
            ["ID", "EID", "CVET", "RPRO"],
            [
                ["500", 999000458013169d, "PRE", "INSEMIN"],     // EID numérico (así viene en un .xls)
                ["501", "999 000 458000000", "0", "SECA"],        // EID con espacios, como texto
                ["502", "-", "FRES", "FRESCA"],                   // sin EID
            ]);

    private static Dictionary<string, ValoresExportacion> ValoresCsv() => new()
    {
        [Clave("30092")] = Previa,
        [Clave("30036")] = Ok,
    };

    private static string Normalizada(Aviss.Application.Importacion.FilaLeida fila, string columna)
        => fila.Celdas.TryGetValue(columna, out var c) && !c.EsVacia ? c.Texto.Trim() : "";

    [Theory]
    [InlineData(FormatoExportacion.Csv)]
    [InlineData(FormatoExportacion.Xls)]
    public void El_CSV_con_EID_conserva_la_columna_y_agrega_las_nuevas_a_la_derecha(FormatoExportacion formato)
    {
        var original = CsvConEidSinScdat();
        var valores = ValoresCsv();

        var generado = GenerarOk(original, formato, valores);

        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal("EID", a.Encabezados[1]);
        Assert.Equal(a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)), b.Encabezados);
        Assert.Equal(a.Filas.Count, b.Filas.Count);
        for (int k = 0; k < a.Filas.Count; k++)
            Assert.Equal(Normalizada(a.Filas[k], "EID"), Normalizada(b.Filas[k], "EID"));
        var validacion = new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores);
        Assert.True(validacion.EsValido, $"La validación falló: {validacion.Diferencia}");
    }

    [Theory]
    [InlineData(FormatoExportacion.Csv)]
    [InlineData(FormatoExportacion.Xls)]
    public void El_xls_con_EID_se_exporta_sin_tocar_la_columna(FormatoExportacion formato)
    {
        var original = XlsConEid();
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("500")] = Previa };

        var generado = GenerarOk(original, formato, valores);

        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal(a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)), b.Encabezados);
        for (int k = 0; k < a.Filas.Count; k++)
            Assert.Equal(Normalizada(a.Filas[k], "EID"), Normalizada(b.Filas[k], "EID"));
        var validacion = new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores);
        Assert.True(validacion.EsValido, $"La validación falló: {validacion.Diferencia}");
    }
}
