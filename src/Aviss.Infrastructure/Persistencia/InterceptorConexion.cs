using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aviss.Infrastructure.Persistencia;

/// <summary>La base no puede exigir las claves foráneas: la app no debe abrirla (RF-16).</summary>
public sealed class ForeignKeysNoActivasException : Exception
{
    public ForeignKeysNoActivasException()
        : base("No se pudo activar la verificación de relaciones de la base de datos.") { }
}

/// <summary>
/// Se ejecuta cada vez que EF abre una conexión. Los PRAGMA de SQLite valen por conexión (no se guardan en el archivo,
/// salvo journal_mode), por eso hay que repetirlos en cada apertura:
/// foreign_keys=ON (relaciones), WAL (escrituras que no bloquean lecturas), synchronous=FULL (no perder un tacto
/// confirmado si se corta la energía). La espera ante bloqueos (5 s) NO va por PRAGMA busy_timeout: Microsoft.Data.Sqlite
/// maneja su propio reintento con el "Default Timeout" de la cadena de conexión (ver ConfiguracionDb).
/// Después comprueba que foreign_keys quedó en 1; si no, lanza una excepción propia.
/// </summary>
public sealed class InterceptorConexion : DbConnectionInterceptor
{
    public static readonly InterceptorConexion Instancia = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Configurar(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Configurar(connection);
        return Task.CompletedTask;
    }

    private static void Configurar(DbConnection conexion)
    {
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
            cmd.ExecuteNonQuery();
        }
        VerificarForeignKeys(conexion);
    }

    /// <summary>Lanza <see cref="ForeignKeysNoActivasException"/> si <c>PRAGMA foreign_keys</c> no devuelve 1.</summary>
    public static void VerificarForeignKeys(DbConnection conexion)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys";
        var valor = cmd.ExecuteScalar();
        if (valor is null || Convert.ToInt64(valor) != 1)
            throw new ForeignKeysNoActivasException();
    }
}
