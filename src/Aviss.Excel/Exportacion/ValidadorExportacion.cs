using System.Globalization;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// RF-18 (y constitución §2): reabre el archivo generado con el MISMO perfil de importación y lo compara contra la lectura del
/// original y contra los valores esperados. Cualquier diferencia devuelve solo su categoría, nunca un valor del archivo.
/// <list type="bullet">
/// <item>Misma cantidad de filas y misma fila Total declarada.</item>
/// <item>Encabezados: los originales, en el mismo orden, seguidos de las columnas nuevas.</item>
/// <item>Cada celda original igual. Si el formato no cambió se compara también el tipo (texto, número, fecha); si cambió (de .xls a CSV
/// o al revés) se compara el valor: los espacios de los bordes y <c>5</c> contra <c>5.0</c> no cuentan como diferencia, porque el CSV
/// no guarda tipos.</item>
/// <item>Cada columna nueva con el valor esperado de esa vaca (vacío si no tiene registro).</item>
/// </list>
/// </summary>
internal static class ValidadorExportacion
{
    public static ResultadoValidacionArchivo Validar(
        byte[] original, byte[] generado, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        var perfil = new PerfilDairyComp();
        var lecturaOriginal = perfil.Leer(original, LimitesImportacion.MaxFilas);
        var lecturaGenerada = perfil.Leer(generado, LimitesImportacion.MaxFilas);
        if (!lecturaOriginal.EsCorrecto || !lecturaGenerada.EsCorrecto)
            return Invalido(DiferenciaExportacion.ArchivoIlegible);

        var a = lecturaOriginal.Lectura!;
        var b = lecturaGenerada.Lectura!;

        // Encabezados: originales + nuevos, exactamente.
        var esperados = a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)).ToList();
        if (!b.Encabezados.SequenceEqual(esperados, StringComparer.Ordinal))
            return Invalido(DiferenciaExportacion.ColumnasDistintas);

        if (a.Filas.Count != b.Filas.Count || a.TotalDeclarado != b.TotalDeclarado)
            return Invalido(DiferenciaExportacion.FilasDistintas);

        bool mismoFormato = PerfilDairyComp.EsLibroOle2(original) == (formato == FormatoExportacion.Xls);
        var valoresEsperados = MapaFilasExportacion.Construir(a, valoresPorCaravana);

        for (int k = 0; k < a.Filas.Count; k++)
        {
            foreach (var encabezado in a.Encabezados)
            {
                a.Filas[k].Celdas.TryGetValue(encabezado, out var celdaA);
                b.Filas[k].Celdas.TryGetValue(encabezado, out var celdaB);
                if (!CeldasIguales(celdaA, celdaB, mismoFormato))
                    return Invalido(DiferenciaExportacion.ValorOriginalCambiado);
            }

            var textos = valoresEsperados[k].EnOrden();
            for (int j = 0; j < ColumnasExportacion.Todas.Count; j++)
            {
                b.Filas[k].Celdas.TryGetValue(ColumnasExportacion.Todas[j].Nombre, out var celdaNueva);
                if (!string.Equals(TextoONada(celdaNueva), textos[j], StringComparison.Ordinal))
                    return Invalido(DiferenciaExportacion.ValorNuevoIncorrecto);
            }
        }

        return ResultadoValidacionArchivo.Valido;
    }

    private static ResultadoValidacionArchivo Invalido(DiferenciaExportacion diferencia) => new(diferencia);

    /// <summary>El texto de la celda, o "" si no existe o está vacía (una celda en blanco y una que falta son lo mismo).</summary>
    private static string TextoONada(CeldaLeida? celda) => celda is null || celda.EsVacia ? "" : celda.Texto;

    private static bool CeldasIguales(CeldaLeida? a, CeldaLeida? b, bool mismoFormato)
    {
        bool vaciaA = a is null || a.EsVacia, vaciaB = b is null || b.EsVacia;
        if (vaciaA || vaciaB) return vaciaA && vaciaB;

        if (mismoFormato)
            return a!.Tipo == b!.Tipo && string.Equals(a.Texto, b.Texto, StringComparison.Ordinal);

        // Entre formatos distintos el CSV no guarda tipos: se compara el valor.
        if (string.Equals(a!.Texto.Trim(), b!.Texto.Trim(), StringComparison.Ordinal)) return true;
        return double.TryParse(a.Texto.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b.Texto.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && x == y;
    }
}
