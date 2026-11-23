using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Actualizar una base que ya tiene datos de la migración "Inicial" a "Mangas". Cierra el riesgo de la spec 001
/// "aplicar una segunda migración sobre una base con datos". El peligro concreto: SQLite reconstruye la tabla
/// Historial al cambiar su CHECK y, al hacerlo, pierde sus triggers (el historial quedaría modificable en silencio).
/// </summary>
public sealed class MigracionMangasTests : IDisposable
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

    // Todos los triggers que "Inicial" crea; deben seguir existiendo tras actualizar.
    private static readonly string[] TriggersDeInicial =
    {
        "TR_Historial_NoModificar", "TR_Historial_NoBorrar",
        "TR_Tacto_NoModificar", "TR_Tacto_NoBorrar",
        "TR_Animal_NoBorrar", "TR_Animal_Inmutables",
        "TR_Establecimiento_NoBorrar", "TR_Rodeo_NoBorrar", "TR_Usuario_NoBorrar", "TR_EstadoReproductivo_NoBorrar",
        "TR_Usuario_SinAsignarProtegido"
    };

    [Fact]
    public void Actualizar_desde_Inicial_con_datos_conserva_todo_y_los_triggers()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);

        // Datos creados con el esquema viejo (sin la regla de nombre único: dos veterinarios con el mismo nombre).
        old.Usuario("Sin asignar", id: EsquemaAnterior.SinAsignarId);
        var vet1 = old.Usuario("Dra. Pérez");
        old.Usuario("DRA. PÉREZ ");
        var rodeo = _db.Rodeo();
        var estado = _db.Estado("SECA", "SECA", "SECA");
        var animal = _db.Animal(rodeo, "100", estadoId: estado);
        var tacto = old.Tacto(animal, vet1, estado, diasPrenez: 40);
        var historial = old.Historial(vet1, "Tacto");
        foreach (var t in TriggersDeInicial) Assert.True(_db.ExisteTrigger(t), "antes de migrar: " + t);

        MigrarHasta("Mangas");

        // Los datos siguen.
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Usuario"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Animal WHERE Id = $id", ("$id", animal)));
        Assert.Equal(40L, _db.Escalar("SELECT DiasPrenez FROM Tacto WHERE Id = $id", ("$id", tacto)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Historial WHERE Id = $id AND Accion = 'Tacto'", ("$id", historial)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rodeo"));

        // Los triggers siguen, en particular los de Historial (la tabla que se reconstruye), Tacto, Animal y Usuario.
        foreach (var t in TriggersDeInicial) Assert.True(_db.ExisteTrigger(t), "después de migrar: " + t);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'x' WHERE Id = $id", ("$id", historial)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", historial)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Tacto WHERE Id = $id", ("$id", tacto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Animal WHERE Id = $id", ("$id", animal)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Usuario WHERE Id = $id", ("$id", vet1)));
    }

    [Fact]
    public void El_nombre_duplicado_queda_diferenciado_y_el_indice_nuevo_rige()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);
        var vet1 = old.Usuario("Dra. Pérez");
        var vet2 = old.Usuario("DRA. PÉREZ ");
        var otro = old.Usuario("Dr. López");

        MigrarHasta("Mangas");

        // El primero conserva su nombre, el repetido se diferencia con su rowid y ningún otro cambia.
        Assert.Equal("Dra. Pérez", _db.Escalar("SELECT Nombre FROM Usuario WHERE Id = $id", ("$id", vet1)));
        var renombrado = (string)_db.Escalar("SELECT Nombre FROM Usuario WHERE Id = $id", ("$id", vet2))!;
        Assert.StartsWith("DRA. PÉREZ ", renombrado);
        Assert.Matches(@"\(\d+\)$", renombrado);
        Assert.Equal("Dr. López", _db.Escalar("SELECT Nombre FROM Usuario WHERE Id = $id", ("$id", otro)));

        // Ya no hay dos con la misma clave y el índice impide crear otro repetido.
        Assert.Equal(3, _db.Contar("SELECT COUNT(DISTINCT upper(trim(Nombre))) FROM Usuario"));
        Assert.Throws<SqliteException>(() => old.Usuario("dr. lópez"));
    }

    [Fact]
    public void Si_Sin_asignar_esta_repetido_se_conserva_Sin_asignar_y_se_renombra_el_otro()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);
        var intruso = old.Usuario("sin asignar");                       // antes que el verdadero: rowid menor
        old.Usuario("Sin asignar", id: EsquemaAnterior.SinAsignarId);

        MigrarHasta("Mangas");

        Assert.Equal("Sin asignar", _db.Escalar("SELECT Nombre FROM Usuario WHERE Id = $id", ("$id", EsquemaAnterior.SinAsignarId)));
        Assert.Matches(@"^sin asignar \(\d+\)$", (string)_db.Escalar("SELECT Nombre FROM Usuario WHERE Id = $id", ("$id", intruso))!);
    }

    [Fact]
    public void Tres_repetidos_quedan_los_tres_distintos()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);
        old.Usuario("Vet"); old.Usuario("vet"); old.Usuario(" VET");

        MigrarHasta("Mangas");

        Assert.Equal(3, _db.Contar("SELECT COUNT(DISTINCT Nombre) FROM Usuario"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Usuario WHERE Nombre = 'Vet'"));
    }

    [Fact]
    public void Despues_de_actualizar_el_historial_acepta_las_acciones_nuevas_y_rechaza_otras()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);
        var vet = old.Usuario("Vet");
        Assert.Throws<SqliteException>(() => old.Historial(vet, "MarcaPasada"));   // el esquema viejo no la conocía

        MigrarHasta("Mangas");

        foreach (var a in new[] { "MarcaPasada", "AnulacionMarca", "MangaAbierta", "MangaCerrada", "AnulacionCierre" })
            old.Historial(vet, a);
        Assert.Throws<SqliteException>(() => old.Historial(vet, "Otra"));
        Assert.Equal(5, _db.Contar("SELECT COUNT(*) FROM Historial"));
    }

    [Fact]
    public void Una_base_nueva_y_una_actualizada_tienen_los_mismos_triggers_e_indices()
    {
        MigrarHasta("Inicial");
        var old = new EsquemaAnterior(_db);
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

    [Fact]
    public void Aplicar_dos_veces_no_falla()
    {
        MigrarHasta(null);
        MigrarHasta(null);
        // Una fila por migración del proyecto (Inicial, Mangas, IndiceTactoFecha...): aplicar de nuevo no agrega ninguna.
        using var ctx = _db.NuevoContexto();
        Assert.Equal(ctx.Database.GetMigrations().Count(), _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory"));
    }
}
