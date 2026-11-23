using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-15, RF-16, RF-21: el tacto con un toque ya mira el último registro de CUALQUIER tipo (tacto o marca).</summary>
public sealed class RegistrarTactoConMarcaTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    [Fact]
    public async Task Despues_de_la_marca_un_toque_en_un_estado_guarda_el_tacto_por_la_fachada()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoPrenada, null, null));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
        Assert.Equal(1, _e.Cuenta("MarcaPasada", "Anulado = 0")); // la marca sigue vigente
    }

    [Fact]
    public async Task El_tacto_se_rechaza_si_es_anterior_a_la_ultima_marca_de_la_vaca()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var manga = _e.Base.Manga(_e.RodeoId, 1);
        // Una marca dos días "en el futuro": la hora de la tablet quedó atrasada.
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddDays(2));
        var antes = _e.FotoCampo();

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoPrenada, null, null));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoTacto, r.Codigo);
        Assert.Contains("hora", r.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Una_marca_anulada_ya_no_cuenta_para_la_validacion_del_reloj()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var manga = _e.Base.Manga(_e.RodeoId, 1);
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddDays(2), anulada: true);

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoPrenada, null, null));

        Assert.True(r.EsCorrecto, r.Mensaje);
    }

    [Fact]
    public async Task El_tacto_sigue_rechazando_un_estado_de_baja_y_deja_la_marca_intacta()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100");
        var estadoBaja = _e.Base.Estado("VIEJO", "VIEJO", "VIEJO", activo: false);
        var antes = _e.FotoCampo();

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, estadoBaja, null, null));

        Assert.Equal(CodigoError.EstadoDeBaja, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Tocar_el_estado_que_la_vaca_ya_tiene_registra_un_tacto_sin_cambio()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoSeca, null, null));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoSeca), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
        Assert.Equal(EscenarioConServicios.Texto(_e.EstadoSeca), _e.Col("Tacto", r.Valor, "EstadoAnteriorId"));
    }

    [Fact]
    public async Task Una_vaca_con_tacto_cuenta_como_procesada_aunque_no_tenga_marca()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        _e.AnimalImportado("101");
        await _e.TactoAsync(animal, _e.EstadoPrenada);

        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Equal(2, avance.TotalVacas);
        Assert.Equal(1, avance.Procesadas);
    }

    [Fact]
    public async Task Los_estados_activos_se_listan_para_los_botones_de_la_ficha()
    {
        _e.Base.Estado("VIEJO", "VIEJO", "VIEJO", activo: false);

        var lista = await _e.Campo.ListarEventosAsync();
        var estados = lista.Principales.Concat(lista.Otros.Where(o => o.Id != EventosCampo.Rechazo)).ToList();

        Assert.Equal(["PREÑADA", "SECA"], estados.Select(s => s.Nombre).Order());
        Assert.DoesNotContain(estados, s => s.Nombre == "VIEJO");
    }
}
