using Aviss.Application.Comandos;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-10: qué genera historial y qué no.</summary>
public sealed class HistorialTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    [Fact]
    public async Task Un_registro_por_alta_por_campo_editado_por_tacto_por_anulacion_de_tacto_y_por_anulacion_de_alta()
    {
        var animales = _e.Servicio<IAnimalService>();
        var alta = (await animales.AltaEnCampoAsync(new AltaEnCampoCommand(_e.RodeoId, "1", null, null, _e.UsuarioId))).Valor;
        Assert.Equal(1, _e.Cuenta("Historial"));                                  // alta

        await animales.EditarAsync(new EditarAnimalCommand(alta, "2", "ID", "obs", _e.UsuarioId));
        Assert.Equal(4, _e.Cuenta("Historial"));                                  // + caravana, id, observaciones

        var tacto = await _e.TactoAsync(alta, _e.EstadoSeca);
        Assert.Equal(5, _e.Cuenta("Historial"));                                  // + tacto

        await _e.AnularTactoAsync(tacto);
        Assert.Equal(6, _e.Cuenta("Historial"));                                  // + anulación de tacto

        await animales.AnularAltaAsync(new AnularAltaCommand(alta, _e.UsuarioId));
        Assert.Equal(7, _e.Cuenta("Historial"));                                  // + anulación de alta

        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'Alta'"));
        Assert.Equal(3, _e.Cuenta("Historial", "Accion = 'Edicion'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'Tacto'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionTacto'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionAlta'"));
    }

    [Fact]
    public async Task Los_estados_van_por_id_y_cada_registro_trae_usuario_y_fecha()
    {
        var otro = _e.Base.Usuario("Otro");
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(2));
        await _e.TactoAsync(animal, _e.EstadoPrenada, usuarioId: otro);

        Assert.Equal(T(_e.EstadoSeca), _e.Base.Escalar("SELECT ValorAnterior FROM Historial"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Base.Escalar("SELECT ValorNuevo FROM Historial"));
        Assert.Equal(T(otro), _e.Base.Escalar("SELECT UsuarioId FROM Historial"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Base.Escalar("SELECT FechaHoraUtc FROM Historial"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Base.Escalar("SELECT CreadoEnUtc FROM Historial"));
    }

    [Fact]
    public async Task Un_rechazo_no_genera_historial()
    {
        var animal = _e.AnimalCampo();
        await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoSeca, 999, null, _e.UsuarioId));
        await _e.Servicio<IAnimalService>().AltaEnCampoAsync(new AltaEnCampoCommand(_e.RodeoId, "100", null, null, _e.UsuarioId)); // caravana repetida
        Assert.Equal(0, _e.Cuenta("Historial"));
    }
}
