using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 009, RF-1, RF-6, RF-8, RF-10, RF-11, RF-13: con la bandera "hay una ficha con tacto abierta", leer una vaca que
/// se marcaría y entra en tacto devuelve <see cref="TipoResultadoLectura.RequiereDecision"/> SIN guardar nada.
/// </summary>
public sealed class MarcaPasadaFichaAbiertaTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    /// <summary>Lee con la bandera encendida y exige que el servicio responda sin error.</summary>
    private async Task<ResultadoLecturaDto> LeerConFichaAbiertaAsync(string codigo)
    {
        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, codigo, HayFichaConTactoAbierta: true));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    [Fact]
    public void La_bandera_por_omision_esta_apagada_y_los_pedidos_viejos_siguen_compilando()
        => Assert.False(new ProcesarLecturaCommand(Guid.NewGuid(), "100").HayFichaConTactoAbierta);

    [Fact]
    public async Task Con_la_bandera_una_vaca_nueva_que_entra_en_tacto_pide_decision_sin_guardar_nada()
    {
        _e.AnimalImportado("100");
        var otra = _e.AnimalImportado("101");
        await _e.LeerAsync("100"); // la ficha abierta ya tiene su marca: manga 1, contador 1
        var antes = _e.FotoCampo();
        var historialAntes = _e.Cuenta("Historial");

        var r = await LeerConFichaAbiertaAsync("101");

        Assert.Equal(TipoResultadoLectura.RequiereDecision, r.Tipo);
        Assert.Equal(otra, r.AnimalId);
        Assert.Equal("101", r.Caravana);
        Assert.NotNull(r.Ficha);              // trae la ficha: el aviso muestra el CVET de la nueva
        Assert.True(r.Ficha!.EntraEnTacto);
        Assert.Null(r.MarcaId);
        Assert.Null(r.NumeroManga);
        Assert.Null(r.ContadorManga);
        // Ni marca, ni manga, ni historial, ni cambia el contador.
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(historialAntes, _e.Cuenta("Historial"));
        Assert.Equal(1, (await _e.EstadoAsync()).Contador);
    }

    [Fact]
    public async Task Con_la_bandera_y_sin_manga_abierta_no_se_abre_manga_ni_se_guarda_historial()
    {
        _e.AnimalImportado("100");

        var r = await LeerConFichaAbiertaAsync("100");

        Assert.Equal(TipoResultadoLectura.RequiereDecision, r.Tipo);
        Assert.Equal(0, _e.Cuenta("Manga"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    [Fact]
    public async Task El_segundo_pedido_sin_la_bandera_la_marca_como_siempre()
    {
        var animal = _e.AnimalImportado("100");
        Assert.Equal(TipoResultadoLectura.RequiereDecision, (await LeerConFichaAbiertaAsync("100")).Tipo);

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(animal, r.AnimalId);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Preguntar_dos_veces_no_deja_rastro_y_el_estado_de_campo_no_cambia()
    {
        _e.AnimalImportado("100");
        var antes = _e.FotoCampo();

        await LeerConFichaAbiertaAsync("100");
        await LeerConFichaAbiertaAsync("100");

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, (await _e.EstadoAsync()).Contador);
    }

    [Fact]
    public async Task Una_vaca_con_cvet_cero_se_marca_igual_y_devuelve_sin_tacto_aunque_haya_ficha_abierta()
    {
        var animal = _e.AnimalImportado("100", json: """{"CVET":"0"}""");

        var r = await LeerConFichaAbiertaAsync("100");

        Assert.Equal(TipoResultadoLectura.SinTacto, r.Tipo);
        Assert.Equal(animal, r.AnimalId);
        Assert.Null(r.Ficha);
        Assert.NotNull(r.MarcaId);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Una_vaca_ya_procesada_en_la_manga_actual_sigue_siendo_repetida_con_la_bandera()
    {
        await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var antes = _e.FotoCampo();
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));

        var r = await LeerConFichaAbiertaAsync("100");

        Assert.Equal(TipoResultadoLectura.Repetido, r.Tipo);
        Assert.Equal(TipoAviso.Repetido, r.Aviso);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Una_vaca_que_paso_en_otra_manga_o_con_tacto_de_hoy_sigue_siendo_ya_paso_con_la_bandera()
    {
        await _e.VacaMarcadaAsync("100");
        await _e.FinalizarAsync();
        var conTacto = _e.AnimalImportado("101", _e.EstadoSeca);
        await _e.TactoAsync(conTacto, _e.EstadoPrenada);

        var enOtraManga = await LeerConFichaAbiertaAsync("100");
        var sinMarca = await LeerConFichaAbiertaAsync("101");

        Assert.Equal(TipoResultadoLectura.YaPaso, enOtraManga.Tipo);
        Assert.Equal(TipoResultadoLectura.YaPaso, sinMarca.Tipo);
        Assert.Equal(TipoAviso.YaPasoSinManga, sinMarca.Aviso);
    }

    [Fact]
    public async Task Una_vaca_en_espera_sigue_saliendo_en_espera_con_la_bandera_sin_marcar()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var espera = await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(animal));
        Assert.True(espera.EsCorrecto, espera.Mensaje);
        var antes = _e.FotoCampo();

        var r = await LeerConFichaAbiertaAsync("100");

        Assert.Equal(TipoResultadoLectura.EnEspera, r.Tipo);
        Assert.NotNull(r.Ficha);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Una_caravana_que_no_existe_sigue_siendo_no_encontrada_con_la_bandera()
    {
        var r = await LeerConFichaAbiertaAsync("999");

        Assert.Equal(TipoResultadoLectura.NoEncontrada, r.Tipo);
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    [Fact]
    public async Task Un_reloj_atrasado_con_la_bandera_devuelve_el_error_de_siempre_sin_aviso()
    {
        var animal = _e.AnimalImportado("100");
        _e.Base.Tacto(animal, _e.EstadoSeca, fechaHora: _e.Reloj.UtcAhora.AddDays(2));
        var antes = _e.FotoCampo();

        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", HayFichaConTactoAbierta: true));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoRegistro, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Un_rodeo_de_baja_con_la_bandera_devuelve_el_error_de_siempre()
    {
        _e.AnimalImportado("100");
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");

        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", HayFichaConTactoAbierta: true));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.RodeoDeBaja, r.Codigo);
    }

    [Fact]
    public async Task Pedir_decision_no_deja_ninguna_fila_nueva_aunque_se_reabra_la_base()
    {
        _e.AnimalImportado("100");
        await LeerConFichaAbiertaAsync("100");

        // Un servicio nuevo (otro scope y otro DbContext): lo que no se guardó, no aparece.
        var estado = await _e.Servicio<ICampoService>().ObtenerEstadoTrabajoAsync(_e.RodeoId);
        Assert.Equal(0, estado.Valor.Contador);
        Assert.Equal(0, _e.Cuenta("Manga"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Historial"));
    }
}
