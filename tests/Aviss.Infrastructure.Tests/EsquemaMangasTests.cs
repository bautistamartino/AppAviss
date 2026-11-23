using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Reglas de las mangas, los cierres y las marcas de pasada que la base garantiza por sí sola (spec 003, RF-4, 5, 8, 20, 38).
/// Todo con SQL crudo, salteando la app.
/// </summary>
public sealed class EsquemaMangasTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    private (Guid rodeo, Guid manga) Base()
    {
        var rodeo = _db.Rodeo();
        return (rodeo, _db.Manga(rodeo, 1));
    }

    // ---------- Manga ----------

    [Fact]
    public void Manga_no_se_puede_modificar_ni_borrar()
    {
        var (_, manga) = Base();
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Manga SET Numero = 2 WHERE Id = $id", ("$id", manga)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Manga SET CreadoEnUtc = CreadoEnUtc WHERE Id = $id", ("$id", manga)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Manga WHERE Id = $id", ("$id", manga)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Manga"));
        Assert.Equal(1L, _db.Escalar("SELECT Numero FROM Manga WHERE Id = $id", ("$id", manga)));
    }

    [Fact]
    public void El_numero_de_manga_es_unico_por_rodeo_pero_se_repite_entre_rodeos()
    {
        var (rodeo, _) = Base();
        Assert.Throws<SqliteException>(() => _db.Manga(rodeo, 1));
        _db.Manga(rodeo, 2);
        _db.Manga(_db.Rodeo(), 1);
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Manga"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_numero_de_manga_debe_ser_1_o_mas(int numero)
        => Assert.Throws<SqliteException>(() => _db.Manga(_db.Rodeo(), numero));

    [Fact]
    public void Manga_con_rodeo_se_rechaza()
    {
        var rodeo = _db.Rodeo();
        Assert.Throws<SqliteException>(() => _db.Manga(Guid.NewGuid(), 1));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Manga"));
    }

    // ---------- CierreManga ----------

    [Fact]
    public void Cierre_solo_acepta_pasar_Anulado_de_0_a_1()
    {
        var (_, manga) = Base();
        var cierre = _db.CierreManga(manga, aviss: 10, confirmada: 12);

        // Cualquier otro cambio, también combinado con la anulación, se rechaza.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET CantidadAviss = 11 WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET CantidadConfirmada = 13, Diferencia = 3 WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET Tipo = 'Automatico' WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET Anulado = 1, CantidadAviss = 11 WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM CierreManga WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM CierreManga"));

        // Anular sí (y se puede tocar ActualizadoEnUtc).
        Assert.Equal(1, _db.Ejecutar("UPDATE CierreManga SET Anulado = 1, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", cierre), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddMinutes(1)))));
        Assert.Equal(1L, _db.Escalar("SELECT Anulado FROM CierreManga WHERE Id = $id", ("$id", cierre)));
    }

    [Fact]
    public void Un_cierre_anulado_no_vuelve_a_vigente_ni_cambia()
    {
        var (_, manga) = Base();
        var cierre = _db.CierreManga(manga, anulado: true);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET Anulado = 0 WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET Anulado = 1 WHERE Id = $id", ("$id", cierre)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE CierreManga SET CantidadAviss = 5 WHERE Id = $id", ("$id", cierre)));
    }

    [Fact]
    public void Una_manga_no_puede_tener_dos_cierres_vigentes_pero_si_uno_anulado_y_otro_vigente()
    {
        var (_, manga) = Base();
        _db.CierreManga(manga);
        Assert.Throws<SqliteException>(() => _db.CierreManga(manga));

        // Con el primero anulado entra uno nuevo (la manga se reabrió y se volvió a cerrar).
        _db.Ejecutar("UPDATE CierreManga SET Anulado = 1 WHERE MangaId = $m", ("$m", manga));
        _db.CierreManga(manga);
        _db.CierreManga(manga, anulado: true); // y varios anulados conviven
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM CierreManga"));
    }

    [Fact]
    public void Cierre_con_datos_invalidos_se_rechaza()
    {
        var (_, manga) = Base();
        Assert.Throws<SqliteException>(() => _db.CierreManga(manga, tipo: "Inventado"));
        Assert.Throws<SqliteException>(() => _db.CierreManga(manga, aviss: -1));
        Assert.Throws<SqliteException>(() => _db.CierreManga(manga, aviss: 5, confirmada: -2));
        // Diferencia que no es confirmada - Aviss, o diferencia sin confirmada.
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $m, 'Manual', 'x', 10, 12, 5, 0, 'x', 'x')",
            ("$id", Guid.NewGuid()), ("$m", manga)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $m, 'Manual', 'x', 10, NULL, 2, 0, 'x', 'x')",
            ("$id", Guid.NewGuid()), ("$m", manga)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $m, 'Manual', 'x', 10, 2, 'x', 'x')",
            ("$id", Guid.NewGuid()), ("$m", manga)));
    }

    [Fact]
    public void Cierre_con_manga_se_rechaza()
    {
        var (_, manga) = Base();
        Assert.Throws<SqliteException>(() => _db.CierreManga(Guid.NewGuid()));
    }

    [Fact]
    public void Cierre_sin_cantidad_confirmada_ni_diferencia_es_valido()
    {
        var (_, manga) = Base();
        _db.CierreManga(manga, "Automatico", aviss: 7);
        Assert.Null(_db.Escalar("SELECT Diferencia FROM CierreManga"));
    }

    // ---------- MarcaPasada ----------

    private (Guid rodeo, Guid manga, Guid animal) ConAnimal()
    {
        var (rodeo, manga) = Base();
        return (rodeo, manga, _db.Animal(rodeo, "100"));
    }

    [Fact]
    public void Marca_solo_acepta_pasar_Anulado_de_0_a_1()
    {
        var (_, manga, animal) = ConAnimal();
        var marca = _db.MarcaPasada(animal, manga);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET OrdenInsercion = 999 WHERE Id = $id", ("$id", marca)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET FechaHoraUtc = $f WHERE Id = $id", ("$id", marca), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddHours(1)))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET AnimalId = $a WHERE Id = $id", ("$id", marca), ("$a", _db.Animal(_db.Rodeo(), "5"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 1, OrdenInsercion = 999 WHERE Id = $id", ("$id", marca)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM MarcaPasada WHERE Id = $id", ("$id", marca)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM MarcaPasada"));

        Assert.Equal(1, _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 1, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", marca), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddMinutes(1)))));
    }

    [Fact]
    public void Una_marca_anulada_no_vuelve_a_vigente()
    {
        var (_, manga, animal) = ConAnimal();
        var marca = _db.MarcaPasada(animal, manga, anulada: true);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 0 WHERE Id = $id", ("$id", marca)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 1 WHERE Id = $id", ("$id", marca)));
    }

    [Fact]
    public void Una_vaca_tiene_una_sola_marca_vigente_por_manga()
    {
        var (rodeo, manga, animal) = ConAnimal();
        _db.MarcaPasada(animal, manga);
        Assert.Throws<SqliteException>(() => _db.MarcaPasada(animal, manga));

        // Otra vaca en la misma manga, o la misma vaca en otra manga, sí.
        _db.MarcaPasada(_db.Animal(rodeo, "101"), manga);
        _db.MarcaPasada(animal, _db.Manga(rodeo, 2));

        // Desmarcada, se puede marcar de nuevo en la misma manga (queda la anulada y la nueva vigente).
        _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 1 WHERE AnimalId = $a AND MangaId = $m", ("$a", animal), ("$m", manga));
        _db.MarcaPasada(animal, manga);
        Assert.Equal(4, _db.Contar("SELECT COUNT(*) FROM MarcaPasada"));
    }

    [Fact]
    public void El_orden_de_guardado_de_las_marcas_es_unico()
    {
        var (rodeo, manga, animal) = ConAnimal();
        _db.MarcaPasada(animal, manga, orden: 50);
        Assert.Throws<SqliteException>(() => _db.MarcaPasada(_db.Animal(rodeo, "101"), manga, orden: 50));
    }

    [Fact]
    public void Marcar_una_vaca_en_la_manga_de_otro_rodeo_se_rechaza()
    {
        var (_, _, animal) = ConAnimal();
        var otroRodeo = _db.Rodeo();
        var mangaAjena = _db.Manga(otroRodeo, 1);

        var ex = Assert.Throws<SqliteException>(() => _db.MarcaPasada(animal, mangaAjena));
        Assert.Contains("mismo rodeo", ex.Message);
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM MarcaPasada"));
    }

    [Fact]
    public void Marca_con_animal_manga_se_rechaza()
    {
        var (_, manga, animal) = ConAnimal();
        Assert.Throws<SqliteException>(() => _db.MarcaPasada(Guid.NewGuid(), manga));
        Assert.Throws<SqliteException>(() => _db.MarcaPasada(animal, Guid.NewGuid()));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM MarcaPasada"));
    }

    [Fact]
    public void Un_animal_con_marcas_no_se_puede_borrar()
    {
        var (_, manga, animal) = ConAnimal();
        _db.MarcaPasada(animal, manga);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Animal WHERE Id = $a", ("$a", animal)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Manga WHERE Id = $m", ("$m", manga)));
    }

    // ---------- Historial: acciones nuevas y triggers que siguen vivos ----------

    [Theory]
    [InlineData("MarcaPasada")]
    [InlineData("AnulacionMarca")]
    [InlineData("MangaAbierta")]
    [InlineData("MangaCerrada")]
    [InlineData("AnulacionCierre")]
    public void El_historial_acepta_las_acciones_nuevas(string accion)
    {
        _db.Historial(accion);
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Historial WHERE Accion = $a", ("$a", accion)));
    }

    [Theory]
    [InlineData("Alta")]
    [InlineData("Edicion")]
    [InlineData("Tacto")]
    [InlineData("AnulacionTacto")]
    [InlineData("AnulacionAlta")]
    public void El_historial_sigue_aceptando_las_acciones_anteriores(string accion)
        => Assert.NotEqual(Guid.Empty, _db.Historial(accion));

    [Fact]
    public void El_historial_rechaza_otra_accion()
    {
        Assert.Throws<SqliteException>(() => _db.Historial("Borrado"));
        Assert.Throws<SqliteException>(() => _db.Historial("marcapasada")); // distingue mayúsculas
    }

    [Fact]
    public void Los_triggers_del_historial_siguen_vivos_y_funcionan_con_las_acciones_nuevas()
    {
        Assert.True(_db.ExisteTrigger("TR_Historial_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Historial_NoBorrar"));
        var id = _db.Historial("MangaCerrada");
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'otro' WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", id)));
    }

    [Theory]
    [InlineData("TR_Manga_NoModificar")]
    [InlineData("TR_Manga_NoBorrar")]
    [InlineData("TR_CierreManga_NoModificar")]
    [InlineData("TR_CierreManga_NoBorrar")]
    [InlineData("TR_MarcaPasada_NoModificar")]
    [InlineData("TR_MarcaPasada_NoBorrar")]
    [InlineData("TR_MarcaPasada_MismoRodeo")]
    public void Existen_los_triggers_nuevos(string nombre)
        => Assert.True(_db.ExisteTrigger(nombre));
}
