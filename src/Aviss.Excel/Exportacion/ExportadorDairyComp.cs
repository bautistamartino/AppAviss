using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using NPOI.SS.UserModel;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Arma el archivo de vuelta para Dairy Comp (spec 007): toma el original guardado en el rodeo, le agrega a la DERECHA las columnas
/// de <see cref="ColumnasExportacion"/> y devuelve los bytes en CSV o .xls. Nunca modifica el original. Única excepción (regla del
/// 2026-11-24): si el original trae SCDAT/SCTIM, en el archivo nuevo se REEMPLAZA su valor en las vacas con tacto (ver <see cref="PlanColumnasExportacion"/>).
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

        // RF-14: si el original ya trae una columna con el nombre de una nueva que NO se reemplaza (sin distinguir mayúsculas), no hay
        // forma segura de elegir cuál manda: se rechaza sin escribir nada. SCDAT y SCTIM no chocan: si vienen, se reemplazan
        // (regla del 2026-11-24) y si faltan, se crean y se avisa.
        // Se devuelve el nombre de la columna NUEVA que choca (un texto fijo de la app), nunca el encabezado del archivo.
        var plan = PlanColumnasExportacion.Crear(lectura.Encabezados);
        if (plan.Choque is not null)
            return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.ColumnaDuplicada, plan.Choque.Nombre);

        try
        {
            var valoresPorFila = MapaFilasExportacion.Construir(lectura, valoresPorCaravana);

            byte[]? bytes;
            if (formato == FormatoExportacion.Xls)
                bytes = EscritorXls.Escribir(hoja, lectura, valoresPorFila, plan);
            else if (PerfilDairyComp.EsLibroOle2(original))
            {
                bytes = EscritorCsv.DesdeHoja(hoja, lectura, valoresPorFila, plan);
                // Desde un .xls, el único motivo de no poder escribir es un carácter que Latin-1 no tiene: se orienta al .xls.
                if (bytes is null) return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.CsvNoAdmiteCaracteres);
            }
            else
            {
                var csv = LectorCsv.Analizar(original);
                bytes = csv is null ? null : EscritorCsv.DesdeTexto(original, csv, lectura, valoresPorFila, plan);
            }

            if (bytes is null) return ResultadoGeneracionArchivo.Fallido(FalloGeneracion.NoSePudoGenerar);
            return ResultadoGeneracionArchivo.Correcto(
                bytes,
                formato == FormatoExportacion.Csv ? "csv" : "xls",
                lectura.Filas.Count,
                valoresPorFila.Count(v => !v.EsVacio),
                plan.Creadas);
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
