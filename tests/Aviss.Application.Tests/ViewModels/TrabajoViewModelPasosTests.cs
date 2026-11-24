using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

// Spec 005 (RF-27 a RF-30): la ficha con eventos con pasos posteriores (Preñada fecha previa y Aborto).
public class TrabajoViewModelPasosTests
{
    private static readonly EventoCampoDto Revisar = new(Guid.Parse("66666666-0000-0000-0000-000000000001"), "Revisar Nuevamente", false, true);
    private static readonly EventoCampoDto Ok = new(Guid.Parse("66666666-0000-0000-0000-000000000002"), "OK", false, true);
    private static readonly EventoCampoDto Vacia = new(Guid.Parse("66666666-0000-0000-0000-000000000003"), "VACIA", true, true);
    private static readonly EventoCampoDto Correcta = new(Guid.Parse("66666666-0000-0000-0000-000000000004"), "Preñada fecha correcta", true, true);
    private static readonly EventoCampoDto Previa = new(Guid.Parse("66666666-0000-0000-0000-000000000005"), "Preñada fecha previa", true, true,
        [PasoEvento.DiasPreniez], Correcta.Id, "PRENADA_FECHA_PREVIA");
    private static readonly EventoCampoDto Aborto = new(Guid.Parse("66666666-0000-0000-0000-000000000006"), "Aborto", true, true,
        [PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], null, "ABORTO");
    private static readonly EventoCampoDto NoInsem = new(Guid.Parse("66666666-0000-0000-0000-000000000007"), "NOINSEM", true, true);
    private static readonly EventoCampoDto Insemin = new(Guid.Parse("66666666-0000-0000-0000-000000000008"), "INSEMIN", true, false, [], null, "INSEMIN");
    private static readonly EventoCampoDto Seca = new(Guid.Parse("66666666-0000-0000-0000-000000000009"), "SECA", true, false, [], null, "SECA");
    private static readonly EventoCampoDto Fresca = new(Guid.Parse("66666666-0000-0000-0000-00000000000a"), "FRESCA", true, false, [], null, "FRESCA");

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            var sesion = new SesionTrabajo();
            sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [Revisar, Ok, Vacia, Correcta, Previa, Aborto, NoInsem, Insemin, Seca, Fresca];
            Vm = new TrabajoViewModel(Campo, new FakeLector(), new FakePreferencias { ControlPorMangaPorDefecto = true }, new FakeAvisos(),
                new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), sesion);
        }

        public Guid Vaca(string caravana, Guid? estadoId = null)
        {
            var animal = Guid.NewGuid();
            var ficha = Datos.Ficha(animal, caravana, cvet: "PRE") with { EstadoId = estadoId };
            Campo.Lecturas[caravana] = new ResultadoLecturaDto(TipoResultadoLectura.Marcada, caravana, animal, caravana, ficha,
                Guid.NewGuid(), 1, 1, null, null, false);
            return animal;
        }

        public async Task<Guid> AbrirFichaAsync(string caravana, Guid? estadoId = null)
        {
            var animal = Vaca(caravana, estadoId);
            await Vm.IniciarAsync();
            await Vm.EncolarLecturaAsync(caravana);
            return animal;
        }

        /// <summary>Abre el selector, elige Aborto y completa sus dos pasos.</summary>
        public async Task ElegirAbortoAsync(string dias, ResultadoAborto resultado)
        {
            await Vm.AbrirEventosCommand.ExecuteAsync(null);
            await Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
            Vm.Selector.TextoDias = dias;
            await Vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
            await Vm.Selector.ElegirResultadoCommand.ExecuteAsync(resultado);
        }
    }

    [Fact]
    public async Task La_lista_principal_trae_los_siete_en_orden_y_Otro_evento_el_resto()
    {
        var b = new Banco();
        b.Campo.Estados = [Revisar, Ok, Vacia, Correcta, Previa, Aborto, NoInsem, Insemin, Seca, Fresca];
        await b.AbrirFichaAsync("1001");

        Assert.Equal(["Revisar Nuevamente", "OK", "VACIA", "Preñada fecha correcta", "Preñada fecha previa", "Aborto", "NOINSEM"],
            b.Vm.Selector.Principales.Select(e => e.Nombre));
        Assert.Equal(["RECHAZO"], b.Vm.Selector.Otros.Select(e => e.Nombre));
    }

    [Fact]
    public async Task Elegir_un_evento_sin_pasos_no_guarda_y_habilita_Entrar()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Correcta);

        Assert.Equal(Correcta, b.Vm.EventoElegido);
        Assert.Equal("Preñada fecha correcta", b.Vm.TextoEventoElegido);
        Assert.Empty(b.Campo.Tactos);
        Assert.True(b.Vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Con_un_paso_abierto_no_se_guarda_nada_ni_se_puede_entrar_y_el_casillero_no_cambia()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Previa);

        Assert.True(b.Vm.Selector.HayPaso);
        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);
        Assert.Null(b.Vm.EventoElegido);
        Assert.False(b.Vm.EntrarCommand.CanExecute(null));
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
    }

    [Fact]
    public async Task Fecha_previa_confirmada_queda_en_el_casillero_con_el_resumen_y_Entrar_manda_la_eleccion()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Previa);
        b.Vm.Selector.TextoDias = "130";
        await b.Vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal("Preñada fecha previa, 130 días", b.Vm.TextoEventoElegido);
        Assert.Empty(b.Campo.Tactos);
        Assert.True(b.Vm.EntrarCommand.CanExecute(null));

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Previa.Id), (cmd.AnimalId, cmd.EstadoNuevoId));
        Assert.Equal(new EleccionEvento(Previa.Id, 130), cmd.Eleccion);
        Assert.Empty(b.Campo.Rechazos);
        Assert.Equal("1001: Preñada fecha previa, 130 días", b.Vm.UltimoGuardado);
        Assert.Equal(TipoDeshacer.Tacto, b.Vm.AvisoDeshacer!.Tipo);
    }

    [Fact]
    public async Task Cancelar_un_paso_deja_el_evento_que_ya_estaba_en_el_casillero()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);

        b.Vm.Selector.CancelarCommand.Execute(null);

        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);   // vuelve a la lista
        Assert.False(b.Vm.Selector.HayPaso);
        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Cerrar_la_lista_con_un_paso_abierto_lo_descarta_sin_elegir_nada()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Previa);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        Assert.False(b.Vm.Selector.HayPaso);
        Assert.Null(b.Vm.EventoElegido);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task Reabrir_el_evento_con_pasos_precarga_los_valores_actuales()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.ElegirAbortoAsync("45", ResultadoAborto.Seca);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);

        Assert.Equal("45", b.Vm.Selector.TextoDias);
        Assert.Equal(Aborto.Id, b.Vm.Selector.Resaltado);
    }

    [Fact]
    public async Task Las_lecturas_con_un_paso_abierto_quedan_en_cola_y_se_procesan_al_cerrar()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        b.Vaca("2002");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Aborto);

        await b.Vm.EncolarLecturaAsync("2002");
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);
        Assert.Equal("2002", b.Vm.Ficha!.Caravana);
    }

    [Theory]
    [InlineData(ResultadoAborto.VaciaOk)]
    [InlineData(ResultadoAborto.Inseminada)]
    [InlineData(ResultadoAborto.Seca)]
    [InlineData(ResultadoAborto.Fresca)]
    public async Task Entrar_con_un_aborto_que_cambia_el_estado_guarda_un_tacto_con_la_eleccion(ResultadoAborto resultado)
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1001");
        await b.ElegirAbortoAsync("45", resultado);
        b.Vm.ObservacionPendiente = "Abortó solo";

        Assert.Equal($"Aborto, 45 días, {Aviss.Domain.Catalogo.ReglasResultadoAborto.Etiqueta(resultado)}", b.Vm.TextoEventoElegido);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Aborto.Id, "Abortó solo"), (cmd.AnimalId, cmd.EstadoNuevoId, cmd.Observaciones));
        Assert.Equal(new EleccionEvento(Aborto.Id, null, 45, resultado), cmd.Eleccion);
        Assert.Empty(b.Campo.Rechazos);
        Assert.Equal(TipoDeshacer.Tacto, b.Vm.AvisoDeshacer!.Tipo);
    }

    [Fact]
    public async Task Entrar_con_un_aborto_con_Rechazo_guarda_un_solo_registro_de_rechazo_con_los_dias()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1001");
        await b.ElegirAbortoAsync("45", ResultadoAborto.Rechazo);
        b.Vm.ObservacionPendiente = "Pata";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Equal((animal, Aborto.Id, "Pata"), (cmd.AnimalId, cmd.EventoId, cmd.Observaciones));
        Assert.Equal(new EleccionEvento(Aborto.Id, null, 45, ResultadoAborto.Rechazo), cmd.Eleccion);
        Assert.Empty(b.Campo.Tactos);
        Assert.Equal(TipoDeshacer.Rechazo, b.Vm.AvisoDeshacer!.Tipo);
        Assert.Equal("1001: Aborto, 45 días, Rechazo", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task Entrar_con_OK_manda_el_comando_de_solo_evento_con_su_eleccion_sin_datos()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Equal(new EleccionEvento(Ok.Id), cmd.Eleccion);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Entrar_con_el_Rechazo_fijo_sigue_siendo_un_solo_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(ArmadorListaEventos.EventoRechazo);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal(EventosCampo.Rechazo, Assert.Single(b.Campo.Rechazos).EventoId);
    }

    [Fact]
    public async Task La_eleccion_no_pasa_de_una_vaca_a_la_siguiente()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        b.Vaca("2002");
        await b.ElegirAbortoAsync("45", ResultadoAborto.VaciaOk);
        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);
        await b.Vm.EncolarLecturaAsync("2002");

        Assert.Null(b.Vm.EventoElegido);
        Assert.Null(b.Vm.EleccionElegida);
        Assert.Null(b.Vm.Selector.EleccionActual);
    }

    // ---- resaltado inicial (RF-9)

    [Fact]
    public async Task Una_vaca_PRENADA_abre_con_Preniada_fecha_correcta_resaltada()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001", Correcta.Id);   // PREÑADA conserva su identidad: es "fecha correcta"

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        Assert.Equal(Correcta.Id, b.Vm.Selector.Resaltado);
        Assert.False(b.Vm.Selector.OtroEventoResaltado);
    }

    [Theory]
    [InlineData("INSEMIN")]
    [InlineData("SECA")]
    [InlineData("FRESCA")]
    public async Task Una_vaca_en_INSEMIN_SECA_o_FRESCA_abre_sin_resaltar_nada(string nombre)
    {
        var b = new Banco();
        var estado = new[] { Insemin, Seca, Fresca }.First(e => e.Nombre == nombre);
        await b.AbrirFichaAsync("1001", estado.Id);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        // Esos tres no están en ninguna lista (decisión del 2026-11-24): ni "Otro evento" ni un principal quedan marcados.
        Assert.False(b.Vm.Selector.OtroEventoResaltado);
        Assert.Null(b.Vm.Selector.Buscar(b.Vm.Selector.Resaltado));
        Assert.False(b.Vm.Selector.MostrandoOtros);
    }

    // ---- cuenta de toques (RF-14)

    [Fact]
    public async Task Cuenta_de_toques_sin_pasos_2_con_principal_y_3_desde_Otro_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");

        // Principal: casillero (1) + el evento (2).
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);
        Assert.Equal(Ok, b.Vm.EventoElegido);

        // Otro evento: casillero (1) + "Otro evento" (2) + el evento (3).
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Seca);
        Assert.Equal(Seca, b.Vm.EventoElegido);
    }

    // ---- "+ Agregar evento" solo en la secundaria, y sin paso abierto

    [Fact]
    public async Task Agregar_evento_solo_se_ofrece_en_Otro_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        Assert.False(b.Vm.Selector.PuedeCrearNuevo);

        b.Vm.Selector.VerOtrosCommand.Execute(null);

        Assert.True(b.Vm.Selector.PuedeCrearNuevo);
    }
}
