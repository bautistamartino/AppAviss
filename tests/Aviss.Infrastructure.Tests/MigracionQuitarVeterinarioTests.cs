using System.Security.Cryptography;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006: la migración "QuitarVeterinario" sobre una base creada por la versión anterior (con Usuario, datos de cada tabla,
/// triggers e índices). Es la prueba de humo de T2; la batería completa (triggers, CHECK, índices, fallas) es de T3.
/// </summary>
public sealed class MigracionQuitarVeterinarioTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    private EsquemaAnterior.Poblado BaseVieja()
    {
        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        return new EsquemaAnterior(_db).Poblar();
    }

    private void Migrar() => EsquemaAnterior.MigrarHasta(_db, null);

    [Fact]
    public void Conserva_todos_los_datos()
    {
        var p = BaseVieja();
        var antes = EsquemaAnterior.Instantanea(_db);
        Assert.NotEmpty(antes["Tacto"]);
        Assert.NotEmpty(antes["Espera"]);

        Migrar();
        var despues = EsquemaAnterior.Instantanea(_db);

        foreach (var (tabla, filas) in antes)
            Assert.True(filas.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió al migrar.");

        // El Excel original: byte por byte y con su hash (el guardado y el recalculado coinciden con los de antes).
        var blob = (byte[])_db.Escalar("SELECT ArchivoOrigenBlob FROM Rodeo WHERE Id = $id", ("$id", p.Rodeo1))!;
        Assert.Equal(p.BlobRodeo1, blob);
        Assert.Equal(p.HashRodeo1, _db.Escalar("SELECT ArchivoOrigenHash FROM Rodeo WHERE Id = $id", ("$id", p.Rodeo1)));
        Assert.Equal(p.HashRodeo1, Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant());

        // Se conserva el vínculo entre la espera resuelta y el registro que la resolvió, y la anulación del tacto.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera WHERE Id = $id AND Estado = 'Resuelta' AND RegistroResolucionId IS NOT NULL", ("$id", p.EsperaResuelta)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $id AND TactoAnuladoId = $t", ("$id", p.Anulacion), ("$t", p.TactoAnulado)));
    }

    [Fact]
    public void Descarta_solo_quien_y_tablet()
    {
        BaseVieja();
        Migrar();

        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        foreach (var tabla in new[] { "Tacto", "MarcaPasada", "Manga", "CierreManga", "Rechazo", "Espera", "Historial" })
        foreach (var columna in new[] { "UsuarioId", "AbiertaPorUsuarioId", "AnuladoPorUsuarioId", "ResueltaPorUsuarioId", "DispositivoId" })
            Assert.Equal(0, _db.Contar($"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = $c", ("$c", columna)));

        // Los nombres de los veterinarios no quedan en ningún valor de ninguna tabla.
        var volcado = EsquemaAnterior.VolcadoDeTexto(_db);
        foreach (var nombre in EsquemaAnterior.NombresDeVeterinarios) Assert.DoesNotContain(nombre, volcado);
        Assert.DoesNotContain("Sin asignar", volcado);
        Assert.DoesNotContain("MP-123", volcado);
    }

    [Fact]
    public void Claves_foraneas_activas()
    {
        BaseVieja();
        Migrar();

        // Ninguna referencia rota.
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));

        // La conexión que usa la app las exige.
        using (var ctx = _db.NuevoContexto())
        {
            ctx.Database.OpenConnection();
            using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        Assert.Throws<SqliteException>(() => _db.Tacto(Guid.NewGuid(), Guid.NewGuid()));   // animal y estado inexistentes

        // La FK compuesta de Tacto (AnimalId, TactoAnuladoId) -> Tacto (AnimalId, Id) sigue existiendo. Ojo: una FK compuesta
        // aparece como DOS filas en pragma_foreign_key_list (una por columna).
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('Tacto') WHERE \"table\" = 'Tacto'"));
        // Las hijas de Manga apuntan a "Manga" (no a una tabla temporal). La clave alternativa de Tacto se comprueba con la FK compuesta (la exige).
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('CierreManga') WHERE \"table\" = 'Manga'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('MarcaPasada') WHERE \"table\" = 'Manga'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE sql LIKE '%_nueva%'"));    }

    [Fact]
    public void Quedan_los_triggers_de_inmutabilidad()
    {
        BaseVieja();
        Migrar();
        foreach (var t in new[]
        {
            "TR_Historial_NoModificar", "TR_Historial_NoBorrar", "TR_Tacto_NoModificar", "TR_Tacto_NoBorrar",
            "TR_MarcaPasada_NoModificar", "TR_MarcaPasada_NoBorrar", "TR_MarcaPasada_MismoRodeo", "TR_Manga_NoModificar", "TR_Manga_NoBorrar",
            "TR_CierreManga_NoModificar", "TR_CierreManga_NoBorrar", "TR_Rechazo_NoModificar", "TR_Rechazo_NoBorrar",
            "TR_Espera_NoModificar", "TR_Espera_NoBorrar"
        })
            Assert.True(_db.ExisteTrigger(t), t);
        Assert.False(_db.ExisteTrigger("TR_Usuario_NoBorrar"));
        Assert.False(_db.ExisteTrigger("TR_Usuario_SinAsignarProtegido"));
    }
}
