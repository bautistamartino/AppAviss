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

    // ---- Renombrar ----

    [Fact]
    public async Task Renombra_el_establecimiento_y_deja_el_resto_igual()
    {
        var id = (await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo Norte", "notas")).Valor;

        var r = await _e.Servicio<IEstablecimientoService>().RenombrarAsync(id, "  Tambo Nuevo ");

        Assert.True(r.EsCorrecto);
        Assert.Equal("Tambo Nuevo", _e.Col("Establecimiento", id, "Nombre"));
        Assert.Equal("notas", _e.Col("Establecimiento", id, "Notas"));
        Assert.Equal("1", _e.Col("Establecimiento", id, "Activo"));
        Assert.Equal(1, _e.Cuenta("Establecimiento"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Renombrar_con_nombre_vacio_se_rechaza_y_no_cambia_nada(string? nombre)
    {
        var id = (await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo Norte", null)).Valor;

        var r = await _e.Servicio<IEstablecimientoService>().RenombrarAsync(id, nombre!);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal("Tambo Norte", _e.Col("Establecimiento", id, "Nombre"));
    }

    [Theory]
    [InlineData("Tambo Sur")]
    [InlineData("  tambo sur ")]
    [InlineData("TAMBO SUR")]
    public async Task Renombrar_con_el_nombre_de_otro_se_rechaza_sin_distinguir_mayusculas(string nuevo)
    {
        var s = _e.Servicio<IEstablecimientoService>();
        var id = (await s.CrearAsync("Tambo Norte", null)).Valor;
        await s.CrearAsync("Tambo Sur", null);

        var r = await s.RenombrarAsync(id, nuevo);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensaje));
        Assert.Equal("Tambo Norte", _e.Col("Establecimiento", id, "Nombre"));
    }

    [Fact]
    public async Task Renombrar_con_el_nombre_de_otro_dado_de_baja_tambien_se_rechaza()
    {
        var s = _e.Servicio<IEstablecimientoService>();
        var id = (await s.CrearAsync("Tambo Norte", null)).Valor;
        var baja = (await s.CrearAsync("Tambo Sur", null)).Valor;
        await s.DarDeBajaAsync(baja);

        var r = await s.RenombrarAsync(id, "Tambo Sur");

        Assert.Equal(CodigoError.Duplicado, r.Codigo);
    }

    [Fact]
    public async Task Renombrar_al_mismo_nombre_propio_o_cambiando_mayusculas_no_es_error()
    {
        var s = _e.Servicio<IEstablecimientoService>();
        var id = (await s.CrearAsync("Tambo Norte", null)).Valor;
        await s.CrearAsync("Tambo Sur", null);

        Assert.True((await s.RenombrarAsync(id, "Tambo Norte")).EsCorrecto);
        Assert.True((await s.RenombrarAsync(id, "TAMBO NORTE")).EsCorrecto);
        Assert.Equal("TAMBO NORTE", _e.Col("Establecimiento", id, "Nombre"));
    }

    [Fact]
    public async Task Renombrar_uno_inexistente_o_de_baja_devuelve_no_encontrado()
    {
        var s = _e.Servicio<IEstablecimientoService>();
        Assert.Equal(CodigoError.NoEncontrado, (await s.RenombrarAsync(Guid.NewGuid(), "Otro")).Codigo);

        var id = (await s.CrearAsync("Tambo Norte", null)).Valor;
        await s.DarDeBajaAsync(id);
        var r = await s.RenombrarAsync(id, "Otro");

        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
        Assert.Equal("Tambo Norte", _e.Col("Establecimiento", id, "Nombre"));
    }

    [Fact]
    public async Task Renombrar_por_la_fachada_de_campo_cambia_el_nombre()
    {
        var id = (await _e.Servicio<IEstablecimientoService>().CrearAsync("Tambo Norte", null)).Valor;

        var r = await _e.Campo.RenombrarEstablecimientoAsync(id, "Tambo Nuevo");

        Assert.True(r.EsCorrecto);
        Assert.Equal("Tambo Nuevo", _e.Col("Establecimiento", id, "Nombre"));
    }
}
