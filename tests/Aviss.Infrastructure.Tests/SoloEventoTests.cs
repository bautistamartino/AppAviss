using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 005, RF-16, RF-17, RF-18, RF-22: OK, Aborto y Revisar Nuevamente son eventos del catálogo de tipo "solo evento": se guardan
/// como un registro (tabla Rechazo con EventoId) que no cambia el estado de la vaca ni su último tacto, entran en el orden compartido,
/// se deshacen y se editan entre estado, solo evento y Rechazo sin dejar nada a medias.
/// </summary>
public sealed class SoloEventoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    private readonly Guid _ok;
    private readonly Guid _aborto;
    private readonly Guid _revisar;
    public void Dispose() => _e.Dispose();

    public SoloEventoTests()
    {
        _ok = _e.Base.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK", principal: true, orden: 2);
        _revisar = _e.Base.Estado("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", tipo: "SoloEvento", clave: "REVISAR_NUEVAMENTE", principal: true, orden: 1);
        _aborto = _e.Base.Estado("ABORTO", "Aborto", "ABORTO", tipo: "SoloEvento", clave: "ABORTO", principal: true, orden: 5);
    }

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> GuardarAsync(Guid animal, Guid evento, string? obs = null)
        => _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, evento, obs, _e.UsuarioId));

    private async Task<Guid> GuardarOkAsync(Guid animal, Guid evento, string? obs = null)
    {
        var r = await GuardarAsync(animal, evento, obs);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("Aborto")]
    [InlineData("Revisar Nuevamente")]
    public async Task Cada_solo_evento_se_guarda_sin_cambiar_el_estado_ni_el_ultimo_tacto(string nombre)
    {
        var evento = nombre switch { "OK" => _ok, "Aborto" => _aborto, _ => _revisar };
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var estado = _e.Col("Animal", animal, "EstadoReproductivoId");
        var ultimoTacto = _e.Col("Animal", animal, "FechaUltimoTactoUtc");
        var actualizado = _e.Col("Animal", animal, "ActualizadoEnUtc");
        var tactos = _e.Cuenta("Tacto");

        var id = await GuardarOkAsync(animal, evento, "  nota ");

        Assert.Equal(estado, _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(ultimoTacto, _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(actualizado, _e.Col("Animal", animal, "ActualizadoEnUtc"));
        Assert.Equal(tactos, _e.Cuenta("Tacto"));
        // Observación, usuario, dispositivo y la hora exacta del reloj; el evento queda en EventoId.
        Assert.Equal("nota", _e.Col("Rechazo", id, "Observaciones"));
        Assert.Equal(T(_e.UsuarioId), _e.Col("Rechazo", id, "UsuarioId"));
        Assert.Equal(T(_e.DispositivoId), _e.Col("Rechazo", id, "DispositivoId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Rechazo", id, "FechaHoraUtc"));
        Assert.Equal(T(evento), _e.Col("Rechazo", id, "EventoId"));
        // El historial lleva el nombre del evento.
        Assert.Equal(nombre, _e.Base.Escalar($"SELECT ValorNuevo FROM Historial WHERE Accion = 'RechazoCreado' AND EntidadId = '{T(id)}'")?.ToString());
    }

    [Fact]
    public async Task El_Rechazo_fijo_guarda_EventoId_nulo()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await GuardarOkAsync(animal, EventosCampo.Rechazo, "pata");
        Assert.Null(_e.Col("Rechazo", id, "EventoId"));
    }

    [Fact]
    public async Task Un_evento_inactivo_inexistente_o_que_es_un_estado_se_rechaza_sin_guardar_nada()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var inactivo = _e.Base.Estado("VIEJO_OK", "Viejo OK", "VIEJO OK", activo: false, tipo: "SoloEvento");
        var antes = _e.FotoCampo();

        Assert.Equal(CodigoError.EstadoDeBaja, (await GuardarAsync(animal, inactivo)).Codigo);
        Assert.Equal(CodigoError.NoEncontrado, (await GuardarAsync(animal, Guid.NewGuid())).Codigo);
        Assert.Equal(CodigoError.DatosInvalidos, (await GuardarAsync(animal, _e.EstadoSeca)).Codigo);

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Un_solo_evento_no_se_puede_guardar_como_tacto()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);

        var r = await _e.Servicio<ITrabajoCampoService>()
            .RegistrarTactoAsync(new RegistrarTactoCommand(animal, _ok, null, null, _e.UsuarioId));

        Assert.False(r.EsCorrecto);
        Assert.Equal(0, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task Si_falla_el_historial_no_queda_nada_a_medias()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        _e.ForzarFallaAlGuardarHistorial("RechazoCreado");

        await Assert.ThrowsAnyAsync<Exception>(() => GuardarAsync(animal, _ok));

        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Se_deshace_y_conserva_la_fecha_y_hora_exactas_del_registro()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(7));
        var id = await GuardarOkAsync(animal, _ok);
        var fecha = _e.Col("Rechazo", id, "FechaHoraUtc");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));

        var r = await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", id, "Anulado"));
        Assert.Equal(fecha, _e.Col("Rechazo", id, "FechaHoraUtc"));   // anular no pisa la hora original (RF-22)
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'RechazoAnulado'"));
    }

    [Fact]
    public async Task Solo_se_deshace_el_ultimo_y_comparte_el_orden_con_tactos_y_marcas()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var ok = await GuardarOkAsync(animal, _ok);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        Assert.False((await _e.DeshacerAsync(TipoDeshacer.Rechazo, ok)).EsCorrecto);
        Assert.True(long.Parse(_e.Col("Tacto", tacto, "OrdenInsercion")!) > long.Parse(_e.Col("Rechazo", ok, "OrdenInsercion")!));
    }

    // ---- Corregir (editar) en todas las direcciones ----

    private async Task<(Guid Animal, Guid Registro)> VacaConAsync(string que)
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = que == "tacto" ? await _e.TactoAsync(animal, _e.EstadoPrenada)
            : que == "ok" ? await GuardarOkAsync(animal, _ok, "x")
            : await GuardarOkAsync(animal, EventosCampo.Rechazo, "pata");
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        return (animal, id);
    }

    private Task<Resultado<Guid>> CorregirAsync(Guid registro, Guid evento, string? obs = null)
        => _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(registro, evento, obs, _e.UsuarioId));

    [Fact]
    public async Task Corregir_un_tacto_por_un_solo_evento_devuelve_el_estado_y_guarda_el_evento()
    {
        var (animal, tacto) = await VacaConAsync("tacto");

        var r = await CorregirAsync(tacto, _aborto, "se perdió");

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", tacto, "Anulado"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_aborto), _e.Col("Rechazo", r.Valor, "EventoId"));
        Assert.Equal("se perdió", _e.Col("Rechazo", r.Valor, "Observaciones"));
    }

    [Fact]
    public async Task Corregir_un_solo_evento_por_un_estado_lo_anula_y_guarda_un_tacto()
    {
        var (animal, ok) = await VacaConAsync("ok");

        var r = await CorregirAsync(ok, _e.EstadoPrenada);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", ok, "Anulado"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
    }

    [Fact]
    public async Task Corregir_entre_solo_evento_y_Rechazo_en_ambas_direcciones()
    {
        var (_, ok) = await VacaConAsync("ok");
        var aRechazo = await CorregirAsync(ok, EventosCampo.Rechazo, "pata");
        Assert.True(aRechazo.EsCorrecto, aRechazo.Mensaje);
        Assert.Null(_e.Col("Rechazo", aRechazo.Valor, "EventoId"));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));

        var aAborto = await CorregirAsync(aRechazo.Valor, _aborto);

        Assert.True(aAborto.EsCorrecto, aAborto.Mensaje);
        Assert.Equal(T(_aborto), _e.Col("Rechazo", aAborto.Valor, "EventoId"));
        Assert.Equal("1", _e.Col("Rechazo", aRechazo.Valor, "Anulado"));
    }

    [Fact]
    public async Task Si_el_evento_nuevo_se_rechaza_el_original_sigue_vigente()
    {
        var (_, tacto) = await VacaConAsync("tacto");
        var inactivo = _e.Base.Estado("VIEJO_OK", "Viejo OK", "VIEJO OK", activo: false, tipo: "SoloEvento");

        var r = await CorregirAsync(tacto, inactivo);

        Assert.False(r.EsCorrecto);
        Assert.Equal("0", _e.Col("Tacto", tacto, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    // ---- Lo que se ve: movimientos, ficha y aviso ----

    [Fact]
    public async Task Movimientos_ficha_y_aviso_muestran_el_nombre_del_evento_aunque_se_desactive_despues()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await GuardarOkAsync(animal, _aborto, "tarde");
        _e.Base.Ejecutar("UPDATE EstadoReproductivo SET Activo = 0 WHERE Id = $i", ("$i", _aborto));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);
        Assert.Equal(("Aborto", (Guid?)_aborto, TipoMovimiento.Rechazo), (mov.Detalle, mov.EventoId, mov.Tipo));

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;
        Assert.Contains(ficha.Historial, h => h.Titulo == "Aborto: tarde");
        Assert.Null(ficha.RechazoVigente);   // un Aborto no es la marca de rechazo

        var lista = await _e.Campo.ListarEventosAsync();
        Assert.DoesNotContain(lista.Principales.Concat(lista.Otros), x => x.Id == _aborto);   // desactivado: no se ofrece

        var lectura = await _e.LeerAsync("100");
        Assert.Equal(TipoResultadoLectura.Repetido, lectura.Tipo);
        Assert.Equal("Aborto", lectura.UltimoRegistro!.EstadoNombre);
        Assert.Equal(TipoRegistroCampo.Rechazo, lectura.UltimoRegistro.Tipo);
    }

    [Fact]
    public async Task Un_tacto_y_un_rechazo_fijo_traen_su_EventoId_en_movimientos()
    {
        var (_, tacto) = await VacaConAsync("tacto");
        var otra = _e.AnimalImportado("200", _e.EstadoSeca);
        var rechazo = await GuardarOkAsync(otra, EventosCampo.Rechazo, "x");

        var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        Assert.Equal(_e.EstadoPrenada, lista.Single(m => m.RegistroId == tacto).EventoId);
        Assert.Equal(EventosCampo.Rechazo, lista.Single(m => m.RegistroId == rechazo).EventoId);
    }

    [Fact]
    public async Task Cambiar_el_nombre_del_evento_no_pierde_los_registros()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await GuardarOkAsync(animal, _ok);

        _e.Base.Ejecutar("UPDATE EstadoReproductivo SET CodigoDairyComp = 'OK2', Nombre = 'Todo bien' WHERE Id = $i", ("$i", _ok));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);
        Assert.Equal("Todo bien", mov.Detalle);
        Assert.Equal(_ok, mov.EventoId);
    }
}
