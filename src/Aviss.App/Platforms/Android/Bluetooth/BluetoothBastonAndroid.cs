using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Aviss.Rfid;
using Aviss.Rfid.Diagnostico;

namespace Aviss.App.Bluetooth;

/// <summary>
/// Permiso de Bluetooth de producción (spec 008, RF-5 y RF-26): solo la conexión. En Android 12 o más es BLUETOOTH_CONNECT
/// (se pide en ejecución); antes, BLUETOOTH, que es un permiso normal (se concede al instalar). No pide SCAN ni ubicación
/// porque la app no busca dispositivos: solo usa los ya emparejados.
/// </summary>
public sealed class PermisoBaston : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? [(Android.Manifest.Permission.BluetoothConnect, true)]
            : [(Android.Manifest.Permission.Bluetooth, false)];
}

/// <summary>Lo propio de Android para el lector del bastón: permiso, estado del Bluetooth, emparejados y ajustes.</summary>
public sealed class BluetoothBastonAndroid : IBluetoothBaston
{
    private static BluetoothAdapter? Adaptador()
        => (Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    public async Task<bool> PedirPermisosAsync()
    {
        if (TienePermisos()) return true;

        // El diálogo de permisos de MAUI se abre en el hilo principal.
        var estado = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var actual = await Permissions.CheckStatusAsync<PermisoBaston>();
            return actual == PermissionStatus.Granted ? actual : await Permissions.RequestAsync<PermisoBaston>();
        });
        return estado == PermissionStatus.Granted;
    }

    public bool TienePermisos()
    {
        // Antes de Android 12 el permiso es normal y viene concedido con la instalación.
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return true;
        return Android.App.Application.Context.CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) == Permission.Granted;
    }

    public bool HayBluetooth() => Adaptador() is not null;

    public bool EstaEncendido()
    {
        try { return Adaptador()?.IsEnabled ?? false; }
        catch (Exception) { return false; }
    }

    public IReadOnlyList<DispositivoBluetooth> ListarEmparejados()
    {
        var lista = new List<DispositivoBluetooth>();
        // Sin el permiso, Android lanza SecurityException al pedir los emparejados.
        if (!TienePermisos()) return lista;

        try
        {
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
                lista.Add(new DispositivoBluetooth(d.Name ?? "", d.Address ?? "", tipo, []));
            }
        }
        catch (Exception)
        {
            // Bluetooth apagado o permiso retirado justo ahora: se devuelve lo que haya.
        }
        return lista.OrderBy(d => d.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool EstaEmparejado(string direccion) => Buscar(direccion) is not null;

    public void AbrirAjustesBluetooth() => Abrir(new Intent(Settings.ActionBluetoothSettings));

    public void AbrirAjustesDeLaApp()
    {
        var intent = new Intent(Settings.ActionApplicationDetailsSettings);
        intent.SetData(Android.Net.Uri.FromParts("package", Android.App.Application.Context.PackageName, null));
        Abrir(intent);
    }

    /// <summary>Busca el dispositivo emparejado por su dirección (la dirección es lo que identifica al bastón).</summary>
    internal static BluetoothDevice? Buscar(string direccion)
    {
        try
        {
            return Adaptador()?.BondedDevices?.FirstOrDefault(d => string.Equals(d.Address, direccion, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Abrir(Intent intent)
    {
        intent.AddFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
    }
}
