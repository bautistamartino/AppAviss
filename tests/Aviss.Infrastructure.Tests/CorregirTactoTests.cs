using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// "Editar" un movimiento de tacto: como los tactos solo se agregan, se corrige anulando el tacto y registrando uno nuevo
/// con el evento y la observación elegidos, todo en una sola transacción (si algo falla, no queda nada a medias).
/// </summary>
public sealed class CorregirTactoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private async Task<(Guid Animal, Guid Tacto)> VacaConTactoAsync()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        return (animal, tacto);
    }

    [Fact]
    public async Task Corregir_anula_el_tacto_y_registra_uno_nuevo_con_el_evento_y_la_observacion()
    {
        var (animal, tacto) = await VacaConTactoAsync();
        var vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA");

        var r = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, vacia, "  Revisar pata ", _e.UsuarioId));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", tacto, "Anulado"));                 // el viejo queda anulado, no se borra
        Assert.Equal(3, _e.Cuenta("Tacto"));                                  // el original, su anulación y el nuevo
        Assert.Equal(EscenarioConServicios.Texto(vacia), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(EscenarioConServicios.Texto(vacia), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
        Assert.Equal("Revisar pata", _e.Col("Tacto", r.Valor, "Observaciones"));
        Assert.Equal("0", _e.Col("Tacto", r.Valor, "Anulado"));
        Assert.NotNull(_e.Col("Animal", animal, "FechaUltimoTactoUtc"));
    }

    [Fact]
    public async Task Corregir_deja_el_historial_con_la_anulacion_y_el_tacto_nuevo()
    {
        var (_, tacto) = await VacaConTactoAsync();
        var vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA");

        await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, vacia, null, _e.UsuarioId));

        Assert.Equal(2, _e.Cuenta("Historial", "Accion = 'Tacto'"));            // el original y el nuevo
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionTacto'"));
    }

    [Fact]
    public async Task Despues_de_corregir_el_nuevo_tacto_es_el_ultimo_y_se_puede_deshacer_o_volver_a_corregir()
    {
        var (_, tacto) = await VacaConTactoAsync();
        var vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA");
        var primera = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, vacia, null, _e.UsuarioId));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));

        var segunda = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(primera.Valor, _e.EstadoPrenada, "otra vez", _e.UsuarioId));

        Assert.True(segunda.EsCorrecto, segunda.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", primera.Valor, "Anulado"));
        Assert.Equal("otra vez", _e.Col("Tacto", segunda.Valor, "Observaciones"));
    }

    [Fact]
    public async Task Si_despues_del_tacto_hay_otro_registro_no_se_puede_corregir_y_no_cambia_nada()
    {
        var (animal, tacto) = await VacaConTactoAsync();
        var vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA");
        // Una marca de pasada posterior (por ejemplo, otro día): el tacto ya no es el último registro vigente.
        var manga = _e.Base.Manga(_e.RodeoId, 9, _e.UsuarioId);
        _e.Base.MarcaPasada(animal, manga, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora, orden: 1000);

        var r = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, vacia, null, _e.UsuarioId));

        Assert.Equal(CodigoError.TactoNoEsElUltimo, r.Codigo);
        Assert.Equal("0", _e.Col("Tacto", tacto, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task Si_el_evento_nuevo_no_sirve_no_queda_nada_a_medias_y_el_tacto_original_sigue_vigente()
    {
        var (animal, tacto) = await VacaConTactoAsync();
        var deBaja = _e.Base.Estado("BAJA", "BAJA", "BAJA", activo: false);

        var r = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, deBaja, null, _e.UsuarioId));

        Assert.False(r.EsCorrecto);
        Assert.Equal("0", _e.Col("Tacto", tacto, "Anulado"));                 // no se anuló
        Assert.Equal(1, _e.Cuenta("Tacto"));                                  // y no se agregó ninguno
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'AnulacionTacto'"));
    }

    [Fact]
    public async Task Un_tacto_que_no_existe_se_rechaza()
    {
        var r = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(Guid.NewGuid(), _e.EstadoPrenada, null, _e.UsuarioId));

        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Un_tacto_ya_anulado_no_se_puede_corregir()
    {
        var (_, tacto) = await VacaConTactoAsync();
        await _e.DeshacerAsync(Aviss.Application.Campo.TipoDeshacer.Tacto, tacto);

        var r = await _e.Campo.CorregirTactoAsync(new CorregirTactoCommand(tacto, _e.EstadoPrenada, null, _e.UsuarioId));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.TactoYaAnulado, r.Codigo);
    }
}
