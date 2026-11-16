namespace Aviss.Infrastructure;

/// <summary>
/// Dónde guarda Aviss sus archivos. Todo vive dentro de un único directorio de datos (en Android, el almacenamiento
/// privado de la app; en los tests, una carpeta temporal). Infrastructure no depende de MAUI: el directorio se le pasa.
/// </summary>
public sealed record RutasAviss(string DirectorioDatos)
{
    public string BaseDeDatos => Path.Combine(DirectorioDatos, "aviss.db");

    /// <summary>Archivo donde se arma la base nueva; recién cuando está completa se mueve a <see cref="BaseDeDatos"/> (RF-20).</summary>
    public string BaseDeDatosCreando => Path.Combine(DirectorioDatos, "aviss.db.creando");

    public string DirectorioBackups => Path.Combine(DirectorioDatos, "backups");
    public string DirectorioDanadas => Path.Combine(DirectorioDatos, "danadas");

    /// <summary>Identificador de esta tablet (RF-23). Está fuera de la base a propósito.</summary>
    public string ArchivoDispositivo => Path.Combine(DirectorioDatos, "dispositivo.id");
}
