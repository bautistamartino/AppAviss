using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using NPOI.SS.UserModel;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Arma el archivo de vuelta para Dairy Comp (spec 007): toma el original guardado en el rodeo, le agrega a la DERECHA las columnas
/// de <see cref="ColumnasExportacion"/> y devuelve los bytes en CSV o .xls. Nunca modifica el original ni reemplaza una columna suya.
/// Abre el original con las mismas reglas que la importación (<see cref="PerfilDairyComp"/>), así una fila recibe valores solo si
/// en la base existe como animal. Nunca lanza ni devuelve datos del archivo en sus fallos: solo un <see cref="FalloGeneracion"/>.
/// No guarda estado (singleton).
/// </summary>
public sealed class ExportadorDairyComp : IExportadorArchivo
{
    public ResultadoGeneracionArchivo Generar(byte[] original, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        ISheet hoja;
        LecturaExcel lectura;
        try
        {
            var abierta = PerfilDairyComp.AbrirHoja(original, out _);
            if (abierta is null) return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.OriginalIlegible);
            hoja = abierta;

            var leida = PerfilDairyComp.LeerHoja(hoja, LimitesImportacion.MaxFilas);
            if (!leida.EsCorrecto) return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.OriginalIlegible);
            lectura = leida.Lectura!;
        }
        catch (Exception)
        {
            return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.OriginalIlegible);
        }

        // RF-14: si el original ya trae una columna con el nombre de una nueva (sin distinguir mayúsculas), no hay forma segura de
        // elegir cuál manda: se rechaza sin escribir nada.
        if (lectura.Encabezados.Any(e => ColumnasExportacion.Todas.Any(c => string.Equals(c.Nombre, e, StringComparison.OrdinalIgnoreCase))))
            return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.ColumnaDuplicada);

        try
        {
            var valoresPorFila = MapaFilasExportacion.Construir(lectura, valoresPorCaravana);

            byte[]? bytes;
            if (formato == FormatoExportacion.Xls)
                bytes = EscritorXls.Escribir(hoja, lectura, valoresPorFila);
            else if (PerfilDairyComp.EsLibroOle2(original))
                bytes = EscritorCsv.DesdeHoja(hoja, lectura, valoresPorFila);
            else
            {
                var csv = LectorCsv.Analizar(original);
                bytes = csv is null ? null : EscritorCsv.DesdeTexto(original, csv, lectura, valoresPorFila);
            }

            if (bytes is null) return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.NoSePudoGenerar);
            return ResultadoGeneracionArchivo.Correcto(
                bytes,
                formato == FormatoExportacion.Csv ? "csv" : "xls",
                lectura.Filas.Count,
                valoresPorFila.Count(v => !v.EsVacio));
        }
        catch (Exception)
        {
            // Sin mensajes de la excepción: podrían traer datos del archivo.
            return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.NoSePudoGenerar);
        }
    }

    public ResultadoValidacionArchivo Validar(byte[] original, byte[] generado, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        try
        {
            return ValidadorExportacion.Validar(original, generado, formato, valoresPorCaravana);
        }
        catch (Exception)
        {
            return new ResultadoValidacionArchivo(DiferenciaExportacion.ArchivoIlegible);
        }
    }
}
