using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 004, Rechazo (RF-6, RF-7, RF-11, RF-12): un registro de campo más que NO cambia el estado de la vaca ni su último tacto,
/// entra en el orden de guardado compartido y en el "último vigente", se deshace y se muestra en movimientos, ficha y avisos.
/// </summary>
public sealed class RechazoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> RechazarAsync(Guid animal, string? motivo = "pata") =>
        _e.Campo.RegistrarRechazoAsync(new RegistrarRechazoCommand(animal, motivo));

    private async Task<Guid> RechazarOkAsync(Guid animal, string? motivo = "pata")
    {
        var r = await RechazarAsync(animal, motivo);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    [Fact]
    public async Task Guardar_un_rechazo_registra_el_motivo_la_hora_y_el_historial()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(5));

        var id = await RechazarOkAsync(animal, "  ubre dura ");

        Assert.Equal("ubre dura", _e.Col("Rechazo", id, "Observaciones"));
        Assert.Equal(T(animal), _e.Col("Rechazo", id, "AnimalId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Rechazo", id, "FechaHoraUtc"));
        Assert.Equal("0", _e.Col("Rechazo", id, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Historial", $"Accion = 'RechazoCreado' AND EntidadId = '{T(id)}'"));
    }

    [Fact]
    public async Task El_motivo_es_opcional()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await RechazarOkAsync(animal, "   ");
        Assert.Null(_e.Col("Rechazo", id, "Observaciones"));
    }

    [Fact]
    public async Task El_rechazo_no_cambia_el_estado_de_la_vaca_ni_su_ultimo_tacto_ni_crea_tactos()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var estadoAntes = _e.Col("Animal", animal, "EstadoReproductivoId");
        var ultimoTactoAntes = _e.Col("Animal", animal, "FechaUltimoTactoUtc");
        var actualizadoAntes = _e.Col("Animal", animal, "ActualizadoEnUtc");
        var tactosAntes = _e.Cuenta("Tacto");

        await RechazarOkAsync(animal);

        Assert.Equal(estadoAntes, _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(ultimoTactoAntes, _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(actualizadoAntes, _e.Col("Animal", animal, "ActualizadoEnUtc"));
        Assert.Equal(tactosAntes, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task El_rechazo_entra_en_el_orden_de_guardado_compartido()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var rechazo = await RechazarOkAsync(animal);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        var otra = await _e.VacaMarcadaAsync("200");

        var ordenes = new[]
        {
            long.Parse(_e.Col("MarcaPasada", lectura.MarcaId!.Value, "OrdenInsercion")!),
            long.Parse(_e.Col("Rechazo", rechazo, "OrdenInsercion")!),
            long.Parse(_e.Col("Tacto", tacto, "OrdenInsercion")!),
            long.Parse(_e.Col("MarcaPasada", otra.Lectura.MarcaId!.Value, "OrdenInsercion")!)
        };
        Assert.Equal(ordenes.OrderBy(o => o), ordenes);
        Assert.Equal(4, ordenes.Distinct().Count());
    }

    [Fact]
    public async Task El_rechazo_valida_el_reloj_como_los_demas_registros_y_no_guarda_nada()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var manga = _e.Base.Manga(_e.RodeoId, 9);
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddHours(2));

        var r = await RechazarAsync(animal);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoRegistro, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Rechazo"));
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'RechazoCreado'"));
    }

    [Fact]
    public async Task No_se_puede_rechazar_un_animal_anulado_ni_inexistente()
    {
        var anulado = _e.Base.Animal(_e.RodeoId, "100", "AltaEnCampo", anulado: true);
        Assert.Equal(CodigoError.AnimalAnulado, (await RechazarAsync(anulado)).Codigo);

        Assert.Equal(CodigoError.NoEncontrado, (await RechazarAsync(Guid.NewGuid())).Codigo);
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Un_motivo_de_mas_de_300_caracteres_se_rechaza()
    {
        var animal = _e.AnimalImportado("100");
        var r = await RechazarAsync(animal, new string('x', 301));
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Una_vaca_puede_tener_varios_rechazos()
    {
        var animal = _e.AnimalImportado("100");
        await RechazarOkAsync(animal, "uno");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await RechazarOkAsync(animal, "dos");

        Assert.Equal(2, _e.Cuenta("Rechazo"));
        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;
        Assert.Equal("dos", ficha.RechazoVigente!.Observaciones); // la vigente más reciente
    }

    // ------------------------------------------------------------------ Deshacer (RF-7)

    [Fact]
    public async Task Deshacer_un_rechazo_lo_marca_anulado_y_deja_historial_sin_tocar_nada_mas()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await RechazarOkAsync(animal);
        var tactosAntes = _e.Cuenta("Tacto");
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(30));

        var r = await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(animal, r.Valor.AnimalId);
        Assert.Equal("1", _e.Col("Rechazo", id, "Anulado"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Rechazo", id, "AnuladoEnUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", $"Accion = 'RechazoAnulado' AND EntidadId = '{T(id)}'"));
        Assert.Equal(tactosAntes, _e.Cuenta("Tacto"));
        Assert.Equal(1, _e.Cuenta("Rechazo")); // no se borra
    }

    [Fact]
    public async Task Deshacer_dos_veces_el_mismo_rechazo_se_rechaza()
    {
        var animal = _e.AnimalImportado("100");
        var id = await RechazarOkAsync(animal);
        await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        var otra = await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.Equal(CodigoError.RechazoYaAnulado, otra.Codigo);
    }

    [Fact]
    public async Task Solo_se_deshace_el_rechazo_si_es_el_ultimo_registro_vigente_de_la_vaca()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await RechazarOkAsync(animal);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        await _e.TactoAsync(animal, _e.EstadoPrenada); // después del rechazo

        var r = await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.Equal(CodigoError.RechazoNoEsElUltimo, r.Codigo);
        Assert.Equal("0", _e.Col("Rechazo", id, "Anulado"));
    }

    [Fact]
    public async Task Un_rechazo_posterior_impide_desmarcar_la_marca_de_pasada()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100");
        await RechazarOkAsync(animal);

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        Assert.Equal(CodigoError.MarcaNoEsLaUltima, r.Codigo);
    }

    // ------------------------------------------------------------------ Aviso de repetido (RF-7)

    [Fact]
    public async Task Leer_otra_vez_una_vaca_con_rechazo_muestra_el_rechazo_como_ultimo_registro_y_ofrece_corregir()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var id = await RechazarOkAsync(animal, "pata");

        var lectura = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Repetido, lectura.Tipo);
        Assert.Equal(TipoRegistroCampo.Rechazo, lectura.UltimoRegistro!.Tipo);
        Assert.Equal(id, lectura.UltimoRegistro.RegistroId);
        Assert.Equal("pata", lectura.UltimoRegistro.Observaciones);
        Assert.True(lectura.PuedeCorregir);
    }

    // ------------------------------------------------------------------ Movimientos (RF-7)

    [Fact]
    public async Task Los_movimientos_incluyen_el_rechazo_con_su_motivo_y_se_puede_deshacer_si_es_el_ultimo()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await RechazarOkAsync(animal, "pata");

        var movimientos = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        var m = Assert.Single(movimientos, x => x.Tipo == TipoMovimiento.Rechazo);
        Assert.Equal(id, m.RegistroId);
        Assert.Equal(animal, m.AnimalId);
        Assert.Equal("100", m.Caravana);
        Assert.Equal("Rechazo", m.Detalle);
        Assert.Equal("pata", m.Observaciones);
        Assert.False(m.Anulado);
        Assert.True(m.PuedeDeshacer);
        Assert.Equal(TipoMovimiento.Rechazo, movimientos[0].Tipo); // el más reciente primero
        // la marca de pasada ya no se puede deshacer: lo último es el rechazo
        Assert.False(movimientos.Single(x => x.Tipo == TipoMovimiento.Marca).PuedeDeshacer);
    }

    [Fact]
    public async Task Un_rechazo_anulado_sigue_en_los_movimientos_marcado_y_sin_deshacer()
    {
        var animal = _e.AnimalImportado("100");
        var id = await RechazarOkAsync(animal);
        await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        var m = Assert.Single(await _e.Campo.ListarMovimientosAsync(_e.RodeoId));

        Assert.True(m.Anulado);
        Assert.False(m.PuedeDeshacer);
    }

    // ------------------------------------------------------------------ Ficha (RF-11)

    [Fact]
    public async Task La_ficha_muestra_el_rechazo_vigente_y_lo_suma_al_historial_con_los_anulados_marcados()
    {
        var animal = _e.AnimalImportado("100");
        var primero = await RechazarOkAsync(animal, "pata");
        await _e.DeshacerAsync(TipoDeshacer.Rechazo, primero);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var segundo = await RechazarOkAsync(animal, "ubre");

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;

        Assert.Equal(segundo, ficha.RechazoVigente!.RechazoId);
        Assert.Equal("ubre", ficha.RechazoVigente.Observaciones);
        var eventos = ficha.Historial.Where(h => h.Titulo.StartsWith("Rechazo")).ToList();
        Assert.Equal(2, eventos.Count);
        Assert.Single(eventos, h => h.Anulado && h.Titulo == "Rechazo: pata");
        Assert.Single(eventos, h => !h.Anulado && h.Titulo == "Rechazo: ubre");
    }

    [Fact]
    public async Task Una_vaca_sin_rechazos_o_con_el_unico_anulado_no_tiene_rechazo_vigente()
    {
        var animal = _e.AnimalImportado("100");
        Assert.Null((await _e.Campo.ObtenerFichaAsync(animal)).Valor.RechazoVigente);

        var id = await RechazarOkAsync(animal);
        await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.Null((await _e.Campo.ObtenerFichaAsync(animal)).Valor.RechazoVigente);
    }

    // ------------------------------------------------------------------ Avance del día

    [Fact]
    public async Task Una_vaca_con_un_rechazo_de_hoy_cuenta_como_procesada_una_sola_vez()
    {
        var sola = _e.AnimalImportado("1");
        var (conMarca, _) = await _e.VacaMarcadaAsync("2");
        await RechazarOkAsync(sola);
        await RechazarOkAsync(conMarca); // ya contaba por su marca: no se duplica

        var estado = await _e.EstadoAsync();

        Assert.Equal(2, estado.Avance.Procesadas);
        Assert.Equal(1, estado.Contador); // el contador de la manga solo cuenta marcas
    }
}
