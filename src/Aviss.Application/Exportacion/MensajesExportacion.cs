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

    public static string Para(FalloGuardado fallo) => fallo switch
    {
        FalloGuardado.SinEspacio => "No hay espacio suficiente en la tablet para guardar el archivo. No se guardó nada.",
        FalloGuardado.SinPermiso => "La tablet no permitió guardar el archivo. No se guardó nada.",
        FalloGuardado.NoDisponible => "Esta versión de la app todavía no puede guardar archivos. No se guardó nada.",
        _ => "No se pudo guardar el archivo. No quedó nada a medias."
    };
}
