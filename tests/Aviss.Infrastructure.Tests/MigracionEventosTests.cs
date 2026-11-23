using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Actualizar una base con datos de la migración "EsperaYRechazo" (la versión anterior) a "EventosDelCatalogo" (spec 005,
/// RF-19, RF-20). El peligro: perder o reactivar algo, o dejar la base a medias si falla.
/// </summary>
public sealed class MigracionEventosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    private static List<string> Objetos(BaseDeDatosDePrueba db)
    {
        using var cmd = db.Cruda.CreateCommand();
        cmd.CommandText = "SELECT type || ':' || name FROM sqlite_master WHERE type IN ('trigger','index') AND name NOT LIKE 'sqlite_%' ORDER BY 1";
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }

    private async Task SembrarAsync()
    {
        using var ctx = _db.NuevoContexto();
        await new SembradorInicial(_db.Reloj).SembrarEstadosAsync(ctx);
    }

    [Fact]
    public async Task Actualizar_una_base_con_datos_no_pierde_ni_reactiva_nada_y_adopta_el_Aborto_a_mano()
    {
        _db.MigrarHasta("EsperaYRechazo");

        // Una base de la versión anterior: los 6 iniciales (VACIA desactivado a mano y SECA renombrada), un evento propio,
        // un "Aborto" creado a mano y registros que los usan.
        var vet = _db.Usuario("Dra. Pérez");
        var rodeo = _db.Rodeo();
        var vacia = _db.Estado("VACIA", "VACIA", "VACIA", activo: false);
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA");
        _db.Estado("INSEMIN", "INSEMIN", "INSEMIN");
        var seca = _db.Estado("SECA", "Seca larga", "SECA2");
        _db.Estado("FRESCA", "FRESCA", "FRESCA");
        _db.Estado("NOINSEM", "NOINSEM", "NOINSEM");
        var prostag = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG");
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO-DC");
        var animal = _db.Animal(rodeo, "100", estadoId: aborto);
        var tacto = _db.Tacto(animal, vet, aborto);
        var tactoSeca = _db.Tacto(animal, vet, seca);
        var rechazo = _db.Rechazo(animal, vet, "motivo");

        _db.MigrarHasta(null);

        // --- Después de la migración (antes de sembrar): clave, principal y orden de los 6; nada más cambió ---
        string Clave(Guid id) => _db.Escalar("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = $id", ("$id", id))?.ToString() ?? "(nula)";
        Assert.Equal("VACIA", Clave(vacia));
        Assert.Equal("PRENADA", Clave(prenada));
        Assert.Equal("(nula)", Clave(prostag));   // los creados a mano quedan sin clave...
        Assert.Equal("(nula)", Clave(aborto));    // ...y el Aborto a mano se adopta recién al sembrar
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", vacia)));   // sigue desactivado
        Assert.Equal(3, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", vacia)));
        Assert.Equal(4, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
        Assert.Equal(0, _db.Contar("SELECT EsPrincipal FROM EstadoReproductivo WHERE Id = $id", ("$id", prostag)));
        Assert.Equal("Seca larga", _db.Escalar("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", seca)));
        Assert.Equal(8, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));

        // --- Sembrar: agrega OK y Revisar Nuevamente y adopta el Aborto ---
        await SembrarAsync();
        await SembrarAsync(); // dos arranques seguidos no duplican

        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo")); // 9 estándar + PROSTAG
        Assert.Equal("ABORTO", Clave(aborto));
        Assert.Equal("Aborto", _db.Escalar("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal("ABORTO-DC", _db.Escalar("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal("SoloEvento", _db.Escalar("SELECT Tipo FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal(5, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", vacia)));    // no se reactivó
        Assert.Equal("(nula)", Clave(prostag));
        Assert.Equal(0, _db.Contar("SELECT EsPrincipal FROM EstadoReproductivo WHERE Id = $id", ("$id", prostag)));  // queda en "Otro evento"
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'OK' AND Activo = 1"));

        // --- Lo guardado sigue, apuntando a lo mismo ---
        Assert.Equal(aborto.ToString().ToUpperInvariant(), _db.Escalar("SELECT EstadoNuevoId FROM Tacto WHERE Id = $id", ("$id", tacto))?.ToString()?.ToUpperInvariant());
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $id", ("$id", tactoSeca)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Id = $id AND EventoId IS NULL", ("$id", rechazo)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Animal WHERE Id = $id", ("$id", animal)));

        // --- Los triggers y el CHECK de Historial de antes siguen, y el de Rechazo se recreó ---
        Assert.True(_db.ExisteTrigger("TR_Rechazo_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Rechazo_NoBorrar"));
        Assert.True(_db.ExisteTrigger("TR_Rechazo_SoloEvento"));
        Assert.True(_db.ExisteTrigger("TR_Tacto_EstadoNoSoloEvento"));
        Assert.True(_db.ExisteTrigger("TR_Tacto_NoModificar"));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
    }

    [Fact]
    public void Una_base_nueva_y_una_actualizada_tienen_los_mismos_triggers_e_indices()
    {
        _db.MigrarHasta("EsperaYRechazo");
        _db.MigrarHasta(null);
        using var nueva = new BaseDeDatosDePrueba();

        Assert.Equal(Objetos(nueva), Objetos(_db));
    }

    [Fact]
    public void Si_la_migracion_falla_a_mitad_la_base_queda_como_estaba()
    {
        _db.MigrarHasta("EsperaYRechazo");
        var estado = _db.Estado("SECA", "SECA", "SECA");
        // Saboteo: un trigger con el mismo nombre que uno de los últimos pasos hace fallar la migración DESPUÉS de haber
        // agregado las columnas. Como EF aplica cada migración en una transacción, todo tiene que volver atrás.
        _db.Ejecutar("CREATE TRIGGER TR_Tacto_EstadoNoSoloEvento BEFORE INSERT ON Tacto BEGIN SELECT 1; END;");

        Assert.ThrowsAny<Exception>(() => _db.MigrarHasta(null));

        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('EstadoReproductivo') WHERE name IN ('Tipo','EsPrincipal','Orden','ClaveEstandar')"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Rechazo') WHERE name = 'EventoId'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('IX_EstadoReproductivo_ClaveEstandar','TR_Rechazo_SoloEvento')"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%EventosDelCatalogo'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Id = $id", ("$id", estado)));
    }
}
