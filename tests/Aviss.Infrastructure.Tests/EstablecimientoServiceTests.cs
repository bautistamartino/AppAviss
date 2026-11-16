using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-1 y RF-24 para establecimientos.</summary>
public sealed class EstablecimientoServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new(conDatosBase: false);
    public void Dispose() => _e.Dispose();

    [Fact]
    public async Task Guarda_varios_establecimientos()
    {
        var a = await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo Norte", null);
        var b = await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo Sur", "cerca del río");
        Assert.True(a.EsCorrecto);
        Assert.True(b.EsCorrecto);
        Assert.NotEqual(a.Valor, b.Valor);
        var lista = await _e.Servicio<IEstablecimientoService>().ListarAsync();
        Assert.Equal(2, lista.Count);
    }

    [Fact]
    public async Task Las_notas_son_opcionales_y_el_nombre_se_recorta()
    {
        var r = await _e.Servicio<IEstablecimientoService>().CrearAsync("  Tambo Norte  ", "   ");
        Assert.True(r.EsCorrecto);
        Assert.Equal("Tambo Norte", _e.Col("Establecimiento", r.Valor, "Nombre"));
        Assert.Null(_e.Col("Establecimiento", r.Valor, "Notas"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Rechaza_un_nombre_vacio_sin_guardar_nada(string? nombre)
    {
        var r = await _e.Servicio<IEstablecimientoService>().CrearAsync(nombre!, null);
        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Establecimiento"));
    }

    [Fact]
    public async Task Da_de_baja_y_reactiva_sin_borrar()
    {
        var id = (await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo", null)).Valor;

        Assert.True((await _e.Servicio<IEstablecimientoService>().DarDeBajaAsync(id)).EsCorrecto);
        Assert.Equal("0", _e.Col("Establecimiento", id, "Activo"));
        Assert.Equal(1, _e.Cuenta("Establecimiento"));

        Assert.True((await _e.Servicio<IEstablecimientoService>().ReactivarAsync(id)).EsCorrecto);
        Assert.Equal("1", _e.Col("Establecimiento", id, "Activo"));
    }

    [Fact]
    public async Task Dar_de_baja_uno_inexistente_devuelve_no_encontrado()
    {
        var r = await _e.Servicio<IEstablecimientoService>().DarDeBajaAsync(Guid.NewGuid());
        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }
}
