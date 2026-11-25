using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 011 (RF-9 a RF-16): dos eventos en el mismo tacto. El Evento 2 viaja en el mismo registro (un Tacto o un registro de "solo
/// evento"), nunca cambia el estado ni el último tacto, deja una línea más de historial y se guarda todo junto o nada. Corregir lo
/// reenvía y Deshacer lo anula entero.
/// </summary>
public sealed class TrabajoCampoDosEventosTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    private readonly Guid _ok, _revisar, _prostag, _dib, _vacia, _insemin, _aborto, _previa, _prenadaEstandar;
    public void Dispose() => _e.Dispose();

    public TrabajoCampoDosEventosTests()
    {
        _ok = _e.Base.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK", principal: true, orden: 2);
        _revisar = _e.Base.Estado("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", tipo: "SoloEvento", clave: "REVISAR_NUEVAMENTE", principal: true, orden: 1);
        _prostag = _e.Base.Estado("PROSTAG", "PROSTAG", "PROSTAG");            // creado a mano: cambia el estado como Evento 1
        _dib = _e.Base.Estado("DIB", "DIB", "DIB");
        _vacia = _e.Base.Estado("VACIA", "VACIA", "VACIA", clave: "VACIA", principal: true, orden: 3);
        _insemin = _e.Base.Estado("INSEMIN", "INSEMIN", "INSEMIN", clave: "INSEMIN");
        _prenadaEstandar = _e.Base.Estado("PRENADA_OK", "Preñada fecha correcta", "PREÑADA OK", clave: "PRENADA", principal: true, orden: 4);
        _previa = _e.Base.Estado("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", clave: "PRENADA_FECHA_PREVIA",
            principal: true, orden: 5, pasos: "DiasPreniez", destinoId: _prenadaEstandar);
        _aborto = _e.Base.Estado("ABORTO", "Aborto", "ABORTO", clave: "ABORTO", principal: true, orden: 6, pasos: "DiasAborto,ResultadoAborto");
    }

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private static EleccionEvento Con(Guid evento1, Guid evento2, string? obs2 = null)
        => new(evento1, Segundo: new SegundoEventoElegido(evento2, obs2));

    private Task<Resultado<Guid>> TactoAsync(Guid animal, EleccionEvento e, string? obs = null)
        => _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, e.EventoId, null, obs, null, e));

    private Task<Resultado<Guid>> SoloEventoAsync(Guid animal, EleccionEvento e, string? obs = null)
        => _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, e.EventoId, obs, e));

    private async Task<Guid> OkAsync(Task<Resultado<Guid>> tarea)
    {
        var r = await tarea;
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    // ---------- Qué se guarda ----------

    [Fact]
    public async Task OK_mas_PROSTAG_guarda_un_solo_evento_con_el_Evento_2_y_no_cambia_ni_el_estado_ni_el_ultimo_tacto()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var estado = _e.Col("Animal", animal, "EstadoReproductivoId");
        var ultimoTacto = _e.Col("Animal", animal, "FechaUltimoTactoUtc");
        var tactos = _e.Cuenta("Tacto");

        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag, " control "), "  bien "));

        // El orden importa (riesgo aceptado): con OK primero, PROSTAG solo queda registrado.
        Assert.Equal(estado, _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(ultimoTacto, _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(tactos, _e.Cuenta("Tacto"));
        Assert.Equal(T(_ok), _e.Col("Rechazo", id, "EventoId"));
        Assert.Equal(T(_prostag), _e.Col("Rechazo", id, "Evento2Id"));
        Assert.Equal("bien", _e.Col("Rechazo", id, "Observaciones"));
        Assert.Equal("control", _e.Col("Rechazo", id, "Observaciones2"));
        Assert.Equal(1, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task PROSTAG_mas_OK_guarda_un_tacto_que_si_cambia_el_estado_y_el_ultimo_tacto()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var id = await OkAsync(TactoAsync(animal, Con(_prostag, _ok)));

        // Los eventos creados a mano como Evento 1 siguen cambiando el estado (riesgo aceptado, spec 011).
        Assert.Equal(T(_prostag), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(T(_prostag), _e.Col("Tacto", id, "EstadoNuevoId"));
        Assert.Equal(T(_ok), _e.Col("Tacto", id, "Evento2Id"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Un_estandar_de_estado_como_Evento_1_cambia_el_estado_y_el_Evento_2_solo_queda_registrado()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);

        var id = await OkAsync(TactoAsync(animal, Con(_vacia, _revisar, "ver"), "sola"));

        Assert.Equal(T(_vacia), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_vacia), _e.Col("Tacto", id, "EstadoNuevoId"));
        Assert.Null(_e.Col("Tacto", id, "EventoId"));
        Assert.Equal(T(_revisar), _e.Col("Tacto", id, "Evento2Id"));
        Assert.Equal("sola", _e.Col("Tacto", id, "Observaciones"));
        Assert.Equal("ver", _e.Col("Tacto", id, "Observaciones2"));
    }

    [Fact]
    public async Task Un_registro_de_un_solo_evento_queda_como_siempre()
    {
        var a1 = _e.AnimalImportado("100", _e.EstadoSeca);
        var a2 = _e.AnimalImportado("200", _e.EstadoSeca);

        var tacto = await OkAsync(TactoAsync(a1, new EleccionEvento(_vacia)));
        var solo = await OkAsync(SoloEventoAsync(a2, new EleccionEvento(_ok)));

        Assert.Null(_e.Col("Tacto", tacto, "Evento2Id"));
        Assert.Null(_e.Col("Tacto", tacto, "Observaciones2"));
        Assert.Null(_e.Col("Rechazo", solo, "Evento2Id"));
        Assert.Equal(1, _e.Cuenta("Historial", $"EntidadId = '{T(tacto)}'"));
        Assert.Equal(1, _e.Cuenta("Historial", $"EntidadId = '{T(solo)}'"));
    }

    [Fact]
    public async Task Es_posible_guardar_solo_la_observacion_del_Evento_2()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);

        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag, "solo esta")));

        Assert.Null(_e.Col("Rechazo", id, "Observaciones"));
        Assert.Equal("solo esta", _e.Col("Rechazo", id, "Observaciones2"));
    }

    [Fact]
    public async Task Un_Evento_2_creado_a_mano_o_Revisar_Nuevamente_se_acepta_con_Evento_1_que_cambia_estado_o_no()
    {
        foreach (var (evento1, evento2) in new[] { (_ok, _revisar), (_revisar, _dib), (_prenadaEstandar, _prostag), (_insemin, _ok), (_dib, _prostag) })
        {
            var animal = _e.AnimalImportado(Guid.NewGuid().ToString("N")[..8], _e.EstadoSeca);
            var esSolo = evento1 == _ok || evento1 == _revisar;
            var r = esSolo ? await SoloEventoAsync(animal, Con(evento1, evento2)) : await TactoAsync(animal, Con(evento1, evento2));
            Assert.True(r.EsCorrecto, r.Mensaje);
        }
    }

    // ---------- Historial ----------

    [Fact]
    public async Task El_tacto_con_dos_eventos_deja_dos_lineas_de_historial_y_la_anulacion_ninguna_de_Evento_2()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await OkAsync(TactoAsync(animal, Con(_vacia, _ok)));

        Assert.Equal(2, _e.Cuenta("Historial", $"EntidadId = '{T(id)}' AND Accion = 'Tacto'"));
        Assert.Equal("OK", _e.Base.Escalar($"SELECT ValorNuevo FROM Historial WHERE EntidadId = '{T(id)}' AND Campo = 'Evento2'")?.ToString());
        Assert.Equal(1, _e.Cuenta("Historial", $"EntidadId = '{T(id)}' AND Campo = 'Estado'"));

        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var deshacer = await _e.DeshacerAsync(TipoDeshacer.Tacto, id);
        Assert.True(deshacer.EsCorrecto, deshacer.Mensaje);
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionTacto'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Campo = 'Evento2'"));
    }

    [Fact]
    public async Task El_solo_evento_con_dos_eventos_deja_dos_lineas_con_la_misma_accion_y_el_nombre_del_Evento_2()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);

        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag)));

        Assert.Equal(2, _e.Cuenta("Historial", $"EntidadId = '{T(id)}' AND Accion = 'RechazoCreado'"));
        Assert.Equal("OK", _e.Base.Escalar($"SELECT ValorNuevo FROM Historial WHERE EntidadId = '{T(id)}' AND Campo = 'Evento'")?.ToString());
        Assert.Equal("PROSTAG", _e.Base.Escalar($"SELECT ValorNuevo FROM Historial WHERE EntidadId = '{T(id)}' AND Campo = 'Evento2'")?.ToString());
    }

    [Fact]
    public async Task El_historial_conserva_el_nombre_aunque_despues_se_renombre_o_se_de_de_baja_el_Evento_2()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag)));

        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Nombre = 'OTRO NOMBRE', Activo = 0 WHERE Id = '{T(_prostag)}'");

        Assert.Equal("PROSTAG", _e.Base.Escalar($"SELECT ValorNuevo FROM Historial WHERE EntidadId = '{T(id)}' AND Campo = 'Evento2'")?.ToString());
    }

    // ---------- Rechazos: sin guardar nada ----------

    private Task<Resultado<Guid>> Guardar(bool solo, Guid animal, EleccionEvento e, string? obs = null)
        => solo ? SoloEventoAsync(animal, e, obs) : TactoAsync(animal, e, obs);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Los_rechazos_del_Evento_2_no_guardan_nada(bool solo)
    {
        var evento1 = solo ? _ok : _vacia;
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var inactivo = _e.Base.Estado("VIEJO", "Viejo", "VIEJO", activo: false);
        var antes = _e.FotoCampo();

        Assert.Equal(CodigoError.NoEncontrado, (await Guardar(solo, animal, Con(evento1, Guid.NewGuid()))).Codigo);            // inexistente
        Assert.Equal(CodigoError.EstadoDeBaja, (await Guardar(solo, animal, Con(evento1, inactivo))).Codigo);                  // de baja
        Assert.Equal(CodigoError.DatosInvalidos, (await Guardar(solo, animal, Con(evento1, _insemin))).Codigo);                // estándar de estado
        Assert.Equal(CodigoError.DatosInvalidos, (await Guardar(solo, animal, Con(evento1, _previa))).Codigo);                 // con pasos y destino
        Assert.Equal(CodigoError.DatosInvalidos, (await Guardar(solo, animal, Con(evento1, _aborto))).Codigo);                 // con pasos
        Assert.Equal(CodigoError.DatosInvalidos, (await Guardar(solo, animal, Con(evento1, evento1))).Codigo);                 // el mismo
        Assert.Equal(CodigoError.NoEncontrado, (await Guardar(solo, animal, Con(evento1, EventosCampo.Rechazo))).Codigo);      // RECHAZO no es del catálogo
        Assert.Equal(CodigoError.DatosInvalidos, (await Guardar(solo, animal, Con(evento1, _prostag, new string('x', 301)))).Codigo); // observación 2 larga

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Rechazo"));
        Assert.Equal(0, _e.Cuenta("Historial", "Campo = 'Evento2'"));
    }

    [Fact]
    public async Task Un_Evento_1_que_no_admite_segundo_rechaza_el_Evento_2_sin_guardar_nada()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var antes = _e.FotoCampo();

        // Preñada fecha previa (pasos y destino) y Aborto (pasos), como tacto.
        var previa = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _previa, null, null, null,
            new EleccionEvento(_previa, DiasPreniez: 130, Segundo: new SegundoEventoElegido(_ok, null))));
        var aborto = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _aborto, null, null, null,
            new EleccionEvento(_aborto, DiasAborto: 10, ResultadoAborto: Aviss.Domain.Enums.ResultadoAborto.VaciaOk, Segundo: new SegundoEventoElegido(_ok, null))));
        // Aborto con resultado Rechazo (va a la tabla de solo evento) y el Rechazo fijo.
        var abortoRechazo = await _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, _aborto, null,
            new EleccionEvento(_aborto, DiasAborto: 10, ResultadoAborto: Aviss.Domain.Enums.ResultadoAborto.Rechazo, Segundo: new SegundoEventoElegido(_ok, null))));
        var rechazoFijo = await SoloEventoAsync(animal, Con(EventosCampo.Rechazo, _ok));

        Assert.All(new[] { previa, aborto, abortoRechazo, rechazoFijo }, r =>
        {
            Assert.False(r.EsCorrecto);
            Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        });
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task El_tope_de_300_caracteres_rige_tambien_para_la_observacion_del_Evento_1_de_un_tacto_y_de_un_solo_evento()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var antes = _e.FotoCampo();
        var larga = new string('x', 301);

        Assert.Equal(CodigoError.DatosInvalidos, (await TactoAsync(animal, new EleccionEvento(_vacia), larga)).Codigo);    // sin Evento 2
        Assert.Equal(CodigoError.DatosInvalidos, (await TactoAsync(animal, Con(_vacia, _ok), larga)).Codigo);
        Assert.Equal(CodigoError.DatosInvalidos, (await SoloEventoAsync(animal, new EleccionEvento(_ok), larga)).Codigo);
        Assert.Equal(CodigoError.DatosInvalidos, (await SoloEventoAsync(animal, Con(_ok, _prostag), larga)).Codigo);
        Assert.Equal(antes, _e.FotoCampo());

        // 300 justos entran.
        Assert.True((await TactoAsync(animal, Con(_vacia, _ok, new string('y', 300)), new string('x', 300))).EsCorrecto);
    }

    [Fact]
    public async Task Si_falla_el_historial_del_Evento_2_no_queda_nada_a_medias()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var antes = _e.FotoCampo();
        _e.Base.Ejecutar("CREATE TRIGGER TR_Prueba_Falla_E2 BEFORE INSERT ON Historial WHEN NEW.Campo = 'Evento2' BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");

        await Assert.ThrowsAnyAsync<Exception>(() => TactoAsync(animal, Con(_vacia, _ok)));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'Tacto'"));
    }

    // ---------- Espera ----------

    [Fact]
    public async Task Entrar_con_dos_eventos_resuelve_la_espera_y_un_rechazo_la_deja_pendiente()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = (await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(animal))).Valor;

        var rechazado = await SoloEventoAsync(animal, Con(_ok, _insemin));
        Assert.False(rechazado.EsCorrecto);
        Assert.Equal("Pendiente", _e.Col("Espera", espera, "Estado"));

        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag)));
        Assert.NotEqual("Pendiente", _e.Col("Espera", espera, "Estado"));
        Assert.Equal(T(id), _e.Col("Espera", espera, "RegistroResolucionId"));
    }

    // ---------- Deshacer ----------

    [Fact]
    public async Task Deshacer_un_tacto_con_dos_eventos_lo_anula_entero_y_conserva_los_dos_y_su_fecha()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await OkAsync(TactoAsync(animal, Con(_vacia, _ok, "dos")));
        var fecha = _e.Col("Tacto", id, "FechaHoraUtc");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var r = await _e.DeshacerAsync(TipoDeshacer.Tacto, id);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", id, "Anulado"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_ok), _e.Col("Tacto", id, "Evento2Id"));
        Assert.Equal("dos", _e.Col("Tacto", id, "Observaciones2"));
        Assert.Equal(fecha, _e.Col("Tacto", id, "FechaHoraUtc"));
        // La anulación no copia el Evento 2.
        Assert.Equal(0, _e.Cuenta("Tacto", $"Tipo = 'Anulacion' AND Evento2Id IS NOT NULL"));
    }

    [Fact]
    public async Task Deshacer_un_solo_evento_con_dos_eventos_lo_anula_entero_y_conserva_los_dos()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag, "dos")));
        var fecha = _e.Col("Rechazo", id, "FechaHoraUtc");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var r = await _e.DeshacerAsync(TipoDeshacer.Rechazo, id);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", id, "Anulado"));
        Assert.Equal(T(_prostag), _e.Col("Rechazo", id, "Evento2Id"));
        Assert.Equal("dos", _e.Col("Rechazo", id, "Observaciones2"));
        Assert.Equal(fecha, _e.Col("Rechazo", id, "FechaHoraUtc"));
    }

    // ---------- Corregir (Editar) ----------

    private Task<Resultado<Guid>> CorregirAsync(Guid registro, EleccionEvento e, string? obs = null)
        => _e.Campo.CorregirRegistroAsync(new CorregirRegistroCommand(registro, e.EventoId, obs, e));

    private async Task<(Guid Animal, Guid Registro)> VacaConTactoAsync(EleccionEvento e, bool solo)
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var id = await OkAsync(solo ? SoloEventoAsync(animal, e) : TactoAsync(animal, e));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        return (animal, id);
    }

    [Fact]
    public async Task Corregir_puede_sumarle_un_Evento_2_a_un_registro_de_un_solo_evento()
    {
        var (_, viejo) = await VacaConTactoAsync(new EleccionEvento(_ok), solo: true);

        var r = await CorregirAsync(viejo, Con(_ok, _prostag, "agregado"));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", viejo, "Anulado"));
        Assert.Equal(T(_prostag), _e.Col("Rechazo", r.Valor, "Evento2Id"));
        Assert.Equal("agregado", _e.Col("Rechazo", r.Valor, "Observaciones2"));
    }

    [Fact]
    public async Task Corregir_puede_cambiar_y_quitar_el_Evento_2()
    {
        var (_, original) = await VacaConTactoAsync(Con(_ok, _prostag, "uno"), solo: true);

        var cambiado = await CorregirAsync(original, Con(_ok, _dib, "otro"));
        Assert.True(cambiado.EsCorrecto, cambiado.Mensaje);
        Assert.Equal(T(_dib), _e.Col("Rechazo", cambiado.Valor, "Evento2Id"));
        Assert.Equal("otro", _e.Col("Rechazo", cambiado.Valor, "Observaciones2"));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));

        var quitado = await CorregirAsync(cambiado.Valor, new EleccionEvento(_ok));
        Assert.True(quitado.EsCorrecto, quitado.Mensaje);
        Assert.Null(_e.Col("Rechazo", quitado.Valor, "Evento2Id"));
        Assert.Null(_e.Col("Rechazo", quitado.Valor, "Observaciones2"));
        // Los anulados conservan lo que tenían.
        Assert.Equal(T(_prostag), _e.Col("Rechazo", original, "Evento2Id"));
        Assert.Equal(T(_dib), _e.Col("Rechazo", cambiado.Valor, "Evento2Id"));
    }

    [Fact]
    public async Task Corregir_de_solo_evento_a_tacto_y_al_reves_lleva_el_Evento_2_a_la_otra_tabla()
    {
        var (animal, solo) = await VacaConTactoAsync(Con(_ok, _prostag, "n"), solo: true);

        var aTacto = await CorregirAsync(solo, Con(_vacia, _prostag, "n"));

        Assert.True(aTacto.EsCorrecto, aTacto.Mensaje);
        Assert.Equal("1", _e.Col("Rechazo", solo, "Anulado"));
        Assert.Equal(T(_vacia), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_prostag), _e.Col("Tacto", aTacto.Valor, "Evento2Id"));
        Assert.Equal("n", _e.Col("Tacto", aTacto.Valor, "Observaciones2"));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));

        var aSolo = await CorregirAsync(aTacto.Valor, Con(_revisar, _prostag, "n"));

        Assert.True(aSolo.EsCorrecto, aSolo.Mensaje);
        Assert.Equal("1", _e.Col("Tacto", aTacto.Valor, "Anulado"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(T(_prostag), _e.Col("Rechazo", aSolo.Valor, "Evento2Id"));
        Assert.Equal("n", _e.Col("Rechazo", aSolo.Valor, "Observaciones2"));
    }

    [Fact]
    public async Task Si_el_registro_nuevo_se_rechaza_el_original_sigue_vigente()
    {
        var (animal, original) = await VacaConTactoAsync(Con(_vacia, _ok), solo: false);
        var antes = _e.FotoCampo();

        var r = await CorregirAsync(original, Con(_vacia, _insemin)); // el Evento 2 no es combinable

        Assert.False(r.EsCorrecto);
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal("0", _e.Col("Tacto", original, "Anulado"));
        Assert.Equal(T(_vacia), _e.Col("Animal", animal, "EstadoReproductivoId"));
    }

    // ---------- Ver los dos eventos ----------

    [Fact]
    public async Task Ficha_movimientos_y_aviso_de_repetido_dicen_los_dos_eventos_unidos_por_mas()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag, "uno")));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);
        Assert.Equal("OK + PROSTAG", mov.Detalle);
        Assert.Equal(_ok, mov.EventoId);
        Assert.Equal(_prostag, mov.Evento2Id);
        Assert.Equal("uno", mov.Observaciones2);

        var repetido = await _e.LeerAsync("100");
        Assert.Equal("OK + PROSTAG", repetido.UltimoRegistro!.EstadoNombre);
        Assert.True(repetido.PuedeCorregir);
    }

    [Fact]
    public async Task El_tacto_con_dos_eventos_se_ve_con_los_dos_en_movimientos_y_en_la_ficha()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var id = await OkAsync(TactoAsync(animal, Con(_vacia, _ok)));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);
        Assert.Equal("VACIA + OK", mov.Detalle);
        Assert.Equal(_ok, mov.Evento2Id);

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;
        Assert.Contains(ficha.Historial, h => h.Titulo == "Tacto: VACIA + OK");
    }

    [Fact]
    public async Task Un_Evento_2_dado_de_baja_despues_sigue_mostrando_su_nombre()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await OkAsync(SoloEventoAsync(animal, Con(_ok, _prostag)));
        _e.Base.Ejecutar($"UPDATE EstadoReproductivo SET Activo = 0 WHERE Id = '{T(_prostag)}'");

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);

        Assert.Equal("OK + PROSTAG", mov.Detalle);
    }

    [Fact]
    public async Task Un_registro_de_un_evento_se_ve_exactamente_como_siempre()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var id = await OkAsync(SoloEventoAsync(animal, new EleccionEvento(_ok)));

        var mov = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.RegistroId == id);

        Assert.Equal("OK", mov.Detalle);
        Assert.Null(mov.Evento2Id);
        Assert.Null(mov.Observaciones2);
    }

    // ---------- La lista de eventos del servicio trae las banderas ----------

    [Fact]
    public async Task La_lista_de_eventos_del_servicio_trae_las_banderas_de_segundo_evento()
    {
        var lista = await _e.Campo.ListarEventosAsync();
        var todos = lista.Principales.Concat(lista.Otros).ToDictionary(x => x.Id);

        Assert.True(todos[_ok].AdmiteSegundo && todos[_ok].PuedeSerSegundo);
        Assert.True(todos[_prostag].AdmiteSegundo && todos[_prostag].PuedeSerSegundo);
        Assert.True(todos[_vacia].AdmiteSegundo);
        Assert.False(todos[_vacia].PuedeSerSegundo);
        Assert.False(todos[_aborto].AdmiteSegundo || todos[_aborto].PuedeSerSegundo);
        Assert.False(todos[_previa].AdmiteSegundo || todos[_previa].PuedeSerSegundo);
        Assert.False(todos[EventosCampo.Rechazo].AdmiteSegundo || todos[EventosCampo.Rechazo].PuedeSerSegundo);
    }
}
