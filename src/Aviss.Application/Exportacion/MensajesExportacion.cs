namespace Aviss.Application.Exportacion;

/// <summary>Mensajes fijos en español de la exportación. Nunca llevan caravanas, nombres de tambo ni valores del archivo (RF-27).</summary>
public static class MensajesExportacion
{
    public const string RodeoNoEncontrado = "El rodeo no existe o está dado de baja.";
    public const string OriginalIlegible = "No se pudo leer el archivo original guardado en el rodeo. No se generó nada.";
    public const string NoSePudoGenerar = "No se pudo generar el archivo. No se guardó nada; probá de nuevo.";
    public const string ExportacionNoValida = "El archivo generado no coincide con el original, así que no se entregó. No se guardó nada; probá de nuevo.";
    public const string ConstanciaNoGuardada = "El archivo se guardó, pero no quedó registrada la exportación en la app.";

    public const string CsvNoAdmiteCaracteres = "El archivo tiene un carácter que el CSV no admite. Exportalo en Excel (.xls). No se generó nada.";

    /// <summary>RF-14: nombra la columna nueva que chocó (un nombre fijo de la app, no un dato del tambo).</summary>
    public static string ColumnaDuplicada(string columna)
        => $"El archivo original ya trae una columna llamada {columna}, que es una de las nuevas. No se generó nada.";

    /// <summary>
    /// Aviso (no error) cuando el original no traía SCDAT y/o SCTIM y se crearon como columnas nuevas. Solo nombra columnas fijas
    /// de la app, nunca datos del archivo.
    /// </summary>
    public static string ColumnasCreadas(IReadOnlyList<string> columnas) => columnas.Count switch
    {
        0 => "",
        1 => $"El archivo original no traía la columna {columnas[0]}: se creó nueva a la derecha. Revisá que Dairy Comp la tome al importar.",
        _ => $"El archivo original no traía las columnas {string.Join(" y ", columnas)}: se crearon nuevas a la derecha. Revisá que Dairy Comp las tome al importar."
    };

    public static string Para(FalloGuardado fallo) => fallo switch
    {
        FalloGuardado.SinEspacio => "No hay espacio suficiente en la tablet para guardar el archivo. No se guardó nada.",
        FalloGuardado.SinPermiso => "La tablet no permitió guardar el archivo. No se guardó nada.",
        FalloGuardado.NoDisponible => "Esta versión de la app todavía no puede guardar archivos. No se guardó nada.",
        _ => "No se pudo guardar el archivo. No quedó nada a medias."
    };
}
