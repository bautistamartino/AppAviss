using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-5: catálogo de estados reproductivos configurable.</summary>
public sealed class CatalogoEstadosServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private ICatalogoEstadosService Catalogo() => _e.Servicio<ICatalogoEstadosService>();

    [Fact]
    public async Task Agrega_un_estado_nuevo_sin_tocar_codigo()
    {
        var r = await Catalogo().CrearAsync("ABORTO", "Aborto", "ABORT");
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("ABORTO", _e.Col("EstadoReproductivo", r.Valor, "Codigo"));
        Assert.Equal("1", _e.Col("EstadoReproductivo", r.Valor, "Activo"));
        Assert.Equal("ABORT", _e.Col("EstadoReproductivo", r.Valor, "CodigoDairyComp"));
    }

    [Theory]
    [InlineData("aborto")]
    [InlineData("CON ESPACIO")]
    [InlineData("PREÑADA2")]
    [InlineData("")]
    public async Task Rechaza_un_codigo_interno_con_formato_invalido(string codigo)
    {
        var antes = _e.Cuenta("EstadoReproductivo");
        var r = await Catalogo().CrearAsync(codigo, "Nombre", "DC");
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal(antes, _e.Cuenta("EstadoReproductivo"));
    }

    [Fact]
    public async Task Rechaza_un_codigo_interno_repetido()
    {
        var r = await Catalogo().CrearAsync("SECA", "Otra seca", "OTRA");
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
    }

    [Theory]
    [InlineData("seca")]
    [InlineData("preñada")] // la Ñ minúscula tiene que chocar con la mayúscula
    [InlineData("PREÑADA")]
    public async Task Rechaza_un_nombre_repetido_sin_distinguir_mayusculas_ni_la_enie(string nombre)
    {
        var r = await Catalogo().CrearAsync("NUEVO", nombre, "NUEVO_DC");
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
    }

    [Fact]
    public async Task El_codigo_de_dairy_comp_es_unico_solo_entre_activos()
    {
        var choque = await Catalogo().CrearAsync("OTRO", "Otro", "SECA");
        Assert.Equal(CodigoError.CodigoDairyCompDuplicado, choque.Codigo);

        // Al desactivar SECA su código queda libre...
        Assert.True((await Catalogo().DesactivarAsync(_e.EstadoSeca)).EsCorrecto);
        var libre = await Catalogo().CrearAsync("OTRO", "Otro", "SECA");
        Assert.True(libre.EsCorrecto, libre.Mensaje);

        // ...y por eso reactivar SECA ahora choca.
        var reactivar = await Catalogo().ActivarAsync(_e.EstadoSeca);
        Assert.Equal(CodigoError.CodigoDairyCompDuplicado, reactivar.Codigo);
        Assert.Equal("0", _e.Col("EstadoReproductivo", _e.EstadoSeca, "Activo"));
    }

    [Fact]
    public async Task Modifica_nombre_y_codigo_de_dairy_comp_pero_no_el_codigo_interno()
    {
        var r = await Catalogo().ModificarAsync(_e.EstadoSeca, "Seca nueva", "SECA2");
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("Seca nueva", _e.Col("EstadoReproductivo", _e.EstadoSeca, "Nombre"));
        Assert.Equal("SECA2", _e.Col("EstadoReproductivo", _e.EstadoSeca, "CodigoDairyComp"));
        Assert.Equal("SECA", _e.Col("EstadoReproductivo", _e.EstadoSeca, "Codigo"));
    }

    [Fact]
    public async Task Modificar_rechaza_nombre_o_codigo_de_otro_estado_pero_acepta_los_propios()
    {
        Assert.True((await Catalogo().ModificarAsync(_e.EstadoSeca, "seca", "SECA")).EsCorrecto); // cambia solo mayúsculas del suyo
        Assert.Equal(CodigoError.Duplicado, (await Catalogo().ModificarAsync(_e.EstadoSeca, "preñada", "SECA")).Codigo);
        Assert.Equal(CodigoError.CodigoDairyCompDuplicado, (await Catalogo().ModificarAsync(_e.EstadoSeca, "Seca", "PREÑADA")).Codigo);
        Assert.Equal(CodigoError.NoEncontrado, (await Catalogo().ModificarAsync(Guid.NewGuid(), "X", "X")).Codigo);
    }

    [Fact]
    public async Task Renombrar_no_rompe_tactos_ni_historial_porque_van_por_id()
    {
        var animal = _e.AnimalCampo();
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        await Catalogo().ModificarAsync(_e.EstadoPrenada, "Gestante", "GESTANTE");

        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoPrenada), _e.Col("Tacto", tacto, "EstadoNuevoId"));
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(1, _e.Cuenta("Historial", $"ValorNuevo = '{EscenarioConServicios.Texto(_e.EstadoPrenada)}'"));
    }

    [Fact]
    public async Task Desactiva_y_activa_sin_borrar()
    {
        Assert.True((await Catalogo().DesactivarAsync(_e.EstadoSeca)).EsCorrecto);
        Assert.Equal("0", _e.Col("EstadoReproductivo", _e.EstadoSeca, "Activo"));
        Assert.True((await Catalogo().ActivarAsync(_e.EstadoSeca)).EsCorrecto);
        Assert.Equal("1", _e.Col("EstadoReproductivo", _e.EstadoSeca, "Activo"));
        Assert.Equal(CodigoError.NoEncontrado, (await Catalogo().DesactivarAsync(Guid.NewGuid())).Codigo);
    }
}
