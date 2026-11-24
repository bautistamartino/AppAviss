using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Tests;

/// <summary>Spec 007, RF-21: las reglas de la constancia de una exportación.</summary>
public class ExportacionTests
{
    private static readonly DateTime Ahora = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    private static Exportacion Crear(Guid? rodeo = null, string? nombre = "rodeo_aviss_20261124_1200.csv", string? hash = null,
        int filas = 10, int conEvento = 4, FormatoExportacion formato = FormatoExportacion.Csv, DateTime? fecha = null)
        => Exportacion.Registrar(rodeo ?? Guid.NewGuid(), fecha ?? Ahora, nombre, formato, hash ?? Hash, filas, conEvento, Ahora);

    [Fact]
    public void Registrar_guarda_todos_los_datos()
    {
        var rodeo = Guid.NewGuid();
        var e = Crear(rodeo, formato: FormatoExportacion.Xls);
        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal(rodeo, e.RodeoId);
        Assert.Equal(Ahora, e.FechaUtc);
        Assert.Equal(Ahora, e.CreadoEnUtc);
        Assert.Equal("rodeo_aviss_20261124_1200.csv", e.ArchivoNombre);
        Assert.Equal(FormatoExportacion.Xls, e.Formato);
        Assert.Equal(Hash, e.Hash);
        Assert.Equal(10, e.CantidadFilas);
        Assert.Equal(4, e.CantidadVacasConEvento);
    }

    [Fact]
    public void Acepta_cero_filas_y_cero_vacas_con_evento()
        => Assert.Equal(0, Crear(filas: 0, conEvento: 0).CantidadFilas);

    [Fact]
    public void El_rodeo_es_obligatorio() => Assert.Throws<DominioException>(() => Crear(Guid.Empty));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_nombre_es_obligatorio(string? nombre) => Assert.Throws<DominioException>(() => Crear(nombre: nombre));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void La_huella_debe_tener_64_caracteres(string hash) => Assert.Throws<DominioException>(() => Crear(hash: hash));

    [Fact]
    public void Las_cantidades_no_pueden_ser_negativas()
    {
        Assert.Throws<DominioException>(() => Crear(filas: -1, conEvento: 0));
        Assert.Throws<DominioException>(() => Crear(conEvento: -1));
    }

    [Fact]
    public void Las_vacas_con_evento_no_pueden_superar_las_filas()
        => Assert.Throws<DominioException>(() => Crear(filas: 3, conEvento: 4));

    [Fact]
    public void La_fecha_debe_ser_UTC()
        => Assert.Throws<DominioException>(() => Crear(fecha: new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Local)));
}
