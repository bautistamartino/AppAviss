using System.Collections.ObjectModel;
using Aviss.Application.Puertos;
using Aviss.Rfid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Un bastón (dispositivo emparejado) de la lista. <see cref="EsElegido"/> es el que la tablet recuerda hoy.</summary>
public sealed record OpcionBaston(string Nombre, string Direccion, bool EsElegido);

/// <summary>
/// Pantalla "Elegir el bastón" (spec 008, RF-1, RF-2, RF-5, RF-26): lista los dispositivos Bluetooth ya emparejados (los XRS
/// primero), recuerda el que se toca y vuelve. No elige solo, aunque haya uno solo. No busca dispositivos nuevos: para
/// emparejar uno se abren los ajustes de Bluetooth de Android. Sin MAUI, así se prueba con xUnit.
/// </summary>
public sealed partial class ElegirBastonViewModel : ObservableObject
{
    // Textos fijos: nunca llevan nombres ni direcciones de bastones (RF-26).
    public const string MensajeSinPermiso = "Falta el permiso de Bluetooth";
    public const string MensajeSinBluetooth = "Esta tablet no tiene Bluetooth";
    public const string MensajeBluetoothApagado = "Prendé el Bluetooth de la tablet";
    public const string MensajeNadaEmparejado = "No hay ningún bastón emparejado. Emparejalo desde los ajustes de Bluetooth.";

    private readonly IBluetoothBaston _bluetooth;
    private readonly IPreferenciasBaston _preferencias;
    private readonly INavegacion _navegacion;

    public ElegirBastonViewModel(IBluetoothBaston bluetooth, IPreferenciasBaston preferencias, INavegacion navegacion)
        => (_bluetooth, _preferencias, _navegacion) = (bluetooth, preferencias, navegacion);

    public ObservableCollection<OpcionBaston> Bastones { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensaje))]
    private string? _mensaje;

    public bool HayMensaje => !string.IsNullOrEmpty(Mensaje);

    /// <summary>Con el permiso denegado se ofrece abrir los ajustes de la app ("Permisos > Dispositivos cercanos").</summary>
    [ObservableProperty]
    private bool _hayAjustesApp;

    /// <summary>La página lo llama al aparecer: pide el permiso si falta (RF-5) y arma la lista.</summary>
    public async Task CargarAsync()
    {
        Bastones.Clear();
        Mensaje = null;
        HayAjustesApp = false;

        if (!_bluetooth.HayBluetooth())
        {
            Mensaje = MensajeSinBluetooth;
            return;
        }

        if (!_bluetooth.TienePermisos() && !await _bluetooth.PedirPermisosAsync())
        {
            Mensaje = MensajeSinPermiso;
            HayAjustesApp = true;
            return;
        }

        var actual = _preferencias.Obtener();
        // Los XRS primero; dentro de cada grupo, por nombre. Sin lista corta ni elección automática: el responsable decide.
        var ordenados = _bluetooth.ListarEmparejados()
            .OrderBy(d => EsXrs(d.Titulo) ? 0 : 1)
            .ThenBy(d => d.Titulo, StringComparer.OrdinalIgnoreCase);
        foreach (var d in ordenados)
            Bastones.Add(new OpcionBaston(d.Titulo, d.Direccion, actual is not null && actual.Direccion == d.Direccion));

        if (!_bluetooth.EstaEncendido()) Mensaje = MensajeBluetoothApagado;
        else if (Bastones.Count == 0) Mensaje = MensajeNadaEmparejado;
    }

    private static bool EsXrs(string nombre) => nombre.Contains("XRS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Recuerda el bastón tocado y vuelve a la pantalla de trabajo, que conecta con el nuevo.</summary>
    [RelayCommand]
    private async Task ElegirAsync(OpcionBaston? opcion)
    {
        if (opcion is null) return;
        _preferencias.Guardar(new BastonRecordado(opcion.Nombre, opcion.Direccion));
        await _navegacion.VolverAsync();
    }

    [RelayCommand]
    private Task ActualizarAsync() => CargarAsync();

    [RelayCommand]
    private void AbrirAjustesBluetooth() => _bluetooth.AbrirAjustesBluetooth();

    [RelayCommand]
    private void AbrirAjustesApp() => _bluetooth.AbrirAjustesDeLaApp();

    [RelayCommand]
    private Task VolverAsync() => _navegacion.VolverAsync();
}
