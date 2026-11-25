using Android.Bluetooth;
using Android.Content;
using Android.Provider;
using Aviss.Rfid.Diagnostico;

namespace Aviss.App.Diagnostico;

/// <summary>
/// Permiso de Bluetooth para <c>Permissions.RequestAsync</c> de MAUI: en Android 12 o más pide BLUETOOTH_CONNECT y BLUETOOTH_SCAN;
/// antes, BLUETOOTH y BLUETOOTH_ADMIN (permisos "normales": se conceden al instalar, no se preguntan).
/// </summary>
public sealed class PermisoBluetooth : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? [(Android.Manifest.Permission.BluetoothConnect, true), (Android.Manifest.Permission.BluetoothScan, true)]
            : [(Android.Manifest.Permission.Bluetooth, false), (Android.Manifest.Permission.BluetoothAdmin, false)];
}

/// <summary>Lo propio de Android para el diagnóstico del bastón: permisos, dispositivos emparejados y ajustes.</summary>
public sealed class BluetoothDiagnosticoAndroid : IBluetoothDiagnostico
{
    private static BluetoothAdapter? Adaptador()
        => (Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    public async Task<EstadoPermisosBluetooth> PedirPermisosAsync()
    {
        if (Adaptador() is null) return EstadoPermisosBluetooth.BluetoothNoDisponible;

        // Los permisos se piden en ejecución y en el hilo principal (MAUI muestra el diálogo de Android).
        var estado = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var actual = await Permissions.CheckStatusAsync<PermisoBluetooth>();
            return actual == PermissionStatus.Granted ? actual : await Permissions.RequestAsync<PermisoBluetooth>();
        });
        return estado == PermissionStatus.Granted ? EstadoPermisosBluetooth.Concedidos : EstadoPermisosBluetooth.Denegados;
    }

    public IReadOnlyList<DispositivoBluetooth> ListarEmparejados()
    {
        var lista = new List<DispositivoBluetooth>();
        var emparejados = Adaptador()?.BondedDevices;
        if (emparejados is null) return lista;

        foreach (var d in emparejados)
        {
            var tipo = d.Type switch
            {
                BluetoothDeviceType.Classic => TipoDispositivoBluetooth.Clasico,
                BluetoothDeviceType.Le => TipoDispositivoBluetooth.Le,
                BluetoothDeviceType.Dual => TipoDispositivoBluetooth.Dual,
                _ => TipoDispositivoBluetooth.Desconocido,
            };
            // Los UUID que el dispositivo anuncia (en el clásico, los perfiles como SPP 0x1101). Puede venir vacío.
            var uuids = d.GetUuids()?.Select(u => u.ToString() ?? "").Where(u => u.Length > 0).ToList() ?? [];
            lista.Add(new DispositivoBluetooth(d.Name ?? "", d.Address ?? "", tipo, uuids));
        }
        return lista.OrderBy(d => d.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void AbrirAjustesBluetooth()
    {
        var intent = new Intent(Settings.ActionBluetoothSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
    }

    public ITransporteDiagnostico CrearTransporteSpp() => new TransporteSppAndroid();

    public ITransporteDiagnostico CrearTransporteBle() => new TransporteBleAndroid();

    /// <summary>Busca el dispositivo emparejado por su dirección (la dirección es lo que identifica al bastón).</summary>
    internal static BluetoothDevice? Buscar(string direccion)
        => Adaptador()?.BondedDevices?.FirstOrDefault(d => string.Equals(d.Address, direccion, StringComparison.OrdinalIgnoreCase));

    internal static void CancelarBusqueda()
    {
        try { Adaptador()?.CancelDiscovery(); }
        catch (Exception) { /* sin permiso o sin Bluetooth: no hay búsqueda que cancelar */ }
    }
}
