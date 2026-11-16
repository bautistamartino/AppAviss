using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Arranque;

/// <summary>
/// Lecturas de una base SQLite "desde afuera": abre el archivo en SOLO LECTURA (no puede modificarlo) y sin pool de
/// conexiones (así no deja el archivo bloqueado). Sirve para decidir qué hacer con una base antes de tocarla (RF-19, RF-27).
/// </summary>
public static class InspeccionSqlite
{
    public static SqliteConnection AbrirSoloLectura(string ruta)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = ruta, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var cx = new SqliteConnection(cs);
        cx.Open();
        return cx;
    }

    /// <summary>
    /// ¿El archivo es una base SQLite sana? <paramref name="completa"/> usa integrity_check (revisa todo, más lento, para
    /// verificar copias); si no, quick_check (suficiente para decidir al arrancar). Un archivo que no es SQLite da false.
    /// </summary>
    public static bool EstaSana(string ruta, bool completa = false)
    {
        try
        {
            using var cx = AbrirSoloLectura(ruta);
            using var cmd = cx.CreateCommand();
            cmd.CommandText = completa ? "PRAGMA integrity_check" : "PRAGMA quick_check";
            return cmd.ExecuteScalar() is string s && s == "ok";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>Huella de la estructura (tablas, índices, triggers): si dos bases tienen la misma, tienen el mismo esquema.</summary>
    public static string HuellaEsquema(string ruta)
    {
        using var cx = AbrirSoloLectura(ruta);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = "SELECT type, name, tbl_name, COALESCE(sql, '') FROM sqlite_master ORDER BY type, name";
        var sb = new StringBuilder();
        using var lector = cmd.ExecuteReader();
        while (lector.Read())
            sb.Append(lector.GetString(0)).Append('|').Append(lector.GetString(1)).Append('|')
              .Append(lector.GetString(2)).Append('|').Append(lector.GetString(3)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Migraciones de EF registradas en la base, en orden. Una base sin esa tabla (vacía) devuelve una lista vacía.</summary>
    public static IReadOnlyList<string> MigracionesAplicadas(string ruta)
    {
        using var cx = AbrirSoloLectura(ruta);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        var lista = new List<string>();
        try
        {
            using var lector = cmd.ExecuteReader();
            while (lector.Read()) lista.Add(lector.GetString(0));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // "no such table"
        {
            return Array.Empty<string>();
        }
        return lista;
    }
}
