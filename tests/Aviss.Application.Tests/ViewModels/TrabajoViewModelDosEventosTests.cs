using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

// Spec 011 (RF-1 a RF-9, RF-17, RF-18): la segunda fila de la ficha. Lógica de ViewModel, sin MAUI.
public class TrabajoViewModelDosEventosTests
{
    // OK y Revisar Nuevamente: admiten un segundo y pueden ser segundo.
    private static readonly EventoCampoDto Ok = new(Guid.Parse("66666666-0000-0000-0000-000000000001"), "OK", false, true, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Revisar = new(Guid.Parse("66666666-0000-0000-0000-000000000002"), "Revisar Nuevamente", false, true, admiteSegundo: true, puedeSerSegundo: true);
    // VACIA (estándar de estado): admite un segundo, pero no puede ser segundo.
    private static readonly EventoCampoDto Vacia = new(Guid.Parse("66666666-0000-0000-0000-000000000003"), "VACIA", true, true, admiteSegundo: true, puedeSerSegundo: false);
    // Un evento creado a mano (PROSTAG): cambia el estado como Evento 1 y puede ser cualquiera de los dos.
    private static readonly EventoCampoDto Prostag = new(Guid.Parse("66666666-0000-0000-0000-000000000004"), "PROSTAG", true, false, admiteSegundo: true, puedeSerSegundo: true);
    // Aborto (con pasos): no admite un segundo.
    private static readonly EventoCampoDto Aborto = new(Guid.Parse("66666666-0000-0000-0000-000000000005"), "Aborto", true, true,
        [PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], null, "ABORTO");

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public FakeLector Lector { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco(params EventoCampoDto[] eventos)
        {
            Sesion.Empezar(Datos.Rodeo());
            Campo.Estados = eventos.Length > 0 ? eventos : [Ok, Revisar, Vacia, Prostag, Aborto];
            Vm = new TrabajoViewModel(Campo, Lector, new FakePreferencias { ControlPorMangaPorDefecto = true }, new FakeAvisos(),
                new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), Sesion, Lector);
        }

        public async Task<Guid> AbrirFichaAsync(string caravana = "1000", bool enEspera = false)
        {
            var animal = Guid.NewGuid();
            var ficha = Datos.Ficha(animal, caravana, cvet: "PRE", enEspera: enEspera);
            Campo.Lecturas[caravana] = new ResultadoLecturaDto(TipoResultadoLectura.Marcada, caravana, animal, caravana, ficha,
                Guid.NewGuid(), 1, 1, null, null, false);
            Campo.Fichas[animal] = ficha;
            await Vm.IniciarAsync();
            await Vm.EncolarLecturaAsync(caravana);
            return animal;
        }

        /// <summary>Elige el Evento 1 por la lista (como el veterinario).</summary>
        public async Task ElegirEvento1Async(EventoCampoDto evento)
        {
            await Vm.AbrirEventosCommand.ExecuteAsync(null);
            await Vm.Selector.ElegirCommand.ExecuteAsync(evento);
        }

        /// <summary>Abre la fila 2 y elige el Evento 2 por la lista.</summary>
        public async Task ElegirEvento2Async(EventoCampoDto evento)
        {
            Vm.AgregarEvento2Command.Execute(null);
            await Vm.AbrirEventos2Command.ExecuteAsync(null);
            await Vm.Selector.ElegirCommand.ExecuteAsync(evento);
        }
    }

    // ---- RF-1, RF-2: cuándo aparece "+ evento"

    [Fact]
    public async Task Sin_evento_elegido_no_aparece_mas_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();

        Assert.False(b.Vm.PuedeAgregarEvento2);
        Assert.False(b.Vm.HayFila2);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("VACIA")]
    [InlineData("PROSTAG")]
    public async Task Con_un_evento_que_admite_segundo_aparece_mas_evento(string nombre)
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(b.Campo.Estados.First(e => e.Nombre == nombre));

        Assert.True(b.Vm.PuedeAgregarEvento2);
        Assert.False(b.Vm.HayFila2);   // la fila 2 arranca cerrada
    }

    [Fact]
    public async Task Con_Aborto_no_aparece_mas_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        b.Vm.Selector.TextoDias = "45";
        await b.Vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Seca);

        Assert.Equal(Aborto, b.Vm.EventoElegido);
        Assert.False(b.Vm.PuedeAgregarEvento2);
    }

    [Fact]
    public async Task Con_el_Rechazo_fijo_no_aparece_mas_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(ArmadorListaEventos.EventoRechazo);

        Assert.False(b.Vm.PuedeAgregarEvento2);
    }

    [Fact]
    public async Task Agregar_evento_abre_la_fila_2_sin_guardar_nada_y_sin_mas_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);

        b.Vm.AgregarEvento2Command.Execute(null);

        Assert.True(b.Vm.HayFila2);
        Assert.False(b.Vm.PuedeAgregarEvento2);   // como máximo dos eventos (RF-6)
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("Tocá para elegir", b.Vm.TextoEvento2Elegido);
        Assert.Equal("Opcional", b.Vm.TextoCasilleroObservacion2);
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
    }

    // ---- RF-3: la lista del Evento 2

    [Fact]
    public async Task La_lista_del_evento_2_muestra_solo_combinables_y_esconde_el_evento_1()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);

        await b.Vm.AbrirEventos2Command.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);
        Assert.True(b.Vm.Selector.ModoSegundo);
        var visibles = b.Vm.Selector.Principales.Concat(b.Vm.Selector.Otros).Select(e => e.Nombre).ToList();
        Assert.Contains("Revisar Nuevamente", visibles);
        Assert.Contains("PROSTAG", visibles);
        Assert.DoesNotContain("OK", visibles);                      // el Evento 1 no se ofrece
        Assert.DoesNotContain("VACIA", visibles);                   // un estándar de estado no puede ser Evento 2
        Assert.DoesNotContain("Aborto", visibles);
        Assert.DoesNotContain(ArmadorListaEventos.EventoRechazo.Nombre, visibles);
    }

    [Fact]
    public async Task Abrir_la_lista_del_evento_1_despues_de_la_del_2_vuelve_a_mostrar_todo()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);
        await b.Vm.AbrirEventos2Command.ExecuteAsync(null);
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        Assert.False(b.Vm.Selector.ModoSegundo);
        Assert.Contains(b.Vm.Selector.Principales, e => e.Nombre == "VACIA");
    }

    [Fact]
    public async Task Elegir_el_evento_2_lo_deja_en_la_fila_cierra_la_lista_y_no_guarda()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);

        await b.ElegirEvento2Async(Prostag);

        Assert.Equal(Prostag, b.Vm.Evento2Elegido);
        Assert.Equal("PROSTAG", b.Vm.TextoEvento2Elegido);
        Assert.Equal(Ok, b.Vm.EventoElegido);   // el Evento 1 no se toca
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
    }

    // ---- RF-4: una observación por fila

    [Fact]
    public async Task Cada_fila_tiene_su_observacion()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "obs uno";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);
        await b.Vm.AbrirObservacion2Command.ExecuteAsync(null);
        Assert.Equal("", b.Vm.TextoObservacion);   // abre con lo de la fila 2, no con lo de la 1
        b.Vm.TextoObservacion = "obs dos";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        Assert.Equal("obs uno", b.Vm.ObservacionPendiente);
        Assert.Equal("obs dos", b.Vm.Observacion2Pendiente);
        Assert.Equal("obs dos", b.Vm.TextoCasilleroObservacion2);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task La_observacion_del_evento_2_se_puede_escribir_sin_la_del_evento_1()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        await b.Vm.AbrirObservacion2Command.ExecuteAsync(null);
        b.Vm.TextoObservacion = "solo la dos";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Null(cmd.Observaciones);
        Assert.Equal(new SegundoEventoElegido(Prostag.Id, "solo la dos"), cmd.Eleccion!.Segundo);
    }

    // ---- RF-5: Quitar

    [Fact]
    public async Task Quitar_descarta_el_evento_2_y_su_observacion_sin_tocar_el_evento_1()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        b.Vm.ObservacionPendiente = "uno";
        await b.ElegirEvento2Async(Prostag);
        b.Vm.Observacion2Pendiente = "dos";

        b.Vm.QuitarEvento2Command.Execute(null);

        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("", b.Vm.Observacion2Pendiente);
        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.Equal("uno", b.Vm.ObservacionPendiente);
        Assert.True(b.Vm.PuedeAgregarEvento2);   // vuelve el "+ evento"
    }

    // ---- RF-7: cambiar el Evento 1 descarta el Evento 2 con el mensaje fijo

    [Fact]
    public async Task Cambiar_el_evento_1_a_uno_que_no_admite_segundo_descarta_el_evento_2_con_el_mensaje_fijo()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        b.Vm.Observacion2Pendiente = "dos";

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        b.Vm.Selector.TextoDias = "30";
        await b.Vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Seca);

        Assert.Equal(Aborto, b.Vm.EventoElegido);   // el Evento 1 elegido no se pierde
        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("", b.Vm.Observacion2Pendiente);
        Assert.Equal("Ese evento no se combina con otro: se quitó el evento 2", b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Cambiar_el_evento_1_al_mismo_que_el_evento_2_descarta_el_evento_2_con_el_mensaje_fijo()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.ElegirEvento1Async(Prostag);

        Assert.Equal(Prostag, b.Vm.EventoElegido);
        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("Ese evento no se combina con otro: se quitó el evento 2", b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Cambiar_el_evento_1_a_otro_compatible_conserva_el_evento_2_sin_mensaje()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.ElegirEvento1Async(Vacia);

        Assert.Equal(Vacia, b.Vm.EventoElegido);
        Assert.True(b.Vm.HayFila2);
        Assert.Equal(Prostag, b.Vm.Evento2Elegido);
        Assert.True(string.IsNullOrEmpty(b.Vm.MensajeInfo));
    }

    [Fact]
    public async Task Cambiar_el_evento_1_con_la_fila_2_vacia_la_cierra_sin_mensaje()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(ArmadorListaEventos.EventoRechazo);

        Assert.False(b.Vm.HayFila2);
        Assert.True(string.IsNullOrEmpty(b.Vm.MensajeInfo));   // no se perdió nada: no hay nada que avisar
    }

    // ---- RF-9: En espera y Saltar limpian la fila

    [Fact]
    public async Task En_espera_descarta_los_dos_eventos_y_la_fila()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        b.Vm.Observacion2Pendiente = "dos";

        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("", b.Vm.Observacion2Pendiente);
        Assert.Empty(b.Campo.Rechazos);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Saltar_descarta_los_dos_eventos_y_la_fila_y_no_toca_la_espera_si_no_era_de_espera()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Empty(b.Campo.EsperasSalteadas);
        Assert.Empty(b.Campo.Rechazos);
    }

    [Fact]
    public async Task Saltar_una_vaca_en_espera_con_dos_eventos_la_saltea_y_descarta_todo()
    {
        var b = new Banco();
        await b.AbrirFichaAsync(enEspera: true);
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        Assert.Single(b.Campo.EsperasSalteadas);
        Assert.False(b.Vm.HayFila2);
        Assert.Empty(b.Campo.Rechazos);
    }

    [Fact]
    public async Task La_fila_2_arranca_cerrada_en_cada_ficha()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        await b.AbrirFichaAsync("1001");

        Assert.False(b.Vm.HayFila2);
        Assert.Null(b.Vm.Evento2Elegido);
        Assert.Equal("", b.Vm.Observacion2Pendiente);
    }

    // ---- RF-10, RF-17: Entrar

    [Fact]
    public async Task Entrar_con_dos_eventos_de_solo_evento_manda_Segundo_y_dice_OK_mas_PROSTAG()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        b.Vm.ObservacionPendiente = "uno";
        await b.ElegirEvento2Async(Prostag);
        b.Vm.Observacion2Pendiente = "dos";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Equal((animal, Ok.Id, "uno"), (cmd.AnimalId, cmd.EventoId, cmd.Observaciones));
        Assert.Equal(new SegundoEventoElegido(Prostag.Id, "dos"), cmd.Eleccion!.Segundo);
        Assert.Empty(b.Campo.Tactos);
        Assert.Equal("1000: OK + PROSTAG", b.Vm.UltimoGuardado);
        Assert.Equal("1000: OK + PROSTAG", b.Vm.AvisoDeshacer!.Texto);
        Assert.Equal(TipoDeshacer.Rechazo, b.Vm.AvisoDeshacer.Tipo);
        Assert.False(b.Vm.HayFila2);
    }

    [Fact]
    public async Task Entrar_con_un_estado_mas_un_evento_guarda_un_tacto_con_Segundo()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Prostag);
        await b.ElegirEvento2Async(Ok);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Prostag.Id), (cmd.AnimalId, cmd.EstadoNuevoId));
        Assert.Equal(new SegundoEventoElegido(Ok.Id, null), cmd.Eleccion!.Segundo);
        Assert.Empty(b.Campo.Rechazos);
        Assert.Equal("1000: PROSTAG + OK", b.Vm.UltimoGuardado);
        Assert.Equal(TipoDeshacer.Tacto, b.Vm.AvisoDeshacer!.Tipo);
    }

    [Fact]
    public async Task Un_solo_evento_se_guarda_sin_Segundo_como_siempre()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Null(cmd.Eleccion?.Segundo);
        Assert.Equal("1000: OK", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task Entrar_con_la_fila_2_abierta_sin_evento_no_guarda_y_avisa()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.Rechazos);
        Assert.Empty(b.Campo.Tactos);
        Assert.Equal("Elegí el evento 2 o tocá Quitar.", b.Vm.MensajeError);
        Assert.NotNull(b.Vm.Ficha);   // la ficha sigue abierta con todo lo elegido
    }

    [Fact]
    public async Task Si_el_servicio_rechaza_queda_todo_elegido_y_se_muestra_el_motivo()
    {
        var b = new Banco();
        b.Campo.OnRegistrarRechazo = _ => Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento no se puede combinar.");
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        b.Vm.Observacion2Pendiente = "dos";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal("Ese evento no se puede combinar.", b.Vm.MensajeError);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.True(b.Vm.HayFila2);
        Assert.Equal(Prostag, b.Vm.Evento2Elegido);
        Assert.Equal("dos", b.Vm.Observacion2Pendiente);
    }

    // ---- RF-18: las lecturas se ignoran con la fila 2, su lista o su observación abiertas

    private const string MensajeIgnorada = "Terminá con esta vaca antes de leer otra";

    private static void PrepararOtraVaca(Banco b, string caravana)
    {
        var animal = Guid.NewGuid();
        b.Campo.Lecturas[caravana] = Datos.Marcada(animal, caravana, contador: 2);
    }

    [Fact]
    public async Task Con_la_fila_2_abierta_la_lectura_de_otra_vaca_se_ignora()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);
        PrepararOtraVaca(b, "2000");
        var lecturasAntes = b.Campo.LecturasProcesadas.Count;

        await b.Vm.EncolarLecturaAsync("2000");

        Assert.Equal(lecturasAntes, b.Campo.LecturasProcesadas.Count);   // ni siquiera llegó al servicio
        Assert.Equal("1000", b.Vm.Ficha!.Caravana);
        Assert.Equal(MensajeIgnorada, b.Vm.MensajeInfo);
        Assert.True(b.Vm.HayFila2);
    }

    [Fact]
    public async Task Con_la_lista_del_evento_2_abierta_la_lectura_espera_y_al_cerrar_se_ignora()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        b.Vm.AgregarEvento2Command.Execute(null);
        await b.Vm.AbrirEventos2Command.ExecuteAsync(null);
        PrepararOtraVaca(b, "2000");
        var lecturasAntes = b.Campo.LecturasProcesadas.Count;

        await b.Vm.EncolarLecturaAsync("2000");
        Assert.Equal(lecturasAntes, b.Campo.LecturasProcesadas.Count);   // frenada mientras la lista está abierta
        Assert.Equal("1000", b.Vm.Ficha!.Caravana);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        Assert.Equal("1000", b.Vm.Ficha!.Caravana);   // al cerrar se procesa la pendiente y, con la ficha abierta, se ignora
        Assert.Equal(MensajeIgnorada, b.Vm.MensajeInfo);
        Assert.True(b.Vm.HayFila2);
    }

    [Fact]
    public async Task Con_la_observacion_del_evento_2_abierta_la_lectura_espera_y_al_cerrar_se_ignora()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1000");
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        await b.Vm.AbrirObservacion2Command.ExecuteAsync(null);
        PrepararOtraVaca(b, "2000");

        await b.Vm.EncolarLecturaAsync("2000");
        Assert.Equal("1000", b.Vm.Ficha!.Caravana);
        Assert.Equal(CuadroTrabajo.Observacion, b.Vm.Cuadro);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        Assert.Equal("1000", b.Vm.Ficha!.Caravana);
        Assert.Equal(MensajeIgnorada, b.Vm.MensajeInfo);
        Assert.Equal(Prostag, b.Vm.Evento2Elegido);
    }

    // ---- Detalles de pantalla vistos en la tablet (fix/detalles-dos-eventos)

    /// <summary>Deja en pantalla el aviso "se quitó el evento 2": Evento 1 OK, Evento 2 PROSTAG y el Evento 1 pasa a Aborto (que no admite segundo).</summary>
    private static async Task ProvocarAvisoSeQuitoEvento2Async(Banco b)
    {
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        b.Vm.Selector.TextoDias = "30";
        await b.Vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Seca);
        Assert.Equal(TrabajoViewModel.MensajeSeQuitoEvento2, b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Saltar_limpia_el_aviso_de_se_quito_el_evento_2()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await ProvocarAvisoSeQuitoEvento2Async(b);

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.MensajeInfo);
        Assert.False(b.Vm.HayMensajeInfo);
    }

    [Fact]
    public async Task En_espera_limpia_el_aviso_de_se_quito_el_evento_2()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await ProvocarAvisoSeQuitoEvento2Async(b);

        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Entrar_limpia_el_aviso_de_se_quito_el_evento_2()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await ProvocarAvisoSeQuitoEvento2Async(b);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task El_aviso_de_se_quito_el_evento_2_sigue_mientras_la_ficha_esta_abierta()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await ProvocarAvisoSeQuitoEvento2Async(b);

        Assert.NotNull(b.Vm.Ficha);
        Assert.True(b.Vm.HayMensajeInfo);
    }

    [Fact]
    public async Task La_ayuda_de_la_observacion_dice_a_que_evento_pertenece()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        Assert.Equal("Opcional. Se guarda junto con el próximo evento que elijas.", b.Vm.TextoAyudaObservacion);
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        await b.Vm.AbrirObservacion2Command.ExecuteAsync(null);
        Assert.Equal("Opcional. Se guarda junto con el evento 2.", b.Vm.TextoAyudaObservacion);
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);   // volvió la fila 1: vuelve el texto de siempre
        Assert.Equal("Opcional. Se guarda junto con el próximo evento que elijas.", b.Vm.TextoAyudaObservacion);
    }

    [Fact]
    public async Task La_ayuda_de_la_observacion_avisa_el_cambio_al_cambiar_de_fila()
    {
        var b = new Banco();
        await b.AbrirFichaAsync();
        await b.ElegirEvento1Async(Ok);
        await b.ElegirEvento2Async(Prostag);
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);   // la capa de la fila 1 (FilaActiva = 1)
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);
        var cambios = new List<string?>();
        b.Vm.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        await b.Vm.AbrirObservacion2Command.ExecuteAsync(null);

        Assert.Contains(nameof(TrabajoViewModel.TextoAyudaObservacion), cambios);
    }
}
