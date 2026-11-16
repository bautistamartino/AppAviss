using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Persistencia;

/// <summary>Un único lugar donde se arma la configuración de EF, para que la app, los tests y la herramienta de migraciones usen lo mismo.</summary>
public static class ConfiguracionDb
{
    public static void Configurar(DbContextOptionsBuilder opciones, string rutaBase)
    {
        // DefaultTimeout = segundos que espera un comando si la base está bloqueada por otra conexión, antes de fallar.
        var cs = new SqliteConnectionStringBuilder { DataSource = rutaBase, DefaultTimeout = 5 }.ToString();
        opciones.UseSqlite(cs).AddInterceptors(InterceptorConexion.Instancia);
    }
}
