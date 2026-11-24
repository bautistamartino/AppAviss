using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 007 (RF-21, constitución §7): una base de la versión anterior (con la migración "EventosConPasos", con datos de todo) se
/// actualiza con la migración REAL "Exportaciones" a través del arranque: copia previa, ninguna fila se pierde ni cambia, la tabla
/// nueva queda protegida, y si la migración falla la base queda como estaba. También comprueba que el modelo no tiene cambios
/// pendientes (lo mismo que <c>dotnet ef migrations has-pending-model-changes</c>).
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class MigracionExportacionesTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    private readonly FakeReloj _reloj = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public MigracionExportacionesTests() => _rutas = new RutasAviss(_db.Directorio);
    public void Dispose() => _db.Dispose();

    private ArranqueService Servicio()
        => new(_rutas, new MigradorEf(), new BackupService(_rutas, _reloj, _espacio), new SembradorInicial(_reloj),
            new ArranqueResultado(), new LimpiezaDeRastros(_rutas, new LimpiezaPreferenciasNula()));

    private string[] Backups() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db") : [];

    private sealed record Datos(Guid Rodeo, Guid Animal, Guid Tacto, Guid Rechazo, Guid Estado);

    /// <summary>La base como la dejó la spec 005 (migración "EventosConPasos"), con rodeo, vaca, un tacto, un rechazo y un evento de cada tipo.</summary>
    private Datos BaseAnterior()
    {
        _db.MigrarHasta("EventosConPasos");

        var seca = _db.Estado("SECA", "SECA", "SECA", tipo: "Estado", clave: "SECA");
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
        var rodeo = _db.Rodeo();
        var animal = _db.Animal(rodeo, "100", "Importado", estadoId: seca);
        var tacto = _db.Tacto(animal, seca);
        var rechazo = _db.Rechazo(animal, eventoId: aborto, diasAborto: 30);

        _db.CerrarCruda();   // el arranque necesita el archivo sin conexiones abiertas
        return new Datos(rodeo, animal, tacto, rechazo, seca);
    }

    [Fact]
    public async Task Actualizar_hace_la_copia_previa_crea_la_tabla_y_no_cambia_ninguna_fila()
    {
        var d = BaseAnterior();
        var foto = FotoDeLoExistente();
        _db.CerrarCruda();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.True(r.PuedeAbrir);

        // La copia previa existe, está sana y es la base VIEJA (sin la tabla nueva).
        var copia = Assert.Single(Backups());
        Assert.EndsWith("-previa-actualizacion.db", copia);
        using (var cx = new SqliteConnection($"Data Source={copia};Pooling=False;Mode=ReadOnly"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Exportacion'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // La tabla nueva existe, vacía, con su índice y sus triggers.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Exportacion'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Exportacion"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Exportacion_RodeoId_FechaUtc'"));
        Assert.True(_db.ExisteTrigger("TR_Exportacion_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Exportacion_NoBorrar"));

        // Nada de lo existente cambió: mismas filas, mismos valores, mismos triggers de siempre.
        Assert.Equal(foto, FotoDeLoExistente());
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rodeo WHERE Id = $r", ("$r", d.Rodeo)));
        Assert.True(_db.ExisteTrigger("TR_Tacto_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Historial_NoBorrar"));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET DiasAborto = 5 WHERE Id = $r", ("$r", d.Rechazo)));

        // Y la tabla nueva ya funciona y está protegida.
        var e = _db.Exportacion(d.Rodeo);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Exportacion WHERE Id = $id", ("$id", e)));
    }

    /// <summary>Una huella de todo lo que había antes: las tablas existentes, fila por fila (sin la tabla nueva).</summary>
    private string FotoDeLoExistente()
        => string.Join("|",
            _db.Escalar("SELECT group_concat(Id || ':' || Nombre || ':' || ActualizadoEnUtc, ';') FROM (SELECT * FROM Rodeo ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || NumeroCaravana || ':' || COALESCE(EstadoReproductivoId,''), ';') FROM (SELECT * FROM Animal ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || EstadoNuevoId || ':' || Anulado, ';') FROM (SELECT * FROM Tacto ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || COALESCE(EventoId,'') || ':' || COALESCE(DiasAborto,''), ';') FROM (SELECT * FROM Rechazo ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || Nombre || ':' || Activo, ';') FROM (SELECT * FROM EstadoReproductivo WHERE Codigo IN ('SECA','ABORTO') ORDER BY Id)"),
            _db.Escalar("SELECT COUNT(*) FROM Historial"));

    [Fact]
    public async Task Dos_arranques_seguidos_no_cambian_nada()
    {
        BaseAnterior();
        await Servicio().IniciarAsync();
        var foto = FotoDeLoExistente();
        _db.CerrarCruda();
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.Single(Backups());   // no se hizo otra copia
        Assert.Equal(foto, FotoDeLoExistente());
    }

    [Fact]
    public async Task Si_la_migracion_falla_la_base_queda_como_estaba_sin_la_tabla()
    {
        BaseAnterior();
        // Saboteo: un trigger con el nombre del último que crea la migración la hace fallar DESPUÉS de crear la tabla y su
        // índice. EF aplica la migración en una transacción: todo tiene que volver atrás.
        _db.Ejecutar("CREATE TRIGGER TR_Exportacion_NoBorrar BEFORE DELETE ON Tacto BEGIN SELECT 1; END;");
        var huellaEsquema = InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
        var foto = FotoDeLoExistente();
        _db.CerrarCruda();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(huellaEsquema, InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Exportacion'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_Exportaciones'"));
        Assert.Equal(foto, FotoDeLoExistente());
    }

    [Fact]
    public void El_modelo_no_tiene_cambios_pendientes_respecto_de_las_migraciones()
    {
        using var ctx = _db.NuevoContexto();
        // Lo mismo que "dotnet ef migrations has-pending-model-changes": el snapshot de la última migración coincide con el modelo.
        Assert.False(ctx.Database.HasPendingModelChanges());
    }
}
