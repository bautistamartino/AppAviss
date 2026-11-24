using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Lo que la base garantiza por sí sola sobre la constancia de exportación (spec 007, RF-21): solo se agrega, formato, huella y
/// cantidades válidos, y el rodeo tiene que existir. Todo con SQL crudo, salteando la app.
/// </summary>
public sealed class EsquemaExportacionTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    private readonly Guid _rodeo;

    public EsquemaExportacionTests() => _rodeo = _db.Rodeo();
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Se_puede_agregar_una_constancia_valida_en_los_dos_formatos()
    {
        _db.Exportacion(_rodeo, formato: "Csv");
        _db.Exportacion(_rodeo, formato: "Xls");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Exportacion"));
    }

    [Fact]
    public void Una_constancia_no_se_modifica_ni_una_sola_columna()
    {
        var id = _db.Exportacion(_rodeo);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Exportacion SET CantidadFilas = 99 WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Exportacion SET ArchivoNombre = 'otro.csv' WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Exportacion SET Hash = $h WHERE Id = $id", ("$id", id), ("$h", new string('c', 64))));
        Assert.Equal(10, _db.Contar("SELECT CantidadFilas FROM Exportacion WHERE Id = $id", ("$id", id)));
    }

    [Fact]
    public void Una_constancia_no_se_borra()
    {
        var id = _db.Exportacion(_rodeo);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Exportacion WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Exportacion"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Exportacion"));
    }

    [Theory]
    [InlineData("csv")]       // distingue mayúsculas: el valor guardado es el nombre del enum
    [InlineData("Excel")]
    [InlineData("")]
    public void El_formato_solo_puede_ser_Csv_o_Xls(string formato)
        => Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, formato: formato));

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void La_huella_debe_tener_64_caracteres(int largo)
        => Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, hash: new string('a', largo)));

    [Fact]
    public void Las_cantidades_no_pueden_ser_negativas_ni_las_vacas_con_evento_superar_las_filas()
    {
        Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, filas: -1, conEvento: 0));
        Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, filas: 5, conEvento: -1));
        Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, filas: 3, conEvento: 4));
        _db.Exportacion(_rodeo, filas: 0, conEvento: 0);
    }

    [Fact]
    public void El_nombre_no_puede_estar_vacio()
    {
        Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, nombre: ""));
        Assert.Throws<SqliteException>(() => _db.Exportacion(_rodeo, nombre: "   "));
    }

    [Fact]
    public void El_rodeo_tiene_que_existir_y_un_rodeo_con_constancias_no_se_borra()
    {
        Assert.Throws<SqliteException>(() => _db.Exportacion(Guid.NewGuid()));
        _db.Exportacion(_rodeo);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rodeo WHERE Id = $id", ("$id", _rodeo)));
    }

    [Fact]
    public void Existen_el_indice_de_ultima_exportacion_y_los_dos_triggers_y_no_hay_columnas_de_usuario_ni_dispositivo()
    {
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Exportacion_RodeoId_FechaUtc'"));
        Assert.True(_db.ExisteTrigger("TR_Exportacion_NoModificar"));
        Assert.True(_db.ExisteTrigger("TR_Exportacion_NoBorrar"));
        // Spec 006: ni veterinario ni tablet.
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Exportacion') WHERE name LIKE '%Usuario%' OR name LIKE '%Dispositivo%'"));
    }
}
