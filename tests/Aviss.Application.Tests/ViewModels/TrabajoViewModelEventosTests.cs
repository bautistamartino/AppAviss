using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Spec 005: el selector de eventos en la pantalla de trabajo (lógica de ViewModel, sin MAUI).
public class TrabajoViewModelEventosTests
{
    private static readonly EventoCampoDto Revisar = new(Guid.Parse("55555555-0000-0000-0000-000000000001"), "Revisar Nuevamente", false, true);
    private static readonly EventoCampoDto Ok = new(Guid.Parse("55555555-0000-0000-0000-000000000002"), "OK", false, true);
    private static readonly EventoCampoDto Vacia = new(Guid.Parse("55555555-0000-0000-0000-000000000003"), "VACIA", true, true);
    private static readonly EventoCampoDto Insemin = new(Guid.Parse("55555555-0000-0000-0000-000000000004"), "INSEMIN", true, false);
    private static readonly EventoCampoDto Anestro = new(Guid.Parse("55555555-0000-0000-0000-000000000005"), "ANESTRO", false, false);

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            Sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [Revisar, Ok, Vacia, Insemin, Anestro];
            Vm = new TrabajoViewModel(Campo, new FakeLector(), new FakePreferencias { ControlPorMangaPorDefecto = true }, new FakeAvisos(),
                new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), Sesion);
        }

        /// <summary>Una vaca que entra en tacto, con el estado que se pida; leerla abre su ficha.</summary>
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
    }

    [Fact]
    public async Task Elegir_un_principal_no_guarda_nada_y_habilita_Entrar()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        Assert.False(b.Vm.EntrarCommand.CanExecute(null));

        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);

        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);   // la lista se cierra y el evento queda en el casillero
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
        Assert.True(b.Vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Entrar_con_OK_guarda_un_solo_evento_y_no_un_tacto()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);
        b.Vm.ObservacionPendiente = "Todo bien";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Equal((animal, Ok.Id, "Todo bien"), (cmd.AnimalId, cmd.EventoId, cmd.Observaciones));
        Assert.Empty(b.Campo.Tactos);
        Assert.Equal("1001: OK", b.Vm.UltimoGuardado);
        Assert.Equal(TipoDeshacer.Rechazo, b.Vm.AvisoDeshacer!.Tipo);
    }

    [Fact]
    public async Task Entrar_con_VACIA_guarda_un_tacto()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Vacia);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Vacia.Id), (cmd.AnimalId, cmd.EstadoNuevoId));
        Assert.Empty(b.Campo.Rechazos);
        Assert.Equal(TipoDeshacer.Tacto, b.Vm.AvisoDeshacer!.Tipo);
    }

    [Fact]
    public async Task Un_evento_de_Otro_evento_sin_cambio_de_estado_tambien_guarda_un_solo_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);

        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Anestro);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal(Anestro.Id, Assert.Single(b.Campo.Rechazos).EventoId);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task El_resaltado_inicial_es_el_estado_actual_de_la_vaca()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001", Vacia.Id);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        Assert.Equal(Vacia.Id, b.Vm.Selector.Resaltado);
        Assert.False(b.Vm.Selector.OtroEventoResaltado);
    }

    [Fact]
    public async Task Con_una_vaca_en_un_estado_de_Otro_evento_ese_boton_se_resalta_y_la_lista_abre_en_los_principales()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001", Insemin.Id);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        Assert.True(b.Vm.Selector.OtroEventoResaltado);
        Assert.False(b.Vm.Selector.MostrandoOtros);
    }

    [Fact]
    public async Task Despues_de_elegir_el_resaltado_es_el_elegido_y_siempre_se_vuelve_a_abrir_en_los_principales()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001", Vacia.Id);
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Anestro);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        Assert.Equal(Anestro.Id, b.Vm.Selector.Resaltado);
        Assert.False(b.Vm.Selector.MostrandoOtros);
    }

    [Fact]
    public async Task Nuevo_evento_no_se_puede_abrir_desde_los_principales_pero_si_desde_Otro_evento()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        Assert.False(b.Vm.HayFormularioEvento);

        b.Vm.Selector.VerOtrosCommand.Execute(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        Assert.True(b.Vm.HayFormularioEvento);
    }

    [Fact]
    public async Task Un_evento_creado_queda_en_Otro_evento_y_no_se_elige_ni_se_guarda()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        b.Vm.NuevoEventoNombre = "Ñandú";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        var creado = Assert.Single(b.Vm.Selector.Otros, e => e.Nombre == "ÑANDÚ");
        Assert.False(creado.EsPrincipal);
        Assert.DoesNotContain(creado, b.Vm.Selector.Principales);
        Assert.True(b.Vm.Selector.MostrandoOtros);          // sigue en "Otro evento"
        Assert.Null(b.Vm.EventoElegido);                    // no se eligió
        Assert.Null(b.Vm.Selector.Resaltado);
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
    }

    [Fact]
    public async Task Con_todos_los_principales_desactivados_el_selector_queda_con_Otro_evento_y_el_Rechazo()
    {
        var b = new Banco();
        b.Campo.Estados = [Insemin];
        await b.AbrirFichaAsync("1001");

        Assert.False(b.Vm.Selector.HayPrincipales);
        Assert.Equal([EventosCampo.Rechazo, Insemin.Id], b.Vm.Selector.Otros.Select(e => e.Id));
    }

    [Fact]
    public async Task Una_lectura_con_la_lista_principal_abierta_queda_en_cola_y_se_procesa_al_cerrar()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        b.Vaca("2002");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        await b.Vm.EncolarLecturaAsync("2002");
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);   // todavía no se procesó

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);
        // Spec 009: la lectura guardada se procesa al cerrar el cuadro; con la ficha de la 1001 abierta, pregunta antes de reemplazarla.
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);
    }

    [Fact]
    public async Task Una_lectura_con_Otro_evento_abierto_tambien_queda_en_cola()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        b.Vaca("2002");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);

        await b.Vm.EncolarLecturaAsync("2002");
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);
        // Spec 009: la lectura guardada se procesa al cerrar el cuadro; con la ficha de la 1001 abierta, pregunta antes de reemplazarla.
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);
    }

    [Fact]
    public async Task Si_el_guardado_se_rechaza_el_evento_sigue_elegido()
    {
        var b = new Banco();
        await b.AbrirFichaAsync("1001");
        b.Campo.OnRegistrarRechazo = _ => Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La hora de la tablet no es válida.");
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.Equal("La hora de la tablet no es válida.", b.Vm.MensajeError);
    }
}
