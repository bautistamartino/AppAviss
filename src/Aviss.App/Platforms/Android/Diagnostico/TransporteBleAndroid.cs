using Android.Bluetooth;
using Android.Content;
using Aviss.Rfid.Diagnostico;

namespace Aviss.App.Diagnostico;

/// <summary>
/// Segunda ruta de diagnóstico: BLE por GATT. Se conecta al dispositivo emparejado, lista servicios y características (con sus
/// propiedades) en el registro y se suscribe a las notificaciones o indicaciones de todas las que lo permitan. Lo que llegue se
/// entrega como bytes, con la característica como origen.
/// </summary>
public sealed class TransporteBleAndroid : ITransporteDiagnostico
{
    // Descriptor estándar que activa las notificaciones de una característica.
    private static readonly Java.Util.UUID Cccd = Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private BluetoothGatt? _gatt;
    private bool _cerrando;
    // Android solo admite UNA escritura de descriptor por vez: se encolan y se escribe la siguiente al confirmar la anterior.
    private readonly Queue<(BluetoothGattDescriptor Descriptor, byte[] Valor, string Nombre)> _pendientes = new();

    public string Nombre => "BLE";
    public EstadoTransporte Estado { get; private set; } = EstadoTransporte.Desconectado;

    public event Action<CambioEstadoTransporte>? EstadoCambiado;
    public event Action<string, byte[]>? DatosRecibidos;
    public event Action<string>? Informacion;

    public async Task ConectarAsync(DispositivoBluetooth dispositivo, CancellationToken ct = default)
    {
        await DesconectarAsync();
        _cerrando = false;
        Cambiar(EstadoTransporte.Conectando);

        var device = BluetoothDiagnosticoAndroid.Buscar(dispositivo.Direccion);
        if (device is null)
        {
            Cambiar(EstadoTransporte.Error, "El dispositivo ya no figura entre los emparejados.");
            return;
        }

        try
        {
            var contexto = Android.App.Application.Context;
            var callback = new Callback(this);
            // La conexión es asíncrona: el resultado llega a OnConnectionStateChange.
            _gatt = OperatingSystem.IsAndroidVersionAtLeast(23)
                ? device.ConnectGatt(contexto, false, callback, BluetoothTransports.Le)
                : device.ConnectGatt(contexto, false, callback);
            if (_gatt is null) Cambiar(EstadoTransporte.Error, "Android no devolvio una conexion GATT.");
        }
        catch (Exception ex)
        {
            Cambiar(EstadoTransporte.Error, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public Task DesconectarAsync()
    {
        _cerrando = true;
        var gatt = _gatt;
        _gatt = null;
        _pendientes.Clear();
        try { gatt?.Disconnect(); gatt?.Close(); }
        catch (Exception) { /* ya estaba cerrado */ }
        if (Estado != EstadoTransporte.Desconectado) Cambiar(EstadoTransporte.Desconectado);
        return Task.CompletedTask;
    }

    private void Cambiar(EstadoTransporte estado, string? detalle = null)
    {
        Estado = estado;
        EstadoCambiado?.Invoke(new CambioEstadoTransporte(estado, detalle));
    }

    // Se listan los servicios y se encolan las suscripciones.
    private void DescubrirYSuscribir(BluetoothGatt gatt)
    {
        var servicios = gatt.Services ?? [];
        Informacion?.Invoke($"Servicios GATT descubiertos: {servicios.Count}");
        foreach (var servicio in servicios)
        {
            Informacion?.Invoke($"  Servicio {servicio.Uuid}");
            foreach (var c in servicio.Characteristics ?? [])
            {
                var props = c.Properties;
                Informacion?.Invoke($"    Caracteristica {c.Uuid} propiedades={props}");

                var notifica = props.HasFlag(GattProperty.Notify);
                var indica = props.HasFlag(GattProperty.Indicate);
                if (!notifica && !indica) continue;

                if (!gatt.SetCharacteristicNotification(c, true))
                {
                    Informacion?.Invoke($"    No se pudo activar la notificacion de {c.Uuid}.");
                    continue;
                }
                var cccd = c.GetDescriptor(Cccd);
                if (cccd is null)
                {
                    Informacion?.Invoke($"    {c.Uuid} no tiene descriptor 2902; las notificaciones pueden no llegar.");
                    continue;
                }
                // Notificación (0x0001) o indicación (0x0002).
                _pendientes.Enqueue((cccd, notifica ? [0x01, 0x00] : [0x02, 0x00], c.Uuid?.ToString() ?? "?"));
            }
        }
        if (_pendientes.Count == 0)
            Informacion?.Invoke("Ninguna caracteristica admite notificaciones: este dispositivo probablemente no usa BLE para mandar lecturas.");
        EscribirSiguiente(gatt);
    }

    private void EscribirSiguiente(BluetoothGatt gatt)
    {
        if (_pendientes.Count == 0) return;
        var (descriptor, valor, nombre) = _pendientes.Dequeue();
#pragma warning disable CA1422 // Las versiones nuevas de Android traen otra firma; esta sigue funcionando y sirve para todas.
        descriptor.SetValue(valor);
        var ok = gatt.WriteDescriptor(descriptor);
#pragma warning restore CA1422
        Informacion?.Invoke($"Suscripcion a {nombre}: {(ok ? "pedida" : "rechazada por Android")}");
        if (!ok) EscribirSiguiente(gatt);
    }

    private sealed class Callback(TransporteBleAndroid padre) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt is null) return;
            if (newState == ProfileState.Connected && status == GattStatus.Success)
            {
                padre.Cambiar(EstadoTransporte.Conectado, "GATT conectado. Descubriendo servicios...");
                gatt.DiscoverServices();
            }
            else if (newState == ProfileState.Disconnected)
            {
                if (padre._cerrando) return;
                // Un estado de falla (status distinto de Success) es un error; sin falla, el bastón se desconectó.
                if (status != GattStatus.Success) padre.Cambiar(EstadoTransporte.Error, $"GATT desconectado con estado {status}.");
                else padre.Cambiar(EstadoTransporte.Desconectado, "El dispositivo cerro la conexion GATT.");
                try { gatt.Close(); } catch (Exception) { }
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt is null) return;
            if (status != GattStatus.Success)
            {
                padre.Cambiar(EstadoTransporte.Error, $"No se pudieron descubrir los servicios ({status}).");
                return;
            }
            padre.DescubrirYSuscribir(gatt);
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
        {
            padre.Informacion?.Invoke($"Suscripcion confirmada: {descriptor?.Characteristic?.Uuid} ({status})");
            if (gatt is not null) padre.EscribirSiguiente(gatt);
        }

        // Android 13 o más llama a esta versión (con el valor)...
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
            => padre.DatosRecibidos?.Invoke(characteristic.Uuid?.ToString() ?? "BLE", value);

        // ...y las anteriores, a esta.
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
#pragma warning disable CA1422
            var valor = characteristic?.GetValue();
#pragma warning restore CA1422
            if (valor is not null) padre.DatosRecibidos?.Invoke(characteristic!.Uuid?.ToString() ?? "BLE", valor);
        }
    }
}
