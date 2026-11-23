using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-24: nada se borra, todo se da de baja.</summary>
public sealed class BajasTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    [Theory]
    [InlineData("Establecimiento")]
    [InlineData("Rodeo")]
    [InlineData("EstadoReproductivo")]
    [InlineData("Animal")]
    public void El_borrado_directo_aborta_y_no_cambia_nada(string tabla)
    {
        _e.AnimalCampo("1");
        var antes = _e.Cuenta(tabla);
        Assert.ThrowsAny<SqliteException>(() => _e.Base.Ejecutar($"DELETE FROM {tabla}"));
        Assert.Equal(antes, _e.Cuenta(tabla));
    }

    [Fact]
    public async Task Una_baja_no_altera_tactos_historial_ni_animales_ya_guardados()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        var antes = _e.Foto();

        await _e.Servicio<IEstablecimientoService>().DarDeBajaAsync(_e.EstablecimientoId);
        await _e.Servicio<IRodeoService>().DarDeBajaAsync(_e.RodeoId);        await _e.Servicio<ICatalogoEstadosService>().DesactivarAsync(_e.EstadoPrenada);

        Assert.Equal(antes, _e.Foto());
        Assert.Equal(1, _e.Cuenta("Tacto"));
        Assert.Equal(1, _e.Cuenta("Historial"));
    }

    [Fact]
    public async Task Rodeo_y_establecimiento_de_baja_no_admiten_altas_ni_tactos()
    {
        var animal = _e.AnimalCampo("1");
        await _e.Servicio<IRodeoService>().DarDeBajaAsync(_e.RodeoId);

        var alta = await _e.Servicio<IAnimalService>().AltaEnCampoAsync(new AltaEnCampoCommand(_e.RodeoId, "2", null, null));
        Assert.Equal(CodigoError.RodeoDeBaja, alta.Codigo);
        var tacto = await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoSeca, null, null));
        Assert.Equal(CodigoError.RodeoDeBaja, tacto.Codigo);

        await _e.Servicio<IRodeoService>().DarDeBajaAsync(_e.RodeoId); // idempotente
        await _e.Servicio<IEstablecimientoService>().DarDeBajaAsync(_e.EstablecimientoId);
        var rodeoNuevo = await _e.Servicio<IRodeoService>().CrearAsync(
            new CrearRodeoCommand(_e.EstablecimientoId, "R", _e.Reloj.UtcAhora, "a.xls", new string('a', 64), new byte[] { 1 }, "DairyComp"));
        Assert.Equal(CodigoError.EstablecimientoDeBaja, rodeoNuevo.Codigo);
    }

    [Fact]
    public async Task Estado_de_baja_no_se_elige_pero_sigue_figurando_en_lo_anterior()
    {
        var animal = _e.AnimalCampo("1");
        var tacto = await _e.TactoAsync(animal, _e.EstadoSeca);

        await _e.Servicio<ICatalogoEstadosService>().DesactivarAsync(_e.EstadoSeca);

        var conEstadoBaja = await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoSeca, null, null));
        Assert.Equal(CodigoError.EstadoDeBaja, conEstadoBaja.Codigo);

        // Lo ya guardado sigue apuntando al estado dado de baja.
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoSeca), _e.Col("Tacto", tacto, "EstadoNuevoId"));
    }
}
