using Aviss.Domain.Enums;

namespace Aviss.Application.Exportacion;

/// <summary>
/// Un archivo de vuelta ya guardado en el dispositivo. <paramref name="Nombre"/> es el nombre FINAL (puede diferir del sugerido si ya
/// existía uno igual y no se lo quiso pisar), <paramref name="Lugar"/> es el texto que ve el veterinario ("Descargas") y
/// <paramref name="Referencia"/> es lo que la implementación necesita para compartirlo después (en Android, un <c>content://</c>).
/// </summary>
public sealed record ArchivoGuardado(string Nombre, string Lugar, string Referencia);

/// <summary>Por qué no se pudo guardar el archivo (RF-24). Solo categorías: ningún caso trae datos del tambo.</summary>
public enum FalloGuardado
{
    SinEspacio,
    SinPermiso,
    /// <summary>Esta versión de la app todavía no sabe guardar archivos (la implementación real es de Android).</summary>
    NoDisponible,
    Otro
}

/// <summary>El archivo guardado, o el motivo por el que no se pudo.</summary>
public sealed record ResultadoGuardadoArchivo(ArchivoGuardado? Archivo, FalloGuardado? Fallo)
{
    public bool EsCorrecto => Archivo is not null && Fallo is null;
    public static ResultadoGuardadoArchivo Correcto(ArchivoGuardado archivo) => new(archivo, null);
    public static ResultadoGuardadoArchivo Fallido(FalloGuardado fallo) => new(null, fallo);
}

/// <summary>
/// Entrega del archivo de vuelta (spec 007, RF-19, RF-20, RF-24): guardarlo en un lugar que el veterinario encuentre y ofrecerlo para
/// compartir. La implementación real vive en <c>Platforms/Android</c> (constitución §5); acá solo está el contrato.
/// </summary>
public interface IEntregaArchivo
{
    /// <summary>
    /// Guarda el archivo con un nombre que NO pise ningún archivo existente. Mientras se escribe no debe verse a medias, y si falla no
    /// debe dejar nada parcial. Si se cancela, lanza <see cref="OperationCanceledException"/> sin dejar nada.
    /// </summary>
    Task<ResultadoGuardadoArchivo> GuardarAsync(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct = default);

    /// <summary>Abre las opciones de compartir del sistema para un archivo ya guardado. No usa la red desde la app (RF-20).</summary>
    Task CompartirAsync(ArchivoGuardado archivo, CancellationToken ct = default);
}
