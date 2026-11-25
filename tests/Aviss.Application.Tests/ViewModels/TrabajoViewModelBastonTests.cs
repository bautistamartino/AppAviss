using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Puertos;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>
/// Spec 008 (RF-1 a RF-6, RF-12 a RF-16, RF-23): la pantalla de trabajo muestra el estado del bastón, ofrece reconectar según
/// el motivo y no toca la ficha abierta cuando se pierde la conexión. Los EID de prueba llevan el prefijo 999.
/// </summary>
public class TrabajoViewModelBastonTests
{
    private const string Eid = "999000458013169";
    private static readonly EventoCampoDto Ok = new(Guid.Parse("55555555-0000-0000-0000-000000000002"), "OK", false, true);

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeLector Lector { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public FakeBluetoothBaston Bluetooth { get; } = new();
        public FakePreferenciasBaston Baston { get; } = new();
        public FakePantalla Pantalla { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            var sesion = new SesionTrabajo();
            sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [Ok];
            Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true);
            Vm = new TrabajoViewModel(Campo, Lector, new FakePreferencias { ControlPorMangaPorDefecto = true }, new FakeAvisos(),
                Pantalla, new FakeTemporizador(), Navegacion, sesion, Lector, null, Baston, Bluetooth);
        }

        public Guid Vaca(string codigo, string caravana = "1001")
        {
            var animal = Guid.NewGuid();
            var ficha = Datos.Ficha(animal, caravana, Eid, cvet: "PRE");
            Campo.Lecturas[codigo] = Datos.Marcada(animal, caravana) with { Ficha = ficha, CodigoLeido = codigo };
            Campo.Fichas[animal] = ficha;
            return animal;
        }
    }

    // ------------------------------------------------------------------ estado visible (RF-9, RF-11, RF-13)

    [Fact]
    public async Task Al_entrar_sin_baston_elegido_el_cartel_dice_Elegi_el_baston_y_tocarlo_abre_la_lista()
    {
        var b = new Banco();

        await b.Vm.IniciarAsync();
        // El lector real, al conectar sin bastón recordado, queda así (el falso se conecta solo, por eso se pisa después).
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoElegido);

        Assert.Equal(EstadoBaston.SinBaston, b.Vm.EstadoBaston);
        Assert.Equal("Elegí el bastón", b.Vm.DetalleBaston);
        Assert.True(b.Vm.HayAvisoBaston);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(["ElegirBaston"], b.Navegacion.Destinos);
        Assert.Equal(0, b.Lector.Reconexiones);
    }

    [Fact]
    public async Task El_estado_sigue_al_lector_conectando_conectado_y_sin_baston()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();

        b.Lector.Poner(EstadoConexionRfid.Conectando);
        Assert.Equal(EstadoBaston.Buscando, b.Vm.EstadoBaston);
        Assert.Equal("Buscando el bastón", b.Vm.TextoBaston);
        Assert.True(b.Vm.HayAvisoBaston);

        b.Lector.Poner(EstadoConexionRfid.Conectado);
        Assert.Equal(EstadoBaston.Conectado, b.Vm.EstadoBaston);
        Assert.Equal("Conectado", b.Vm.TextoBaston);
        Assert.False(b.Vm.HayAvisoBaston);

        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
        Assert.Equal(EstadoBaston.SinBaston, b.Vm.EstadoBaston);
        Assert.Equal("Tocá para reconectar", b.Vm.TextoAccionBaston);
    }

    [Fact]
    public async Task Avisa_a_la_pantalla_cuando_cambia_el_estado()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        var cambios = new List<string?>();
        b.Vm.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.BluetoothApagado);

        Assert.Contains(nameof(TrabajoViewModel.EstadoBaston), cambios);
        Assert.Contains(nameof(TrabajoViewModel.TextoBaston), cambios);
        Assert.Contains(nameof(TrabajoViewModel.DetalleBaston), cambios);
        Assert.Contains(nameof(TrabajoViewModel.ColorFondoBaston), cambios);
        Assert.Contains(nameof(TrabajoViewModel.HayAvisoBaston), cambios);
    }

    // ------------------------------------------------------------------ el botón grande según el motivo (RF-5, RF-6, RF-12)

    [Fact]
    public async Task Tocar_con_el_baston_sin_responder_reintenta_de_inmediato()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(1, b.Lector.Reconexiones);
        Assert.Empty(b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Tocar_con_el_baston_desemparejado_lleva_a_elegir_otro()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoEmparejado);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(["ElegirBaston"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Tocar_sin_permiso_lo_pide_y_si_lo_dan_reintenta_sin_abrir_ajustes()
    {
        var b = new Banco();
        b.Bluetooth.Permisos = false;
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinPermiso);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(1, b.Bluetooth.PedidosDePermiso);
        Assert.Equal(0, b.Bluetooth.AjustesAppAbiertos);
        Assert.Equal(1, b.Lector.Reconexiones);
    }

    [Fact]
    public async Task Tocar_sin_permiso_y_con_el_permiso_denegado_otra_vez_abre_los_ajustes_de_la_app()
    {
        var b = new Banco();
        b.Bluetooth.Permisos = false;
        b.Bluetooth.ConcederAlPedir = false;
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinPermiso);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(1, b.Bluetooth.PedidosDePermiso);
        Assert.Equal(1, b.Bluetooth.AjustesAppAbiertos);
        Assert.Equal(0, b.Lector.Reconexiones);
    }

    [Fact]
    public async Task Tocar_con_el_Bluetooth_apagado_abre_los_ajustes_de_Bluetooth()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.BluetoothApagado);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(1, b.Bluetooth.AjustesBluetoothAbiertos);
        Assert.Equal(0, b.Lector.Reconexiones);
    }

    [Fact]
    public async Task Tocar_en_una_tablet_sin_Bluetooth_no_hace_nada()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinBluetooth);

        await b.Vm.ReconectarBastonCommand.ExecuteAsync(null);

        Assert.Equal(0, b.Lector.Reconexiones);
        Assert.Empty(b.Navegacion.Destinos);
        Assert.Equal(0, b.Bluetooth.AjustesBluetoothAbiertos);
    }

    // ------------------------------------------------------------------ cambiar de bastón y su nombre (RF-3)

    [Fact]
    public async Task El_nombre_del_baston_viene_de_las_preferencias_y_Cambiar_baston_abre_la_lista()
    {
        var b = new Banco();
        b.Baston.Actual = new BastonRecordado("XRS2 2949", "AA:00:00:00:00:02");

        await b.Vm.IniciarAsync();
        Assert.Equal("XRS2 2949", b.Vm.NombreBaston);

        await b.Vm.CambiarBastonCommand.ExecuteAsync(null);
        Assert.Equal(["ElegirBaston"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Sin_baston_elegido_el_nombre_dice_que_no_hay()
    {
        var b = new Banco();

        await b.Vm.IniciarAsync();

        Assert.Equal("Sin elegir", b.Vm.NombreBaston);
    }

    [Fact]
    public async Task Al_volver_de_elegir_otro_baston_el_nombre_se_actualiza()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.DetenerAsync();
        b.Baston.Guardar(new BastonRecordado("XRS2 1971", "AA:00:00:00:00:04"));   // lo que hace la pantalla de elegir

        await b.Vm.IniciarAsync();

        Assert.Equal("XRS2 1971", b.Vm.NombreBaston);
    }

    // ------------------------------------------------------------------ la lectura entra como la búsqueda a mano (RF-23)

    [Fact]
    public async Task Un_EID_leido_por_el_baston_abre_la_ficha_igual_que_buscarlo_a_mano()
    {
        var b = new Banco();
        var animal = b.Vaca(Eid);
        await b.Vm.IniciarAsync();

        b.Lector.Emitir(Eid);
        await Esperas.HastaAsync(() => b.Vm.Ficha is not null);

        Assert.Equal([Eid], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);

        // Lo mismo por la búsqueda a mano con otro banco: mismo código, mismo resultado.
        var mano = new Banco();
        var animalMano = mano.Vaca(Eid);
        mano.Vm.TextoBusqueda = Eid;
        await mano.Vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal(animalMano, mano.Vm.Ficha!.AnimalId);
        Assert.Equal(b.Campo.LecturasProcesadas[0].Codigo, mano.Campo.LecturasProcesadas[0].Codigo);
    }

    [Fact]
    public async Task Un_EID_desconocido_leido_por_el_baston_muestra_caravana_no_encontrada_con_el_codigo()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();

        b.Lector.Emitir("999000458999999");
        await Esperas.HastaAsync(() => b.Vm.Cuadro == CuadroTrabajo.NoEncontrada);

        Assert.Equal("999000458999999", b.Vm.CodigoNoEncontrado);
    }

    [Fact]
    public async Task Con_una_ficha_abierta_leer_otra_vaca_con_el_baston_se_ignora_y_avisa()
    {
        var b = new Banco();
        b.Vaca("1001", "1001");
        var otra = b.Vaca("999000458013170", "1002");
        await b.Vm.IniciarAsync();
        await b.Vm.EncolarLecturaAsync("1001");

        b.Lector.Emitir("999000458013170");
        await Esperas.HastaAsync(() => b.Vm.HayMensajeInfo);

        // Spec 009 (modificada 2026-11-25): la ficha abierta sigue siendo la primera, no se marcó la otra vaca y no hay aviso de tres opciones.
        Assert.NotEqual(otra, b.Vm.Ficha!.AnimalId);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(TrabajoViewModel.MensajeLecturaConFichaAbierta, b.Vm.MensajeInfo);
    }

    // ------------------------------------------------------------------ perder el bastón no toca nada (RF-14, RF-15)

    [Fact]
    public async Task Perder_el_baston_con_ficha_evento_y_observacion_cargados_no_cambia_nada_ni_abre_cuadros()
    {
        var b = new Banco();
        var animal = b.Vaca("1001");
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Conectado);
        await b.Vm.EncolarLecturaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);
        b.Vm.ObservacionPendiente = "Con cuidado";
        var ficha = b.Vm.Ficha;
        var registrosAntes = b.Campo.Rechazos.Count + b.Campo.Tactos.Count;

        b.Lector.Poner(EstadoConexionRfid.Conectando);
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);

        Assert.Same(ficha, b.Vm.Ficha);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal(Ok, b.Vm.EventoElegido);
        Assert.Equal("Con cuidado", b.Vm.ObservacionPendiente);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Null(b.Vm.MensajeError);
        Assert.Equal(registrosAntes, b.Campo.Rechazos.Count + b.Campo.Tactos.Count);   // el corte no guarda nada
    }

    [Fact]
    public async Task Sin_baston_se_puede_buscar_a_mano_y_guardar_con_Entrar()
    {
        var b = new Banco();
        var animal = b.Vaca("1001");
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
        b.Vm.TextoBusqueda = "1001";
        await b.Vm.BuscarCommand.ExecuteAsync(null);
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.Selector.ElegirCommand.ExecuteAsync(Ok);
        b.Vm.ObservacionPendiente = "Sin bastón";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var guardado = Assert.Single(b.Campo.Rechazos);
        Assert.Equal((animal, Ok.Id, "Sin bastón"), (guardado.AnimalId, guardado.EventoId, guardado.Observaciones));
    }

    // ------------------------------------------------------------------ ciclo de vida (RF-16)

    [Fact]
    public async Task Pausar_desconecta_el_lector_y_Reanudar_vuelve_a_conectar_de_inmediato()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await Esperas.HastaAsync(() => b.Lector.Conexiones == 1);

        await b.Vm.PausarLectorAsync();
        Assert.Equal(1, b.Lector.Desconexiones);

        await b.Vm.ReanudarLectorAsync();
        Assert.Equal(2, b.Lector.Conexiones);
    }

    [Fact]
    public async Task Reanudar_fuera_de_la_pantalla_de_trabajo_no_conecta()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.DetenerAsync();
        var conexiones = b.Lector.Conexiones;

        await b.Vm.ReanudarLectorAsync();   // la app vuelve de segundo plano estando en otra pantalla

        Assert.Equal(conexiones, b.Lector.Conexiones);
    }

    [Fact]
    public async Task Despues_de_Detener_el_ViewModel_ya_no_escucha_al_lector()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Lector.Poner(EstadoConexionRfid.Conectado);
        await b.Vm.DetenerAsync();
        var cambios = new List<string?>();
        b.Vm.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        b.Lector.Poner(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);

        Assert.Empty(cambios);
        Assert.Equal(EstadoBaston.Conectado, b.Vm.EstadoBaston);
    }

    [Fact]
    public async Task Volver_a_entrar_no_suscribe_dos_veces()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.DetenerAsync();
        await b.Vm.IniciarAsync();
        var cambios = 0;
        b.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TrabajoViewModel.TextoBaston)) cambios++; };

        b.Lector.Poner(EstadoConexionRfid.Conectando);

        Assert.Equal(1, cambios);
    }
}
