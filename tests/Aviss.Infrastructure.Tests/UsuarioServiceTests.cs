using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Domain.Entidades;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-7 y RF-7b: veterinarios y el usuario "Sin asignar".</summary>
public sealed class UsuarioServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new(conDatosBase: false);
    public void Dispose() => _e.Dispose();

    [Fact]
    public async Task Crea_un_usuario_con_nombre_y_matricula_opcional_y_sin_contrasena()
    {
        var r = await _e.Servicio<IUsuarioService>().CrearAsync("Dra. Pérez", "MP-123");
        Assert.True(r.EsCorrecto);
        Assert.Equal("MP-123", _e.Col("Usuario", r.Valor, "Matricula"));

        var sin = await _e.Servicio<IUsuarioService>().CrearAsync("Dr. López", null);
        Assert.True(sin.EsCorrecto);
        Assert.Null(_e.Col("Usuario", sin.Valor, "Matricula"));
    }

    [Fact]
    public async Task Matricula_de_30_caracteres_si_y_de_31_no()
    {
        Assert.True((await _e.Servicio<IUsuarioService>().CrearAsync("A", new string('9', 30))).EsCorrecto);
        var r = await _e.Servicio<IUsuarioService>().CrearAsync("B", new string('9', 31));
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task Rechaza_un_nombre_vacio()
    {
        var r = await _e.Servicio<IUsuarioService>().CrearAsync("  ", null);
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task Da_de_baja_y_reactiva_sin_borrar()
    {
        var id = (await _e.Servicio<IUsuarioService>().CrearAsync("Vet", null)).Valor;
        Assert.True((await _e.Servicio<IUsuarioService>().DarDeBajaAsync(id)).EsCorrecto);
        Assert.Equal("0", _e.Col("Usuario", id, "Activo"));
        Assert.True((await _e.Servicio<IUsuarioService>().ReactivarAsync(id)).EsCorrecto);
        Assert.Equal("1", _e.Col("Usuario", id, "Activo"));
        Assert.Equal(CodigoError.NoEncontrado, (await _e.Servicio<IUsuarioService>().DarDeBajaAsync(Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Sin_asignar_no_se_puede_dar_de_baja_siendo_el_unico_activo_pero_si_hay_otro()
    {
        _e.Base.Usuario("Sin asignar", id: Usuario.SinAsignarId);

        var rechazo = await _e.Servicio<IUsuarioService>().DarDeBajaAsync(Usuario.SinAsignarId);
        Assert.False(rechazo.EsCorrecto);
        Assert.Equal("1", _e.Col("Usuario", Usuario.SinAsignarId, "Activo"));

        await _e.Servicio<IUsuarioService>().CrearAsync("Otro", null);
        var ok = await _e.Servicio<IUsuarioService>().DarDeBajaAsync(Usuario.SinAsignarId);
        Assert.True(ok.EsCorrecto);
        Assert.Equal("0", _e.Col("Usuario", Usuario.SinAsignarId, "Activo"));
    }
}
