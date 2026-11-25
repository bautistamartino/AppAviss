// Permisos de Bluetooth del manifiesto. Los atributos de ensamblado van en su propio archivo, antes de cualquier namespace.
// La app no pide INTERNET (constitución §1) ni ubicación.
using Android.App;

// En TODAS las compilaciones (spec 008, RF-5 y RF-26): lo único que necesita el lector del bastón, que solo usa dispositivos
// ya emparejados. En Android 12 o más es BLUETOOTH_CONNECT (se pide en ejecución); antes, BLUETOOTH (permiso normal).
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothConnect)]
[assembly: UsesPermission(Android.Manifest.Permission.Bluetooth, MaxSdkVersion = 30)]

#if DEBUG
// Solo para el spike de diagnóstico (que busca dispositivos): no existe en la versión que se distribuye.
// neverForLocation: la app no deduce la ubicación de los dispositivos que ve (y así Android 12+ no exige ubicación).
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothScan, UsesPermissionFlags = "neverForLocation")]
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothAdmin, MaxSdkVersion = 30)]
#endif
