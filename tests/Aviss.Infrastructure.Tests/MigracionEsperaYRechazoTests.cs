using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Actualizar una base que ya tiene datos de la migración "IndiceTactoFecha" a "EsperaYRechazo" (spec 004). El peligro es el
/// mismo que en "Mangas": la tabla Historial se reconstruye para ampliar su CHECK y no debe perder datos ni triggers.
/// </summary>
public sealed class MigracionEsperaYRechazoTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    private void MigrarHasta(string? destino)
    {
        using var ctx = _db.NuevoContexto();
        var migrador = ctx.GetService<IMigrator>();
        if (destino is null) migrador.Migrate(); else migrador.Migrate(destino);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Actualizar_desde_IndiceTactoFecha_con_datos_conserva_todo_y_los_triggers()
    {
        MigrarHasta("IndiceTactoFecha");
        var old = new EsquemaAnterior(_db);
        var vet = old.Usuario("Dra. Pérez");
        var rodeo = _db.Rodeo();
        var manga = old.Manga(rodeo, 1, vet);
        var estado = _db.Estado("SECA", "SECA", "SECA");
        var animal = _db.Animal(rodeo, "100", estadoId: estado);
        var tacto = old.Tacto(animal, vet, estado, diasPrenez: 40);
        var marca = old.MarcaPasada(animal, manga, vet);
        var historial = old.Historial(vet, "MarcaPasada");
        Assert.Throws<SqliteException>(() => old.Historial(vet, "EsperaCreada")); // el esquema viejo no la conocía

        MigrarHasta("EsperaYRechazo");

        // Los datos siguen.
        Assert.Equal(40L, _db.Escalar("SELECT DiasPrenez FROM Tacto WHERE Id = $id", ("$id", tacto)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM MarcaPasada WHERE Id = $id", ("$id", marca)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Historial WHERE Id = $id AND Accion = 'MarcaPasada'", ("$id", historial)));

        // Los triggers de Historial se recrearon y siguen funcionando.
        Assert.True(_db.ExisteTrigger("TR_Historial_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Historial_NoBorrar"));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'x' WHERE Id = $id", ("$id", historial)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", historial)));

        // Las acciones nuevas entran y las tablas nuevas funcionan.
        foreach (var a in new[] { "EsperaCreada", "EsperaResuelta", "EsperaSalteada", "RechazoCreado", "RechazoAnulado" })
            old.Historial(vet, a);
        old.Espera(animal, vet);
        old.Rechazo(animal, vet, "motivo");
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }

    [Fact]
    public void Una_base_nueva_y_una_actualizada_tienen_los_mismos_triggers_e_indices()
    {
        MigrarHasta("IndiceTactoFecha");
        MigrarHasta(null);
        using var nueva = new BaseDeDatosDePrueba();

        const string consulta = "SELECT type || ':' || name FROM sqlite_master WHERE type IN ('trigger','index') AND name NOT LIKE 'sqlite_%' ORDER BY 1";
        Assert.Equal(Objetos(nueva, consulta), Objetos(_db, consulta));
    }

    private static List<string> Objetos(BaseDeDatosDePrueba db, string sql)
    {
        using var cmd = db.Cruda.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }
}
