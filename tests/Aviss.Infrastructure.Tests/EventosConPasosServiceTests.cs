using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 005 (segunda vuelta), RF-22, RF-24, RF-25, RF-29, RF-30: Preñada fecha previa y Aborto con sus pasos se guardan completos o
/// no se guardan; Rechazo deja un solo registro; se describen, se deshacen y se editan sin dejar nada a medias.
/// </summary>
public sealed class EventosConPasosServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    private readonly Guid _vacia, _insemin, _seca, _fresca, _prenada, _previa, _aborto, _ok;
    public void Dispose() => _e.Dispose();

    public EventosConPasosServiceTests()
    {
        _vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA", clave: "VACIA");
        _insemin = _e.Base.Estado("INSEMIN", "INSEMIN", "INSEMIN", clave: "INSEMIN");
        _seca = _e.Base.Estado("SECA2", "SECA estándar", "SECA2", clave: "SECA");
        _fresca = _e.Base.Estado("FRESCA", "FRESCA", "FRESCA", clave: "FRESCA");
        _prenada = _e.Base.Estado("PRENADA2", "Preñada fecha correcta", "PREÑADA2", clave: "PRENADA", principal: true, orden: 4);
        _previa = _e.Base.Estado("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", clave: "PRENADA_FECHA_PREVIA",
            principal: true, orden: 5, pasos: "DiasPreniez", destinoId: _prenada);
        _aborto = _e.Base.Estado("ABORTO", "Aborto", "ABORTO", clave: "ABORTO", principal: true, orden: 6, pasos: "DiasAborto,ResultadoAborto");
        _ok = _e.Base.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK", principal: true, orden: 2);
    }

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> TactoAsync(Guid animal, EleccionEvento e)
        => _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, e.EventoId, null, null, null, e));

    private Task<Resultado<Guid>> RechazoAsync(Guid animal, EleccionEvento e)
        => _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, e.EventoId, null, e));

    private EleccionEvento Aborto(int dias, ResultadoAborto r) => new(_aborto, null, dias, r);

    [Fact]
    public async Task Preniada_fecha_previa_guarda_un_tacto_PRENADA_con_evento_y_dias()
    {
        var animal = _e.AnimalImportado("100", _vacia);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));

        var r = await TactoAsync(animal, new EleccionEvento(_previa, DiasPreniez: 130));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(T(_prenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_prenada), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
        Assert.Equal(T(_previa), _e.Col("Tacto", r.Valor, "EventoId"));
        Assert.Equal("130", _e.Col("Tacto", r.Valor, "DiasPreniezInformados"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Tacto", r.Valor, "FechaHoraUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", $"EntidadId = '{T(r.Valor)}'"));
    }

    [Fact]
    public async Task Preniada_fecha_correcta_sigue_siendo_un_tacto_sin_evento_ni_dias()
    {
        var animal = _e.AnimalImportado("100", _vacia);
        var r = await TactoAsync(animal, new EleccionEvento(_prenada));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Null(_e.Col("Tacto", r.Valor, "EventoId"));
        Assert.Null(_e.Col("Tacto", r.Valor, "DiasPreniezInformados"));
    }

    [Theory]
    [InlineData(ResultadoAborto.VaciaOk, "VACIA")]
    [InlineData(ResultadoAborto.Inseminada, "INSEMIN")]
    [InlineData(ResultadoAborto.Seca, "SECA")]
    [InlineData(ResultadoAborto.Fresca, "FRESCA")]
    public async Task Aborto_con_estado_cambia_el_estado_y_la_fecha_de_ultimo_tacto_incluso_si_ya_estaba_en_ese_estado(ResultadoAborto resultado, string clave)
    {
        var final = clave switch { "VACIA" => _vacia, "INSEMIN" => _insemin, "SECA" => _seca, _ => _fresca };
        // La vaca ya está en el estado final: igual se registra el aborto y se actualiza la fecha de último tacto.
        var animal = _e.AnimalImportado("100", final);
        _e.Reloj.Avanzar(TimeSpan.FromHours(2));

        var r = await TactoAsync(animal, Aborto(45, resultado));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(T(final), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(T(_aborto), _e.Col("Tacto", r.Valor, "EventoId"));
        Assert.Equal("45", _e.Col("Tacto", r.Valor, "DiasAborto"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task Aborto_con_Rechazo_deja_un_unico_registro_y_no_toca_el_estado_ni_el_ultimo_tacto()
    {
        var animal = _e.AnimalImportado("100", _seca);
        await _e.TactoAsync(animal, _prenada);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        var estado = _e.Col("Animal", animal, "EstadoReproductivoId");
        var ultimo = _e.Col("Animal", animal, "FechaUltimoTactoUtc");

        var r = await RechazoAsync(animal, Aborto(0, ResultadoAborto.Rechazo));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(estado, _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(ultimo, _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(1, _e.Cuenta("Tacto"));
        Assert.Equal(1, _e.Cuenta("Rechazo"));
        Assert.Equal(T(_aborto), _e.Col("Rechazo", r.Valor, "EventoId"));
        Assert.Equal("0", _e.Col("Rechazo", r.Valor, "DiasAborto"));
    }

    [Fact]
    public async Task Aborto_con_Rechazo_no_se_puede_guardar_como_tacto_ni_un_resultado_con_estado_como_rechazo()
    {
        var animal = _e.AnimalImportado("100", _seca);
        Assert.False((await TactoAsync(animal, Aborto(10, ResultadoAborto.Rechazo))).EsCorrecto);
        Assert.False((await RechazoAsync(animal, Aborto(10, ResultadoAborto.Seca))).EsCorrecto);
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    public static IEnumerable<object?[]> Invalidas()
    {
        // (evento: 0 = previa, 1 = aborto), días de preñez, días de aborto, resultado
        yield return new object?[] { 0, 0, null, null };                          // previa con 0
        yield return new object?[] { 0, 401, null, null };                        // previa fuera de rango
        yield return new object?[] { 0, null, null, null };                       // previa sin días
        yield return new object?[] { 0, 130, 5, null };                           // sobran días de aborto
        yield return new object?[] { 1, null, -1, ResultadoAborto.VaciaOk };      // aborto con días negativos
        yield return new object?[] { 1, null, 401, ResultadoAborto.VaciaOk };     // aborto fuera de rango
        yield return new object?[] { 1, null, null, ResultadoAborto.VaciaOk };    // aborto sin días
        yield return new object?[] { 1, null, 10, null };                         // aborto sin resultado
        yield return new object?[] { 1, 100, 10, ResultadoAborto.VaciaOk };       // sobran días de preñez
    }

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Datos_que_faltan_sobran_o_estan_fuera_de_rango_se_rechazan_sin_guardar_nada(int cual, int? dp, int? da, ResultadoAborto? r)
    {
        var animal = _e.AnimalImportado("100", _seca);
        var antes = _e.FotoCampo();

        var res = await TactoAsync(animal, new EleccionEvento(cual == 0 ? _previa : _aborto, dp, da, r));

        Assert.False(res.EsCorrecto);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Un_evento_sin_pasos_con_datos_o_un_resultado_desactivado_se_rechazan()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var antes = _e.FotoCampo();

        Assert.False((await TactoAsync(animal, new EleccionEvento(_vacia, DiasPreniez: 10))).EsCorrecto);
        Assert.False((await RechazoAsync(animal, new EleccionEvento(_ok, DiasAborto: 3))).EsCorrecto);

        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Activo = 0 WHERE Id = '{T(_fresca)}'");
        var r = await TactoAsync(animal, Aborto(10, ResultadoAborto.Fresca));
        Assert.Equal(CodigoError.EstadoDeBaja, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task OK_y_el_Rechazo_fijo_siguen_sin_cambiar_el_estado()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var antes = _e.Col("Animal", animal, "EstadoReproductivoId");

        Assert.True((await RechazoAsync(animal, new EleccionEvento(_ok))).EsCorrecto);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.True((await RechazoAsync(animal, new EleccionEvento(EventosCampo.Rechazo))).EsCorrecto);

        Assert.Equal(antes, _e.Col("Animal", animal, "EstadoReproductivoId"));
    }

    [Fact]
    public async Task Si_falla_el_historial_no_queda_nada_a_medias()
    {
        var animal = _e.AnimalImportado("100", _seca);
        _e.ForzarFallaAlGuardarHistorial("Tacto");

        await Assert.ThrowsAnyAsync<Exception>(() => TactoAsync(animal, Aborto(10, ResultadoAborto.VaciaOk)));

        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(T(_seca), _e.Col("Animal", animal, "EstadoReproductivoId"));
    }

    [Fact]
    public async Task Deshacer_un_aborto_restaura_el_estado_anterior_y_el_anulado_conserva_sus_datos()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var r = await TactoAsync(animal, Aborto(45, ResultadoAborto.VaciaOk));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var d = await _e.DeshacerAsync(TipoDeshacer.Tacto, r.Valor);

        Assert.True(d.EsCorrecto, d.Mensaje);
        Assert.Equal(T(_seca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal("1", _e.Col("Tacto", r.Valor, "Anulado"));
        Assert.Equal("45", _e.Col("Tacto", r.Valor, "DiasAborto"));
        Assert.Equal(T(_aborto), _e.Col("Tacto", r.Valor, "EventoId"));
    }

    // RF-22: fecha y hora exactas del reloj en el Aborto con Rechazo, en el registro nuevo de una edición y en el anulado.
    [Fact]
    public async Task Aborto_con_Rechazo_el_registro_nuevo_de_una_edicion_y_el_anulado_conservan_su_fecha_y_hora_exactas()
    {
        var animal = _e.AnimalImportado("100", _seca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(137));   // una hora con segundos, no redonda
        var t1 = BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora);

        var rechazo = await RechazoAsync(animal, Aborto(12, ResultadoAborto.Rechazo));
        Assert.Equal(t1, _e.Col("Rechazo", rechazo.Valor, "FechaHoraUtc"));

        _e.Reloj.Avanzar(TimeSpan.FromSeconds(61));
        var t2 = BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora);
        var edicion = await _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(rechazo.Valor, _aborto, null, Aborto(12, ResultadoAborto.Seca)));
        Assert.True(edicion.EsCorrecto, edicion.Mensaje);
        // El nuevo lleva la hora de la edición; el anulado conserva la suya.
        Assert.Equal(t2, _e.Col("Tacto", edicion.Valor, "FechaHoraUtc"));
        Assert.Equal(t1, _e.Col("Rechazo", rechazo.Valor, "FechaHoraUtc"));

        _e.Reloj.Avanzar(TimeSpan.FromSeconds(29));
        var d = await _e.DeshacerAsync(TipoDeshacer.Tacto, edicion.Valor);
        Assert.True(d.EsCorrecto, d.Mensaje);
        Assert.Equal(t2, _e.Col("Tacto", edicion.Valor, "FechaHoraUtc"));   // el anulado no cambia su fecha
    }

    [Fact]
    public async Task Un_estado_con_pasos_o_destino_sin_eleccion_se_rechaza_con_error_controlado()
    {
        var animal = _e.AnimalImportado("100", _seca);

        var aborto = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _aborto, null, null, null));
        var previa = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _previa, null, null, null));

        Assert.Equal(CodigoError.DatosInvalidos, aborto.Codigo);
        Assert.Equal(CodigoError.DatosInvalidos, previa.Codigo);
        Assert.Equal(0, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task Deshacer_un_Aborto_con_Rechazo_conserva_los_dias_en_el_anulado()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var r = await RechazoAsync(animal, Aborto(12, ResultadoAborto.Rechazo));

        var d = await _e.DeshacerAsync(TipoDeshacer.Rechazo, r.Valor);

        Assert.True(d.EsCorrecto, d.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", r.Valor, "Anulado"));
        Assert.Equal("12", _e.Col("Rechazo", r.Valor, "DiasAborto"));
    }

    [Fact]
    public async Task Editar_va_y_vuelve_entre_tacto_con_pasos_Aborto_con_Rechazo_y_estado_comun()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var previa = await TactoAsync(animal, new EleccionEvento(_previa, DiasPreniez: 130));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        // fecha previa -> Aborto con Rechazo (el tacto se anula y queda un rechazo)
        var aRechazo = await _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(previa.Valor, _aborto, null, Aborto(7, ResultadoAborto.Rechazo)));
        Assert.True(aRechazo.EsCorrecto, aRechazo.Mensaje);
        Assert.Equal(T(_seca), _e.Col("Animal", animal, "EstadoReproductivoId")); // la anulación restauró el estado
        Assert.Equal("7", _e.Col("Rechazo", aRechazo.Valor, "DiasAborto"));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        // Aborto con Rechazo -> Aborto con estado Inseminada (rechazo anulado, tacto nuevo)
        var aTacto = await _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(aRechazo.Valor, _aborto, null, Aborto(7, ResultadoAborto.Inseminada)));
        Assert.True(aTacto.EsCorrecto, aTacto.Mensaje);
        Assert.Equal(T(_insemin), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal("1", _e.Col("Rechazo", aRechazo.Valor, "Anulado"));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        // y a un estado común, sin elección explícita
        var comun = await _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(aTacto.Valor, _vacia, null));
        Assert.True(comun.EsCorrecto, comun.Mensaje);
        Assert.Equal(T(_vacia), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Tacto", comun.Valor, "EventoId"));
    }

    [Fact]
    public async Task Editar_con_datos_invalidos_revierte_la_anulacion()
    {
        var animal = _e.AnimalImportado("100", _seca);
        var previa = await TactoAsync(animal, new EleccionEvento(_previa, DiasPreniez: 130));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var antes = _e.FotoCampo();

        var r = await _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(previa.Valor, _aborto, null, Aborto(999, ResultadoAborto.VaciaOk)));

        Assert.False(r.EsCorrecto);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Movimientos_historial_y_aviso_de_repetido_dicen_la_descripcion_completa()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _seca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var tacto = await TactoAsync(animal, Aborto(45, ResultadoAborto.VaciaOk));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == tacto.Valor);
        Assert.Equal("Aborto, 45 días, Vacía OK", mov.Detalle);
        Assert.Equal(_aborto, mov.EventoId);
        Assert.Equal(45, mov.DiasAborto);
        Assert.Equal(ResultadoAborto.VaciaOk, mov.ResultadoAborto);

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;
        Assert.Contains(ficha.Historial, h => h.Titulo == "Tacto: Aborto, 45 días, Vacía OK");

        var repetido = await _e.LeerAsync("100");
        Assert.Equal("Aborto, 45 días, Vacía OK", repetido.UltimoRegistro!.EstadoNombre);
    }

    [Fact]
    public async Task Fecha_previa_y_Aborto_con_Rechazo_se_describen_y_un_Aborto_viejo_queda_como_Aborto()
    {
        var a1 = _e.AnimalImportado("100", _seca);
        var previa = await TactoAsync(a1, new EleccionEvento(_previa, DiasPreniez: 130));
        var a2 = _e.AnimalImportado("200", _seca);
        var rech = await RechazoAsync(a2, Aborto(3, ResultadoAborto.Rechazo));
        // Un Aborto viejo: registro de rechazo sin días (como lo dejó la versión anterior).
        var a3 = _e.AnimalImportado("300", _seca);
        var f = BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora);
        // La base de hoy ya no acepta un Aborto sin días: se arma como estaba, cuando Aborto era un solo evento.
        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Tipo = 'SoloEvento', Pasos = NULL WHERE Id = '{T(_aborto)}'");
        _e.Base.Ejecutar($"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EventoId, Anulado, CreadoEnUtc, ActualizadoEnUtc) VALUES ('{T(Guid.NewGuid())}', '{T(a3)}', '{f}', 99, '{T(_aborto)}', 0, '{f}', '{f}')");
        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Tipo = 'Estado', Pasos = 'DiasAborto,ResultadoAborto' WHERE Id = '{T(_aborto)}'");

        var movs = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);
        Assert.Equal("Preñada fecha previa, 130 días", movs.Single(m => m.RegistroId == previa.Valor).Detalle);
        Assert.Equal("Aborto, 3 días, Rechazo", movs.Single(m => m.RegistroId == rech.Valor).Detalle);
        Assert.Contains(movs, m => m.AnimalId == a3 && m.Detalle == "Aborto" && m.DiasAborto is null && m.ResultadoAborto is null);
    }

    [Fact]
    public async Task La_lista_de_eventos_trae_pasos_y_resultados_de_aborto_activos()
    {
        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Activo = 0 WHERE Id = '{T(_seca)}'");
        var lista = await _e.Campo.ListarEventosAsync();

        Assert.Equal(new[] { ResultadoAborto.VaciaOk, ResultadoAborto.Inseminada, ResultadoAborto.Fresca, ResultadoAborto.Rechazo }, lista.ResultadosAborto);
        Assert.Equal(new[] { PasoEvento.DiasAborto, PasoEvento.ResultadoAborto }, lista.Principales.Single(e => e.Id == _aborto).Pasos);
        Assert.Equal(_prenada, lista.Principales.Single(e => e.Id == _previa).EstadoDestinoId);
    }
}
