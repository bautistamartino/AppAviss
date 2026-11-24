using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 004, En Espera (RF-1 a RF-5, RF-9, RF-12, RF-14): poner en espera, listar, resolver con un tacto o un rechazo en la misma
/// transacción, saltear, y que nada más cierre una espera. Todo entra por la fachada <see cref="ICampoService"/>, como en la app.
/// </summary>
public sealed class EsperaServiceTests : IDisposable
{
    private const string CvetCero = """{"CVET":"0"}""";
    private const string CvetUno = """{"CVET":"1"}""";

    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> PonerAsync(Guid animal) => _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(animal));

    private async Task<Guid> PonerOkAsync(Guid animal)
    {
        var r = await PonerAsync(animal);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private string Estado(Guid esperaId) => _e.Col("Espera", esperaId, "Estado")!;

    // ------------------------------------------------------------------ RF-1: poner en espera

    [Fact]
    public async Task Poner_en_espera_guarda_la_espera_pendiente_con_su_historial_y_ningun_registro_de_campo()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca, json: CvetUno);

        var id = await PonerOkAsync(animal);

        Assert.Equal("Pendiente", Estado(id));
        Assert.Equal(T(animal), _e.Col("Espera", id, "AnimalId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Espera", id, "FechaHoraUtc"));
        Assert.Null(_e.Col("Espera", id, "ResueltaEnUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaCreada'"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Una_vaca_con_CVET_0_no_se_puede_poner_en_espera_y_no_se_guarda_nada()
    {
        var animal = _e.AnimalImportado("100", json: CvetCero);

        var r = await PonerAsync(animal);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.VacaNoEntraEnTacto, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Espera"));
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'EsperaCreada'"));
    }

    [Fact]
    public async Task Una_vaca_sin_CVET_en_el_Excel_si_se_puede_poner_en_espera()
    {
        var animal = _e.AnimalImportado("100");
        Assert.True((await PonerAsync(animal)).EsCorrecto);
    }

    [Fact]
    public async Task Poner_en_espera_dos_veces_a_la_misma_vaca_rechaza_la_segunda_y_deja_una_sola()
    {
        var animal = _e.AnimalImportado("100");
        await PonerOkAsync(animal);

        var segunda = await PonerAsync(animal);

        Assert.False(segunda.EsCorrecto);
        Assert.Equal(CodigoError.VacaYaEnEspera, segunda.Codigo);
        Assert.Equal(1, _e.Cuenta("Espera"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaCreada'"));
    }

    [Fact]
    public async Task Una_vaca_se_puede_poner_en_espera_otra_vez_despues_de_resolverla_o_saltearla()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        await PonerOkAsync(animal);
        await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(animal));

        var otra = await PonerAsync(animal);

        Assert.True(otra.EsCorrecto, otra.Mensaje);
        Assert.Equal(2, _e.Cuenta("Espera"));
        Assert.Equal(1, _e.Cuenta("Espera", "Estado = 'Pendiente'"));
    }

    [Fact]
    public async Task Poner_en_espera_a_un_animal_que_no_existe_se_rechaza()
    {
        var inexistente = await PonerAsync(Guid.NewGuid());
        Assert.Equal(CodigoError.NoEncontrado, inexistente.Codigo);

        var animal = _e.AnimalImportado("100");
        Assert.Equal(0, _e.Cuenta("Espera"));
    }

    // ------------------------------------------------------------------ RF-2: listar

    [Fact]
    public async Task Listar_devuelve_las_esperas_pendientes_del_rodeo_de_la_mas_antigua_a_la_mas_reciente_con_caravana_y_CVET()
    {
        var a = _e.AnimalImportado("7712", json: CvetUno);
        var b = _e.AnimalImportado("8000", json: """{"CVET":"PRE"}""");
        var c = _e.AnimalImportado("9000");
        await PonerOkAsync(a);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await PonerOkAsync(b);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await PonerOkAsync(c);
        // una resuelta no aparece
        await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(b));

        var lista = await _e.Campo.ListarEsperasAsync(_e.RodeoId);

        Assert.Equal(["7712", "9000"], lista.Select(x => x.Caravana));
        Assert.Equal("1", lista[0].Cvet);
        Assert.Equal("", lista[1].Cvet);
        Assert.True(lista[0].FechaHoraUtc < lista[1].FechaHoraUtc);
    }

    [Fact]
    public async Task Listar_no_trae_las_esperas_de_otro_rodeo()
    {
        var otroRodeo = _e.Base.Rodeo(_e.EstablecimientoId, hash: new string('b', 64));
        var propia = _e.AnimalImportado("1");
        var ajena = _e.AnimalImportado("2", rodeoId: otroRodeo);
        await PonerOkAsync(propia);
        await PonerOkAsync(ajena);

        Assert.Equal(["1"], (await _e.Campo.ListarEsperasAsync(_e.RodeoId)).Select(x => x.Caravana));
        Assert.Equal(["2"], (await _e.Campo.ListarEsperasAsync(otroRodeo)).Select(x => x.Caravana));
    }

    // ------------------------------------------------------------------ RF-3: resolver con un tacto o un rechazo

    [Fact]
    public async Task Entrar_con_un_tacto_deja_la_espera_resuelta_en_la_misma_operacion()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));

        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        Assert.Equal("Resuelta", Estado(espera));
        Assert.Equal(T(tacto), _e.Col("Espera", espera, "RegistroResolucionId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Espera", espera, "ResueltaEnUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaResuelta'"));
        Assert.Empty(await _e.Campo.ListarEsperasAsync(_e.RodeoId));
    }

    [Fact]
    public async Task Entrar_con_un_rechazo_deja_la_espera_resuelta_con_el_id_del_rechazo()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);

        var rechazo = await _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, EventosCampo.Rechazo, "pata"));

        Assert.True(rechazo.EsCorrecto, rechazo.Mensaje);
        Assert.Equal("Resuelta", Estado(espera));
        Assert.Equal(T(rechazo.Valor), _e.Col("Espera", espera, "RegistroResolucionId"));
        Assert.Equal(1, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Si_el_tacto_se_rechaza_la_espera_sigue_pendiente_y_no_se_guarda_nada()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        // Un registro "del futuro" respecto del reloj: el tacto se rechaza por reloj atrasado (spec 003 RF-21).
        var manga = _e.Base.Manga(_e.RodeoId, 9);
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddHours(2));

        var r = await _e.Campo.RegistrarTactoAsync(new RegistrarTactoCommand(animal, _e.EstadoPrenada, null, null));

        Assert.False(r.EsCorrecto);
        Assert.Equal("Pendiente", Estado(espera));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("Historial", "Accion = 'EsperaResuelta'"));
    }

    [Fact]
    public async Task Si_el_rechazo_se_rechaza_la_espera_sigue_pendiente()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        var manga = _e.Base.Manga(_e.RodeoId, 9);
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddHours(2));

        var r = await _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(animal, EventosCampo.Rechazo, null));

        Assert.False(r.EsCorrecto);
        Assert.Equal("Pendiente", Estado(espera));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Si_algo_falla_a_mitad_de_la_operacion_ni_el_tacto_ni_la_resolucion_quedan_guardados()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        // El historial de la resolución falla: tacto + animal + espera tienen que revertirse juntos (una sola transacción).
        _e.ForzarFallaAlGuardarHistorial("EsperaResuelta");

        await Assert.ThrowsAnyAsync<Exception>(() => _e.Campo.RegistrarTactoAsync(
            new RegistrarTactoCommand(animal, _e.EstadoPrenada, null, null)));

        Assert.Equal("Pendiente", Estado(espera));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
    }

    [Fact]
    public async Task Registrar_un_tacto_de_una_vaca_sin_espera_no_toca_las_esperas_de_otras()
    {
        var conEspera = _e.AnimalImportado("1", _e.EstadoSeca);
        var sinEspera = _e.AnimalImportado("2", _e.EstadoSeca);
        var espera = await PonerOkAsync(conEspera);

        await _e.TactoAsync(sinEspera, _e.EstadoPrenada);

        Assert.Equal("Pendiente", Estado(espera));
    }

    // ------------------------------------------------------------------ RF-3: saltear

    [Fact]
    public async Task Saltear_deja_la_espera_salteada_sin_guardar_ningun_registro_de_campo()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var r = await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(animal));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("Salteada", Estado(espera));
        Assert.Null(_e.Col("Espera", espera, "RegistroResolucionId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Espera", espera, "ResueltaEnUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaSalteada'"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Rechazo"));
    }

    [Fact]
    public async Task Saltear_una_vaca_sin_espera_pendiente_se_rechaza()
    {
        var animal = _e.AnimalImportado("100");

        var r = await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(animal));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.EsperaNoPendiente, r.Codigo);
    }

    // ------------------------------------------------------------------ RF-4: nada más cierra una espera

    [Fact]
    public async Task Ni_el_tiempo_ni_el_cambio_de_dia_ni_terminar_el_tacto_cierran_la_espera()
    {
        var (espera, animal) = await EsperaDeUnaVacaMarcadaAsync();

        _e.Reloj.Avanzar(TimeSpan.FromDays(3)); // días después: sigue pendiente, con su tiempo a la vista
        var terminar = await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null));
        Assert.True(terminar.EsCorrecto, terminar.Mensaje);
        await _e.Campo.IniciarTrabajoAsync(_e.RodeoId);

        Assert.Equal("Pendiente", Estado(espera));
        var lista = await _e.Campo.ListarEsperasAsync(_e.RodeoId);
        Assert.Equal(animal, Assert.Single(lista).AnimalId);
    }

    [Fact]
    public async Task Leer_otras_vacas_mientras_hay_esperas_no_las_afecta()
    {
        var (espera, _) = await EsperaDeUnaVacaMarcadaAsync();

        await _e.VacaMarcadaAsync("200");
        await _e.VacaMarcadaAsync("300");

        Assert.Equal("Pendiente", Estado(espera));
    }

    // ------------------------------------------------------------------ RF-5: lectura de una vaca en espera

    [Fact]
    public async Task Leer_una_vaca_en_espera_abre_su_ficha_sin_marcarla_ni_sumar_ni_avisar_de_repetida()
    {
        var (_, animal) = await EsperaDeUnaVacaMarcadaAsync();
        var marcasAntes = _e.Cuenta("MarcaPasada");
        var contadorAntes = (await _e.EstadoAsync()).Contador;

        var lectura = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.EnEspera, lectura.Tipo);
        Assert.Equal(animal, lectura.AnimalId);
        Assert.NotNull(lectura.Ficha);
        Assert.True(lectura.Ficha!.EnEspera);
        Assert.Null(lectura.MarcaId);
        Assert.Null(lectura.Aviso);
        Assert.Equal(marcasAntes, _e.Cuenta("MarcaPasada"));
        Assert.Equal(contadorAntes, (await _e.EstadoAsync()).Contador);
    }

    [Fact]
    public async Task Leer_una_vaca_en_espera_que_nunca_se_marco_hoy_tampoco_la_marca()
    {
        var animal = _e.AnimalImportado("500");
        await PonerOkAsync(animal);

        var lectura = await _e.LeerAsync("500");

        Assert.Equal(TipoResultadoLectura.EnEspera, lectura.Tipo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Manga"));
    }

    [Fact]
    public async Task Una_vaca_resuelta_vuelve_a_comportarse_como_cualquier_vaca_ya_procesada()
    {
        var (espera, animal) = await EsperaDeUnaVacaMarcadaAsync();
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        Assert.Equal("Resuelta", Estado(espera));

        var lectura = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Repetido, lectura.Tipo);
        Assert.False(lectura.Ficha?.EnEspera ?? false);
    }

    [Fact]
    public async Task La_ficha_de_una_vaca_dice_si_esta_en_espera()
    {
        var animal = _e.AnimalImportado("100");
        Assert.False((await _e.Campo.ObtenerFichaAsync(animal)).Valor.EnEspera);

        await PonerOkAsync(animal);

        Assert.True((await _e.Campo.ObtenerFichaAsync(animal)).Valor.EnEspera);
    }

    // ------------------------------------------------------------------ RF-9: desmarcar una vaca en espera

    [Fact]
    public async Task Desmarcar_una_vaca_en_espera_se_rechaza_y_no_cambia_nada()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        await PonerOkAsync(lectura.AnimalId!.Value);
        var antes = _e.FotoCampo();

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.VacaEnEspera, r.Codigo);
        Assert.Equal("Está en espera: primero resolvela o saltala.", r.Mensaje);
        Assert.Equal("0", _e.Col("MarcaPasada", lectura.MarcaId.Value, "Anulado"));
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Despues_de_saltear_la_espera_ya_se_puede_desmarcar()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        var animal = lectura.AnimalId!.Value;
        await PonerOkAsync(animal);
        await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(animal));

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        Assert.True(r.EsCorrecto, r.Mensaje);
    }

    // ------------------------------------------------------------------ Deshacer el tacto que resolvió la espera

    [Fact]
    public async Task Deshacer_el_tacto_que_resolvio_una_espera_no_la_vuelve_a_pendiente()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = await PonerOkAsync(animal);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        await _e.AnularTactoAsync(tacto);

        Assert.Equal("Resuelta", Estado(espera));
        Assert.Empty(await _e.Campo.ListarEsperasAsync(_e.RodeoId));
    }

    // ------------------------------------------------------------------ RF-14: persisten

    [Fact]
    public async Task Las_esperas_pendientes_estan_en_la_base_y_las_ve_un_contexto_nuevo()
    {
        var animal = _e.AnimalImportado("100");
        var espera = await PonerOkAsync(animal);

        // Cada llamada abre un contexto nuevo: lo que se ve vino del archivo, no de memoria. La conexión cruda de la base de prueba
        // (otra conexión distinta) también lo ve confirmado.
        Assert.Equal(1, _e.Base.Contar("SELECT COUNT(*) FROM Espera WHERE Estado = 'Pendiente'"));
        Assert.Equal(espera, Assert.Single(await _e.Servicio<ICampoService>().ListarEsperasAsync(_e.RodeoId)).EsperaId);
    }

    // ------------------------------------------------------------------ Historial (RF-12)

    [Fact]
    public async Task Las_tres_acciones_de_espera_quedan_en_el_historial_con_su_fecha()
    {
        var a = _e.AnimalImportado("1", _e.EstadoSeca);
        var b = _e.AnimalImportado("2", _e.EstadoSeca);
        var ea = await PonerOkAsync(a);
        await PonerOkAsync(b);
        await _e.TactoAsync(a, _e.EstadoPrenada);
        await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(b));

        Assert.Equal(2, _e.Cuenta("Historial", "Accion = 'EsperaCreada'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaResuelta'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'EsperaSalteada'"));
        Assert.Equal(1, _e.Cuenta("Historial", $"Accion = 'EsperaResuelta' AND Entidad = 'Espera' AND EntidadId = '{T(ea)}'"));
    }

    // ------------------------------------------------------------------ apoyo

    /// <summary>Una vaca ya marcada (leída) y puesta en espera, como pasa en la manga.</summary>
    private async Task<(Guid Espera, Guid Animal)> EsperaDeUnaVacaMarcadaAsync()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        return (await PonerOkAsync(animal), animal);
    }
}
