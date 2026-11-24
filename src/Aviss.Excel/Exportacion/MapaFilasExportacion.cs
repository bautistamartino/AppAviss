using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Decide qué valores recibe cada fila del original (spec 007). Usa EXACTAMENTE las reglas de la importación
/// (<see cref="ReglasIdImportacion"/> y la clave de caravana), así una fila recibe valores solo si en la base existe como animal:
/// una fila con ID inválido y TODAS las de un ID repetido quedan sin valores, igual que la fila Total (que ni siquiera figura en
/// <see cref="LecturaExcel.Filas"/>).
/// </summary>
internal static class MapaFilasExportacion
{
    /// <summary>Un arreglo paralelo a <see cref="LecturaExcel.Filas"/>: los valores de cada fila, o <see cref="ValoresExportacion.Vacios"/>.</summary>
    public static ValoresExportacion[] Construir(LecturaExcel lectura, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        var claves = new string?[lectura.Filas.Count];
        var apariciones = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int k = 0; k < lectura.Filas.Count; k++)
        {
            lectura.Filas[k].Celdas.TryGetValue(lectura.NombreColumnaId, out var celdaId);
            var id = ReglasIdImportacion.Evaluar(celdaId);
            if (!id.EsValido) continue;
            claves[k] = id.Caravana!.Clave;
            apariciones[claves[k]!] = apariciones.GetValueOrDefault(claves[k]!) + 1;
        }

        var resultado = new ValoresExportacion[lectura.Filas.Count];
        for (int k = 0; k < resultado.Length; k++)
        {
            var clave = claves[k];
            resultado[k] = clave is not null && apariciones[clave] == 1 && valoresPorCaravana.TryGetValue(clave, out var valores)
                ? valores
                : ValoresExportacion.Vacios;
        }
        return resultado;
    }
}
