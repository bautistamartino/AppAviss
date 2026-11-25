using Aviss.App.ViewModels;
using Aviss.Application.Puertos;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>Spec 008 (RF-1, RF-2, RF-5, RF-26): la lista de bastones emparejados, elegir y recordar.</summary>
public class ElegirBastonViewModelTests
{
    private sealed class Banco
    {
        public FakeBluetoothBaston Bluetooth { get; } = new();
        public FakePreferenciasBaston Preferencias { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public ElegirBastonViewModel Vm { get; }

        public Banco() => Vm = new ElegirBastonViewModel(Bluetooth, Preferencias, Navegacion);
    }

    [Fact]
    public async Task Lista_los_emparejados_con_los_XRS_primero_y_el_resto_alfabetico()
    {
        var b = new Banco();
        b.Bluetooth.Con("Parlante", "AA:00:00:00:00:01").Con("XRS2 2949", "AA:00:00:00:00:02")
            .Con("Auriculares", "AA:00:00:00:00:03").Con("XRS2 1971", "AA:00:00:00:00:04").Con("xrs2i 6769", "AA:00:00:00:00:05");

        await b.Vm.CargarAsync();

        Assert.Equal(["XRS2 1971", "XRS2 2949", "xrs2i 6769", "Auriculares", "Parlante"], b.Vm.Bastones.Select(x => x.Nombre));
        Assert.False(b.Vm.HayMensaje);
    }

    [Fact]
    public async Task Elegir_guarda_nombre_y_direccion_y_vuelve_a_la_pantalla_de_trabajo()
    {
        var b = new Banco();
        b.Bluetooth.Con("XRS2 2949", "AA:00:00:00:00:02");
        await b.Vm.CargarAsync();

        await b.Vm.ElegirCommand.ExecuteAsync(b.Vm.Bastones[0]);

        Assert.Equal(new BastonRecordado("XRS2 2949", "AA:00:00:00:00:02"), b.Preferencias.Actual);
        Assert.Equal(["Volver"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task No_elige_solo_aunque_haya_un_unico_XRS()
    {
        var b = new Banco();
        b.Bluetooth.Con("XRS2 2949", "AA:00:00:00:00:02");

        await b.Vm.CargarAsync();

        Assert.Null(b.Preferencias.Actual);
        Assert.Equal(0, b.Preferencias.Guardados);
        Assert.Empty(b.Navegacion.Destinos);
    }

    [Fact]
    public async Task El_bastón_actual_aparece_marcado_como_elegido()
    {
        var b = new Banco();
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04").Con("XRS2 2949", "AA:00:00:00:00:02");
        b.Preferencias.Actual = new BastonRecordado("XRS2 2949", "AA:00:00:00:00:02");

        await b.Vm.CargarAsync();

        Assert.Equal(["XRS2 2949"], b.Vm.Bastones.Where(x => x.EsElegido).Select(x => x.Nombre));
    }

    [Fact]
    public async Task Si_falta_el_permiso_lo_pide_al_abrir_y_si_lo_dan_lista_los_bastones()
    {
        var b = new Banco();
        b.Bluetooth.Permisos = false;
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04");

        await b.Vm.CargarAsync();

        Assert.Equal(1, b.Bluetooth.PedidosDePermiso);
        Assert.Single(b.Vm.Bastones);
    }

    [Fact]
    public async Task Sin_permiso_muestra_el_motivo_no_lista_nada_y_ofrece_los_ajustes_de_la_app()
    {
        var b = new Banco();
        b.Bluetooth.Permisos = false;
        b.Bluetooth.ConcederAlPedir = false;
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04");

        await b.Vm.CargarAsync();

        Assert.Empty(b.Vm.Bastones);
        Assert.Equal("Falta el permiso de Bluetooth", b.Vm.Mensaje);
        Assert.True(b.Vm.HayMensaje);
        Assert.True(b.Vm.HayAjustesApp);
        b.Vm.AbrirAjustesAppCommand.Execute(null);
        Assert.Equal(1, b.Bluetooth.AjustesAppAbiertos);
    }

    [Fact]
    public async Task Con_el_permiso_ya_dado_no_vuelve_a_pedirlo()
    {
        var b = new Banco();

        await b.Vm.CargarAsync();

        Assert.Equal(0, b.Bluetooth.PedidosDePermiso);
    }

    [Fact]
    public async Task Sin_Bluetooth_en_la_tablet_lo_dice_y_no_lista()
    {
        var b = new Banco();
        b.Bluetooth.Hay = false;

        await b.Vm.CargarAsync();

        Assert.Equal("Esta tablet no tiene Bluetooth", b.Vm.Mensaje);
        Assert.Empty(b.Vm.Bastones);
    }

    [Fact]
    public async Task Con_el_Bluetooth_apagado_avisa_pero_lista_los_emparejados_y_ofrece_los_ajustes()
    {
        var b = new Banco();
        b.Bluetooth.Encendido = false;
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04");

        await b.Vm.CargarAsync();

        Assert.Equal("Prendé el Bluetooth de la tablet", b.Vm.Mensaje);
        Assert.Single(b.Vm.Bastones);
    }

    [Fact]
    public async Task Sin_ningun_emparejado_explica_como_emparejar_uno()
    {
        var b = new Banco();

        await b.Vm.CargarAsync();

        Assert.Empty(b.Vm.Bastones);
        Assert.Contains("emparej", b.Vm.Mensaje);
    }

    [Fact]
    public async Task Actualizar_vuelve_a_leer_la_lista()
    {
        var b = new Banco();
        await b.Vm.CargarAsync();
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04");

        await b.Vm.ActualizarCommand.ExecuteAsync(null);

        Assert.Single(b.Vm.Bastones);
        Assert.False(b.Vm.HayMensaje);
    }

    [Fact]
    public async Task Los_ajustes_de_Bluetooth_y_volver_van_a_su_lugar()
    {
        var b = new Banco();

        b.Vm.AbrirAjustesBluetoothCommand.Execute(null);
        await b.Vm.VolverCommand.ExecuteAsync(null);

        Assert.Equal(1, b.Bluetooth.AjustesBluetoothAbiertos);
        Assert.Equal(["Volver"], b.Navegacion.Destinos);
        Assert.Null(b.Preferencias.Actual);
    }

    [Fact]
    public async Task Los_mensajes_no_llevan_nombres_ni_direcciones_de_bastones()
    {
        var b = new Banco();
        b.Bluetooth.Con("XRS2 1971", "AA:00:00:00:00:04");
        b.Bluetooth.Permisos = false;
        b.Bluetooth.ConcederAlPedir = false;

        await b.Vm.CargarAsync();

        Assert.DoesNotContain("XRS2", b.Vm.Mensaje);
        Assert.DoesNotContain("AA:00", b.Vm.Mensaje);
    }
}
