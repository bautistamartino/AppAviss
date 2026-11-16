using Aviss.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Arranque;

/// <summary>
/// Las migraciones de la base detrás de una interfaz, para poder probar el arranque con un migrador falso que falla a
/// mitad (con una sola migración real no hay forma de probar una actualización de verdad). Trabaja sobre una RUTA
/// porque el arranque crea la base en un archivo temporal antes de moverla.
/// </summary>
public interface IMigrador
{
    /// <summary>Todas las migraciones que esta versión de la app conoce, en orden.</summary>
    IReadOnlyList<string> Conocidas();

    /// <summary>Las que ya tiene aplicadas la base de esa ruta (lectura sin modificar nada).</summary>
    IReadOnlyList<string> Aplicadas(string rutaBase);

    /// <summary>Aplica las pendientes (crea la base si no existe).</summary>
    Task AplicarAsync(string rutaBase, CancellationToken ct = default);
}

/// <summary>Implementación con EF Core.</summary>
public sealed class MigradorEf : IMigrador
{
    public IReadOnlyList<string> Conocidas()
    {
        // GetMigrations lee las migraciones del ensamblado: no abre ninguna conexión, así que la ruta es indiferente.
        using var ctx = Nuevo(Path.Combine(Path.GetTempPath(), "aviss-solo-para-listar.db"));
        return ctx.Database.GetMigrations().ToList();
    }

    public IReadOnlyList<string> Aplicadas(string rutaBase) => InspeccionSqlite.MigracionesAplicadas(rutaBase);

    public async Task AplicarAsync(string rutaBase, CancellationToken ct = default)
    {
        try
        {
            using var ctx = Nuevo(rutaBase);
            await ctx.Database.MigrateAsync(ct);
        }
        finally
        {
            SqliteConnection.ClearAllPools(); // que el archivo no quede bloqueado (Windows) para moverlo o restaurarlo
        }
    }

    private static AvissDbContext Nuevo(string ruta)
    {
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, ruta);
        return new AvissDbContext(opciones.Options);
    }
}
