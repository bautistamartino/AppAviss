using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 004, RF-8: "Editar" un movimiento puede cambiar entre tacto y rechazo en ambas direcciones, en una sola transacción:
/// se anula el original y se guarda el nuevo, o no cambia nada.
/// </summary>
public sealed class CorregirRegistroTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> CorregirAsync(Guid registro, Guid evento, string? obs = null)
        => _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(registro, evento, obs, _e.UsuarioId));

    private async Task<(Guid Animal, Guid Tacto)> VacaConTactoAsync()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        return (animal, tacto);
    }

    private async Task<(Guid Animal, Guid Rechazo)> VacaConRechazoAsync(string motivo = "pata")
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var r = await _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, EventosCampo.Rechazo, motivo, _e.UsuarioId));
        Assert.True(r.EsCorrecto, r.Mensaje);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        return (animal, r.Valor);
    }

    [Fact]
    public async Task Cambiar_un_tacto_por_un_rechazo_anula_el_tacto_devuelve_el_estado_y_guarda_el_rechazo()
    {
        var (animal, tacto) = await VacaConTactoAsync();

        var r = await CorregirAsync(tacto, EventosCampo.Rechazo, "  se cojea ");

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", tacto, "Anulado"));
        Assert.Equal(2, _e.Cuenta("Tacto"));                       // el original y su anulación; ningún tacto nuevo
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId")); // volvió al estado anterior
        Assert.Equal("se cojea", _e.Col("Rechazo", r.Valor, "Observaciones"));
        Assert.Equal("0", _e.Col("Rechazo", r.Valor, "Anulado"));
        Assert.Equal(T(animal), _e.Col("Rechazo", r.Valor, "AnimalId"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionTacto'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'RechazoCreado'"));
    }

    [Fact]
    public async Task Cambiar_un_rechazo_por_un_tacto_anula_el_rechazo_y_registra_el_tacto()
    {
        var (animal, rechazo) = await VacaConRechazoAsync();

        var r = await CorregirAsync(rechazo, _e.EstadoPrenada, "ya esta");

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", rechazo, "Anulado"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
        Assert.Equal("ya esta", _e.Col("Tacto", r.Valor, "Observaciones"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'RechazoAnulado'"));
    }

    [Fact]
    public async Task Cambiar_el_motivo_de_un_rechazo_lo_anula_y_guarda_otro_rechazo()
    {
        var (_, rechazo) = await VacaConRechazoAsync("pata");

        var r = await CorregirAsync(rechazo, EventosCampo.Rechazo, "ubre");

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", rechazo, "Anulado"));
        Assert.Equal("ubre", _e.Col("Rechazo", r.Valor, "Observaciones"));
        Assert.Equal(2, _e.Cuenta("Rechazo"));
        // el nuevo es el último registro vigente y se puede volver a editar o deshacer
        Assert.True((await _e.DeshacerAsync(TipoDeshacer.Rechazo, r.Valor)).EsCorrecto);
    }

    [Fact]
    public async Task Los_ordenes_de_guardado_son_distintos_y_crecen_al_corregir_en_cualquier_direccion()
    {
        var (_, tacto) = await VacaConTactoAsync();
        var aRechazo = await CorregirAsync(tacto, EventosCampo.Rechazo, "x");
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var aTacto = await CorregirAsync(aRechazo.Valor, _e.EstadoSeca);

        Assert.True(aTacto.EsCorrecto, aTacto.Mensaje);
        var ordenes = _e.Base.Escalar(@"SELECT group_concat(o) FROM (SELECT OrdenInsercion AS o FROM Tacto UNION ALL SELECT OrdenInsercion FROM Rechazo UNION ALL SELECT OrdenInsercion FROM MarcaPasada)")!.ToString()!
            .Split(',').Select(long.Parse).ToList();
        Assert.Equal(ordenes.Count, ordenes.Distinct().Count());
    }

    [Fact]
    public async Task Si_el_registro_nuevo_se_rechaza_no_cambia_nada_y_el_original_sigue_vigente()
    {
        var (_, rechazo) = await VacaConRechazoAsync();
        var estadoDeBaja = _e.Base.Estado("BAJA", "BAJA", "BAJA", activo: false);
        var antes = _e.FotoCampo();

        var r = await CorregirAsync(rechazo, estadoDeBaja);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.EstadoDeBaja, r.Codigo);
        Assert.Equal("0", _e.Col("Rechazo", rechazo, "Anulado"));
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'RechazoAnulado'"));
    }

    [Fact]
    public async Task Si_algo_falla_a_mitad_de_cambiar_un_tacto_por_un_rechazo_el_tacto_sigue_vigente()
    {
        var (animal, tacto) = await VacaConTactoAsync();
        _e.ForzarFallaAlGuardarHistorial("RechazoCreado"); // falla DESPUÉS de anular el tacto: tiene que revertirse todo
        var antes = _e.FotoCampo();

        await Assert.ThrowsAnyAsync<Exception>(() => CorregirAsync(tacto, EventosCampo.Rechazo, "x"));

        Assert.Equal("0", _e.Col("Tacto", tacto, "Anulado"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Solo_se_corrige_el_ultimo_registro_vigente_de_la_vaca()
    {
        var (animal, rechazo) = await VacaConRechazoAsync();
        await _e.TactoAsync(animal, _e.EstadoPrenada); // ahora el último es el tacto
        var antes = _e.FotoCampo();

        var r = await CorregirAsync(rechazo, EventosCampo.Rechazo, "otro");

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.RechazoNoEsElUltimo, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Corregir_un_registro_que_no_existe_se_rechaza()
    {
        var r = await CorregirAsync(Guid.NewGuid(), EventosCampo.Rechazo);
        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Corregir_un_registro_resuelve_la_espera_pendiente_de_la_vaca_en_la_misma_transaccion()
    {
        var (animal, tacto) = await VacaConTactoAsync();
        var espera = (await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(animal, _e.UsuarioId))).Valor;

        var r = await CorregirAsync(tacto, EventosCampo.Rechazo, "x");

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("Resuelta", _e.Col("Espera", espera, "Estado"));
        Assert.Equal(T(r.Valor), _e.Col("Espera", espera, "RegistroResolucionId"));
    }
}
