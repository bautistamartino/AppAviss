using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Describe el esquema de una base SQLite como un conjunto de "hechos" por tabla (columnas, claves foráneas, índices y CHECK
/// por nombre), para comparar la base migrada contra la que crea EF desde el modelo (EnsureCreated). No compara el texto del
/// DDL (el orden de las columnas puede variar entre una tabla creada y una agregada por migraciones) sino su estructura.
/// Los triggers no se incluyen: EnsureCreated no los crea (viven en las migraciones) y se comparan con una lista explícita.
/// </summary>
public static class DescripcionEsquema
{
    public static SortedDictionary<string, SortedSet<string>> Describir(SqliteConnection cx)
    {
        var resultado = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var tabla in Lista(cx, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrations%'"))
        {
            var hechos = new SortedSet<string>(StringComparer.Ordinal);

            // Columnas (table_xinfo incluye las generadas).
            foreach (var fila in Filas(cx, $"SELECT name, type, \"notnull\", COALESCE(dflt_value, ''), pk, hidden FROM pragma_table_xinfo('{tabla}')"))
                hechos.Add("col:" + string.Join("|", fila));

            // Claves foráneas (una compuesta aparece como una fila por columna).
            foreach (var fila in Filas(cx, $"SELECT \"table\", \"from\", \"to\", on_delete FROM pragma_foreign_key_list('{tabla}')"))
                hechos.Add("fk:" + string.Join("|", fila));

            // Índices: los creados con CREATE INDEX por nombre (con filtro si es parcial); los de UNIQUE/PK por sus columnas.
            foreach (var (nombre, unico, origen, parcial) in IndicesDe(cx, tabla))
            {
                var columnas = string.Join(",", Lista(cx, $"SELECT name FROM pragma_index_info('{nombre}') ORDER BY seqno"));
                if (origen == "c")
                {
                    var sql = Lista(cx, $"SELECT COALESCE(sql, '') FROM sqlite_master WHERE type = 'index' AND name = '{nombre}'").Single();
                    var filtro = parcial ? Normalizar(sql[(sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase) + 7)..]) : "";
                    hechos.Add($"idx:{nombre}|unico={unico}|cols={columnas}|filtro={filtro}");
                }
                else
                {
                    hechos.Add($"uq:{origen}|cols={columnas}");
                }
            }

            // CHECK por nombre, con su expresión normalizada.
            var ddl = Lista(cx, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{tabla}'").Single();
            foreach (var (nombre, expresion) in ExtraerChecks(ddl))
                hechos.Add($"ck:{nombre}|{Normalizar(expresion)}");

            resultado[tabla] = hechos;
        }
        return resultado;
    }

    /// <summary>Nombres de todos los triggers de la base.</summary>
    public static List<string> Triggers(SqliteConnection cx)
        => Lista(cx, "SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name");

    private static string Normalizar(string s) => Regex.Replace(s, "[\\s\"`\\[\\]]", "");

    /// <summary>Busca cada <c>CONSTRAINT "CK_x" CHECK (...)</c> respetando los paréntesis anidados.</summary>
    private static IEnumerable<(string Nombre, string Expresion)> ExtraerChecks(string ddl)
    {
        foreach (Match m in Regex.Matches(ddl, "CONSTRAINT\\s+\"(?<n>[^\"]+)\"\\s+CHECK\\s*\\("))
        {
            var inicio = m.Index + m.Length;
            var nivel = 1;
            var i = inicio;
            while (i < ddl.Length && nivel > 0)
            {
                if (ddl[i] == '(') nivel++;
                else if (ddl[i] == ')') nivel--;
                i++;
            }
            yield return (m.Groups["n"].Value, ddl[inicio..(i - 1)]);
        }
    }

    private static List<(string Nombre, bool Unico, string Origen, bool Parcial)> IndicesDe(SqliteConnection cx, string tabla)
    {
        var lista = new List<(string, bool, string, bool)>();
        using var cmd = cx.CreateCommand();
        cmd.CommandText = $"SELECT name, \"unique\", origin, partial FROM pragma_index_list('{tabla}')";
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((r.GetString(0), r.GetInt64(1) == 1, r.GetString(2), r.GetInt64(3) == 1));
        return lista;
    }

    private static List<string> Lista(SqliteConnection cx, string sql)
    {
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        return lista;
    }

    private static List<string[]> Filas(SqliteConnection cx, string sql)
    {
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var filas = new List<string[]>();
        while (r.Read())
        {
            var f = new string[r.FieldCount];
            for (var i = 0; i < f.Length; i++)
                f[i] = r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
            filas.Add(f);
        }
        return filas;
    }
}
