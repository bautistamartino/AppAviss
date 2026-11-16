using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests;

/// <summary>Configuración de cada conexión: claves foráneas, WAL, synchronous FULL y verificación (RF-16).</summary>
public sealed class ConexionTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();

    public void Dispose() => _db.Dispose();

    private static long Pragma(AvissDbContext ctx, string nombre)
    {
        var conexion = ctx.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open) conexion.Open();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"PRAGMA {nombre}";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Foreign_keys_esta_activo_en_cada_conexion_nueva()
    {
        for (var i = 0; i < 3; i++)
        {
            using var ctx = _db.NuevoContexto();
            Assert.Equal(1, Pragma(ctx, "foreign_keys"));
        }
    }

    [Fact]
    public void Las_claves_foraneas_se_aplican_a_traves_de_EF()
    {
        using var ctx = _db.NuevoContexto();
        // un animal con rodeo inexistente tiene que fallar también por la vía de EF
        var animal = Aviss.Domain.Entidades.Animal.CrearAltaEnCampo(
            Guid.NewGuid(), Aviss.Domain.Reglas.Caravana.Crear("1"), null, null, _db.Reloj.UtcAhora);
        ctx.Add(animal);
        Assert.ThrowsAny<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void La_conexion_usa_WAL_synchronous_FULL_y_espera_por_bloqueos()
    {
        using var ctx = _db.NuevoContexto();
        var conexion = ctx.Database.GetDbConnection();
        conexion.Open();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", (string?)cmd.ExecuteScalar());
        Assert.Equal(2, Pragma(ctx, "synchronous")); // 2 = FULL
        // la espera ante bloqueos la maneja Microsoft.Data.Sqlite con el "Default Timeout" de la cadena de conexión
        Assert.Equal(5, ((SqliteConnection)conexion).DefaultTimeout);
    }

    [Fact]
    public void VerificarForeignKeys_lanza_si_estan_apagadas()
    {
        using var conexion = new SqliteConnection($"Data Source={_db.RutaDb};Pooling=False");
        conexion.Open();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<ForeignKeysNoActivasException>(() => InterceptorConexion.VerificarForeignKeys(conexion));
    }

    [Fact]
    public void VerificarForeignKeys_no_lanza_si_estan_activas()
    {
        using var conexion = new SqliteConnection($"Data Source={_db.RutaDb};Pooling=False");
        conexion.Open();
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON";
            cmd.ExecuteNonQuery();
        }
        InterceptorConexion.VerificarForeignKeys(conexion);
    }
}
