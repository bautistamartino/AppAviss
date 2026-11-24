using Aviss.Domain.Enums;

namespace Aviss.Application.Exportacion;

/// <summary>Por qué no se pudo generar el archivo de vuelta. Ningún caso trae datos del tambo (RF-27).</summary>
public enum FalloGeneracion
{
    /// <summary>El original guardado en el rodeo no se puede leer (dañado, sin ID o RPRO, etc.).</summary>
    OriginalIlegible,

    /// <summary>El original ya trae una columna con el nombre de una de las nuevas (RF-14): no hay forma segura de elegir cuál manda.</summary>
    ColumnaDuplicada,

    /// <summary>No se pudo escribir el archivo (por ejemplo, un texto con caracteres que la codificación del CSV original no admite).</summary>
    NoSePudoGenerar,

    /// <summary>Se pidió CSV pero el original tiene un carácter que la codificación del CSV (Latin-1) no admite: en .xls sí se puede.</summary>
    CsvNoAdmiteCaracteres
}

/// <summary>El archivo generado, o el motivo por el que no se pudo. Un resultado correcto trae bytes; uno fallido, el motivo.</summary>
/// <param name="Contenido">Los bytes del archivo nuevo.</param>
/// <param name="Extension">Sin punto: <c>csv</c> o <c>xls</c>.</param>
/// <param name="Filas">Filas de datos del original que quedan en el archivo (sin la fila Total).</param>
/// <param name="FilasConValores">Cuántas de esas filas llevan al menos un valor en las columnas nuevas.</param>
/// <param name="ColumnaDuplicada">Solo con <see cref="FalloGeneracion.ColumnaDuplicada"/>: el nombre de la columna NUEVA que ya existía (un nombre fijo de la app, nunca un dato del tambo).</param>
public sealed record ResultadoGeneracionArchivo(byte[]? Contenido, string Extension, int Filas, int FilasConValores, FalloGeneracion? Fallo, string? ColumnaDuplicada = null)
{
    public bool EsCorrecto => Fallo is null && Contenido is not null;

    public static ResultadoGeneracionArchivo Correcto(byte[] contenido, string extension, int filas, int filasConValores)
        => new(contenido, extension, filas, filasConValores, null);

    public static ResultadoGeneracionArchivo Fallido(FalloGeneracion fallo, string? columnaDuplicada = null) => new(null, "", 0, 0, fallo, columnaDuplicada);
}

/// <summary>Qué encontró la validación (RF-18). Solo categorías: nunca se escribe un valor del archivo.</summary>
public enum DiferenciaExportacion
{
    /// <summary>El archivo generado no se pudo volver a abrir.</summary>
    ArchivoIlegible,

    /// <summary>No coincide la cantidad de filas con el original.</summary>
    FilasDistintas,

    /// <summary>Los encabezados no son los originales seguidos de las columnas nuevas, en ese orden.</summary>
    ColumnasDistintas,

    /// <summary>Alguna celda original cambió.</summary>
    ValorOriginalCambiado,

    /// <summary>Alguna columna nueva no tiene el valor que corresponde a esa vaca.</summary>
    ValorNuevoIncorrecto
}

/// <summary>El resultado de reabrir y comparar el archivo generado: válido, o la primera diferencia hallada.</summary>
public sealed record ResultadoValidacionArchivo(DiferenciaExportacion? Diferencia)
{
    public bool EsValido => Diferencia is null;
    public static ResultadoValidacionArchivo Valido { get; } = new((DiferenciaExportacion?)null);
}

/// <summary>
/// Genera el archivo de vuelta para Dairy Comp: el original más las columnas nuevas a la derecha (spec 007, RF-4 y RF-5), y lo
/// valida reabriéndolo (RF-18). Se implementa en <c>Aviss.Excel</c>; las dos operaciones usan CPU y no tocan la base ni el
/// disco, así que el servicio las corre fuera del hilo de la pantalla.
/// </summary>
public interface IExportadorArchivo
{
    /// <param name="original">Los bytes del original guardados en el rodeo; no se modifican.</param>
    /// <param name="formato">El formato pedido por el veterinario: puede ser distinto del del original.</param>
    /// <param name="valoresPorCaravana">Los valores de cada vaca con evento, por <c>Caravana.Clave</c>; la que no figura sale vacía.</param>
    ResultadoGeneracionArchivo Generar(byte[] original, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana);

    /// <summary>Reabre <paramref name="generado"/> y lo compara contra el original y contra los valores esperados.</summary>
    ResultadoValidacionArchivo Validar(byte[] original, byte[] generado, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana);
}
