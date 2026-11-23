using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// RF-12: si la migración "QuitarVeterinario" se corta en cualquier comando, SQLite deshace TODO (la migración corre en una
/// sola transacción) y la base queda exactamente como estaba; el segundo intento, sin falla, termina bien.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class MigracionQuitarVeterinarioFallasTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    private AvissDbContext Contexto(InterceptorDeComandos? interceptor)
    {
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, _db.RutaDb);
        if (interceptor is not null) opciones.AddInterceptors(interceptor);
        return new AvissDbContext(opciones.Options);
    }

    private void Migrar(InterceptorDeComandos? interceptor)
    {
        try
        {
            using var ctx = Contexto(interceptor);
            ctx.GetService<IMigrator>().Migrate();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>Cuántos comandos de escritura ejecuta una migración completa (se mide en otra base, sin fallar).</summary>
    private static (int Total, int Primero) ContarComandos()
    {
        using var otra = new BaseDeDatosDePrueba(migrar: false);
        EsquemaAnterior.MigrarHasta(otra, "EsperaYRechazo");
        new EsquemaAnterior(otra).Poblar();
        var interceptor = new InterceptorDeComandos();
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, otra.RutaDb);
        opciones.AddInterceptors(interceptor);
        using (var ctx = new AvissDbContext(opciones.Options)) ctx.GetService<IMigrator>().Migrate();
        SqliteConnection.ClearAllPools();
        // El comando 1 de EF es crear la tabla de historial (infraestructura de EF, no de la migración): "el primero" de la migración es el PRAGMA.
        return (interceptor.Textos.Count, interceptor.Textos.FindIndex(t => t.Contains("defer_foreign_keys")) + 1);
    }

    [Theory]
    [InlineData("el primero")]
    [InlineData("uno al principio")]
    [InlineData("la mitad")]
    [InlineData("el anteultimo")]
    [InlineData("el ultimo")]
    public void Si_falla_un_comando_la_base_queda_igual_y_el_reintento_anda(string donde)
    {
        var (total, primero) = ContarComandos();
        var n = donde switch
        {
            "el primero" => primero,
            "uno al principio" => primero + 4,
            "la mitad" => total / 2,
            "el anteultimo" => total - 1,
            _ => total   // el último: la anotación en el historial de migraciones
        };

        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        new EsquemaAnterior(_db).Poblar();
        var fotoAntes = EsquemaAnterior.Instantanea(_db);
        var usuariosAntes = _db.Contar("SELECT COUNT(*) FROM Usuario");
        _db.CerrarCruda();
        var huellaAntes = InspeccionSqlite.HuellaEsquema(_db.RutaDb);
        var migracionesAntes = InspeccionSqlite.MigracionesAplicadas(_db.RutaDb);

        // Falla en el comando N.
        var falla = new InterceptorDeComandos { FallarEnComando = n };
        Assert.ThrowsAny<Exception>(() => Migrar(falla));
        Assert.Equal(n, falla.Textos.Count); // llegó justo hasta el comando que se hizo fallar

        // La base quedó exactamente igual: esquema (tablas, índices, triggers), migraciones aplicadas y todas las filas.
        _db.CerrarCruda();
        Assert.Equal(huellaAntes, InspeccionSqlite.HuellaEsquema(_db.RutaDb));
        Assert.Equal(migracionesAntes, InspeccionSqlite.MigracionesAplicadas(_db.RutaDb));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        Assert.Equal(usuariosAntes, _db.Contar("SELECT COUNT(*) FROM Usuario"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_QuitarVeterinario'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%_nueva'"));
        var fotoDespuesDeFallar = EsquemaAnterior.Instantanea(_db);
        foreach (var (tabla, filas) in fotoAntes)
            Assert.True(filas.SequenceEqual(fotoDespuesDeFallar[tabla]), $"La tabla {tabla} cambió con la migración fallida.");
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
        _db.CerrarCruda();

        // El segundo intento, sin falla, termina bien y conserva todo.
        Migrar(null);
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_QuitarVeterinario'"));
        var fotoFinal = EsquemaAnterior.Instantanea(_db);
        foreach (var (tabla, filas) in fotoAntes)
            Assert.True(filas.SequenceEqual(fotoFinal[tabla]), $"La tabla {tabla} cambió al reintentar.");
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }
}
