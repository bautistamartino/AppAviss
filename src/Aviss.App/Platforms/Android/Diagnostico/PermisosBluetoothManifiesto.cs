// Los permisos de Bluetooth se declaran en el manifiesto SOLO en la compilación de desarrollo: la versión que se distribuye
// no los pide (el spike de diagnóstico no existe ahí). El manifiesto de la app no pide INTERNET (constitución §1).
// Los atributos de ensamblado van en su propio archivo, antes de cualquier namespace.
#if DEBUG
using Android.App;

[assembly: UsesPermission(Android.Manifest.Permission.BluetoothConnect)]
// neverForLocation: la app no deduce la ubicación de los dispositivos que ve (y así Android 12+ no exige ubicación).
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothScan, UsesPermissionFlags = "neverForLocation")]
// Versiones anteriores a Android 12: los permisos viejos, sin ubicación (solo se listan los ya emparejados, no se busca).
[assembly: UsesPermission(Android.Manifest.Permission.Bluetooth, MaxSdkVersion = 30)]
[assembly: UsesPermission(Android.Manifest.Permission.BluetoothAdmin, MaxSdkVersion = 30)]
#endif
