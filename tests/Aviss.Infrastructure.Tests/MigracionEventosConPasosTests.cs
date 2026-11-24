using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 005, segunda vuelta (RF-19, RF-20, RF-26, RF-31): una base de la versión anterior (con la migración
/// "EventosDelCatalogo", eventos creados a mano, un inicial desactivado, Abortos ya guardados y tactos) se actualiza con la
/// migración REAL "EventosConPasos" a través del arranque: copia previa, sin perder ni reactivar nada, y si falla la base
/// queda como estaba.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class MigracionEventosConPasosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    private readonly FakeReloj _reloj = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public MigracionEventosConPasosTests() => _rutas = new RutasAviss(_db.Directorio);
    public void Dispose() => _db.Dispose();

    private ArranqueService Servicio(IMigrador? migrador = null)
        => new(_rutas, migrador ?? new MigradorEf(), new BackupService(_rutas, _reloj, _espacio), new SembradorInicial(_reloj),
            new ArranqueResultado(), new LimpiezaDeRastros(_rutas, new LimpiezaPreferenciasNula()));

    private string[] Backups() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db") : [];

    private sealed record Datos(
        Guid Vacia, Guid Prenada, Guid Noinsem, Guid Seca, Guid Aborto, Guid Ok, Guid Prostag, Guid FechaPreviaAMano,
        Guid Animal, Guid TactoPrenada, Guid TactoSeca, Guid AbortoGuardado, Guid AbortoAnulado, Guid RechazoComun);

    /// <summary>La base como la dejó la primera vuelta de la spec 005 (migración "EventosDelCatalogo"), con datos de todo.</summary>
    private Datos BaseAnterior()
    {
        _db.MigrarHasta("EventosDelCatalogo");

        var vacia = _db.Estado("VACIA", "VACIA", "VACIA", activo: false, tipo: "Estado", clave: "VACIA", principal: true, orden: 3);
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);
        var noinsem = _db.Estado("NOINSEM", "NOINSEM", "NOINSEM", tipo: "Estado", clave: "NOINSEM", principal: true, orden: 6);
        _db.Estado("INSEMIN", "INSEMIN", "INSEMIN", tipo: "Estado", clave: "INSEMIN");
        var seca = _db.Estado("SECA", "Seca larga", "SECA2", tipo: "Estado", clave: "SECA");
        _db.Estado("FRESCA", "FRESCA", "FRESCA", tipo: "Estado", clave: "FRESCA");
        var ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK", principal: true, orden: 2);
        _db.Estado("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", tipo: "SoloEvento", clave: "REVISAR_NUEVAMENTE", principal: true, orden: 1);
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "SoloEvento", clave: "ABORTO", principal: true, orden: 5);
        var prostag = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG");
        // Una "Preñada fecha previa" creada a mano por el veterinario, y desactivada.
        var fechaPreviaAMano = _db.Estado("PFP", "Preñada fecha previa", "PFP-DC", activo: false);

        var animal = _db.Animal(_db.Rodeo(), "100", estadoId: seca);
        var tactoPrenada = _db.Tacto(animal, prenada, diasPrenez: 70);
        var tactoSeca = _db.Tacto(animal, seca);
        // Abortos ya guardados con la primera vuelta: en Rechazo, con el evento y sin días.
        var abortoGuardado = _db.Rechazo(animal, eventoId: aborto);
        var abortoAnulado = _db.Rechazo(animal, eventoId: aborto, anulado: true);
        var rechazoComun = _db.Rechazo(animal, "motivo");

        _db.CerrarCruda();   // el arranque necesita el archivo sin conexiones abiertas
        return new Datos(vacia, prenada, noinsem, seca, aborto, ok, prostag, fechaPreviaAMano, animal, tactoPrenada,
            tactoSeca, abortoGuardado, abortoAnulado, rechazoComun);
    }

    private string Texto(string sql, params (string, object?)[] p) => _db.Escalar(sql, p)?.ToString() ?? "(nulo)";

    [Fact]
    public async Task Actualizar_hace_la_copia_previa_y_no_pierde_ni_reactiva_nada()
    {
        var d = BaseAnterior();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.True(r.PuedeAbrir);

        // La copia previa existe, está sana y es la base VIEJA (sin las columnas nuevas).
        var copia = Assert.Single(Backups());
        Assert.EndsWith("-previa-actualizacion.db", copia);
        using (var cx = new SqliteConnection($"Data Source={copia};Pooling=False;Mode=ReadOnly"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'EventoId'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // Las columnas nuevas existen y el esquema coincide con el del modelo (los CHECK y las FK están).
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('EstadoReproductivo') WHERE name IN ('Pasos')"));
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name IN ('EventoId','DiasPreniezInformados','DiasAborto')"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Rechazo') WHERE name = 'DiasAborto'"));

        // Nada se perdió ni cambió de identidad: mismas filas, mismos ids.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Animal WHERE Id = $a", ("$a", d.Animal)));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE AnimalId = $a", ("$a", d.Animal)));
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE AnimalId = $a", ("$a", d.Animal)));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE EventoId IS NOT NULL OR DiasAborto IS NOT NULL OR DiasPreniezInformados IS NOT NULL"));

        // Nada se reactivó ni se renombró a mano lo que el veterinario cambió.
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Vacia)));
        Assert.Equal("Seca larga", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Seca)));
        Assert.Equal("SECA2", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Seca)));

        // PREÑADA conserva su identidad y sus tactos, y ahora se llama "Preñada fecha correcta".
        Assert.Equal("Preñada fecha correcta", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Prenada)));
        Assert.Equal("PREÑADA", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Prenada)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $t AND EstadoNuevoId = $e", ("$t", d.TactoPrenada), ("$e", d.Prenada)));

        // Aborto queda como estado con sus pasos, en el orden nuevo; NOINSEM corrido un lugar.
        Assert.Equal("Estado", Texto("SELECT Tipo FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Aborto)));
        Assert.Equal("DiasAborto,ResultadoAborto", Texto("SELECT Pasos FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Aborto)));
        Assert.Equal(6, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Aborto)));
        Assert.Equal(7, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Noinsem)));

        // La "Preñada fecha previa" a mano se adoptó sin cambiar nombre, código ni activo (no se creó otra).
        Assert.Equal("PRENADA_FECHA_PREVIA", Texto("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = $id", ("$id", d.FechaPreviaAMano)));
        Assert.Equal("PFP-DC", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", d.FechaPreviaAMano)));
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", d.FechaPreviaAMano)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA_FECHA_PREVIA'"));
        // Los creados a mano siguen sin clave, no principales, en "Otro evento".
        Assert.Equal(0, _db.Contar("SELECT EsPrincipal FROM EstadoReproductivo WHERE Id = $id", ("$id", d.Prostag)));

        // Los Abortos viejos siguen en Rechazo, sin días (se ven como "Aborto" a secas) y el estado de la vaca no cambió (RF-31).
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Id = $r AND EventoId = $e AND DiasAborto IS NULL", ("$r", d.AbortoGuardado), ("$e", d.Aborto)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Id = $r AND Anulado = 1 AND DiasAborto IS NULL", ("$r", d.AbortoAnulado)));
        Assert.Equal(d.Seca.ToString().ToUpperInvariant(), Texto("SELECT EstadoReproductivoId FROM Animal WHERE Id = $a", ("$a", d.Animal)).ToUpperInvariant());

        // Los registros viejos siguen siendo inmutables con los triggers nuevos.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET DiasAborto = 5 WHERE Id = $r", ("$r", d.AbortoGuardado)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET DiasAborto = 5 WHERE Id = $t", ("$t", d.TactoSeca)));
        Assert.True(_db.ExisteTrigger("TR_Tacto_EventoValido"));
    }

    [Fact]
    public async Task Dos_arranques_seguidos_no_duplican_ni_cambian_nada()
    {
        BaseAnterior();
        await Servicio().IniciarAsync();
        var estados = Texto("SELECT group_concat(Id || Nombre || ActualizadoEnUtc, ';') FROM (SELECT * FROM EstadoReproductivo ORDER BY Id)");
        _db.CerrarCruda();
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.Single(Backups());   // no se hizo otra copia
        Assert.Equal(11, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));   // 10 estándar (uno adoptado) + PROSTAG
        Assert.Equal(estados, Texto("SELECT group_concat(Id || Nombre || ActualizadoEnUtc, ';') FROM (SELECT * FROM EstadoReproductivo ORDER BY Id)"));
    }

    [Fact]
    public async Task Si_la_migracion_falla_la_base_queda_como_estaba()
    {
        BaseAnterior();
        // Saboteo: un trigger con el nombre del último que crea la migración la hace fallar DESPUÉS de haber agregado las
        // columnas y recreado triggers. EF aplica la migración en una transacción: todo tiene que volver atrás.
        _db.Ejecutar("CREATE TRIGGER TR_Tacto_EventoValido BEFORE INSERT ON Tacto BEGIN SELECT 1; END;");
        var huellaEsquema = Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
        _db.CerrarCruda();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(huellaEsquema, Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'EventoId'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%EventosConPasos'"));
        Assert.Equal("SoloEvento", Texto("SELECT Tipo FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Tacto"));
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }
}
