using Android.Bluetooth;
using Aviss.Rfid.Diagnostico;

namespace Aviss.App.Diagnostico;

/// <summary>
/// Primera ruta de diagnóstico: Bluetooth clásico SPP (RFCOMM). Prueba, en orden, el socket "seguro", el "inseguro" y el canal 1
/// por reflexión (truco conocido para dispositivos que no anuncian bien el servicio), con un reintento cada uno, y deja cada intento
/// y su motivo de falla en el registro. Después lee el flujo de bytes en segundo plano hasta que se corte o se desconecte.
/// </summary>
public sealed class TransporteSppAndroid : ITransporteDiagnostico
{
    // UUID estándar del perfil de puerto serie (SPP).
    private static readonly Java.Util.UUID UuidSpp = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
    private const int IntentosPorMetodo = 2;

    private BluetoothSocket? _socket;
    private CancellationTokenSource? _cts;
    private volatile bool _cerrando;

    public string Nombre => "SPP";
    public EstadoTransporte Estado { get; private set; } = EstadoTransporte.Desconectado;

    public event Action<CambioEstadoTransporte>? EstadoCambiado;
    public event Action<string, byte[]>? DatosRecibidos;
    public event Action<string>? Informacion;

    public async Task ConectarAsync(DispositivoBluetooth dispositivo, CancellationToken ct = default)
    {
        await DesconectarAsync();
        _cerrando = false;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        Cambiar(EstadoTransporte.Conectando);

        try
        {
            var conectado = await Task.Run(() => ConectarSocket(dispositivo, token), token);
            if (conectado is null)
            {
                Cambiar(EstadoTransporte.Error, "No se pudo abrir el canal serie con ningún método (ver el detalle de cada intento más abajo).");
                return;
            }

            _socket = conectado;
            Cambiar(EstadoTransporte.Conectado, "Canal serie abierto. Esperando datos del bastón.");
            // La lectura corre sola en segundo plano; ConectarAsync vuelve apenas hay conexión.
            _ = Task.Run(() => Leer(conectado));
        }
        catch (OperationCanceledException)
        {
            Cambiar(EstadoTransporte.Desconectado, "Conexión cancelada.");
        }
        catch (Exception ex)
        {
            Cambiar(EstadoTransporte.Error, Motivo(ex));
        }
    }

    public Task DesconectarAsync()
    {
        _cerrando = true;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        CerrarSocket(_socket);
        _socket = null;
        if (Estado != EstadoTransporte.Desconectado) Cambiar(EstadoTransporte.Desconectado);
        return Task.CompletedTask;
    }

    private BluetoothSocket? ConectarSocket(DispositivoBluetooth dispositivo, CancellationToken ct)
    {
        var device = BluetoothDiagnosticoAndroid.Buscar(dispositivo.Direccion);
        if (device is null)
        {
            Informacion?.Invoke("El dispositivo ya no figura entre los emparejados.");
            return null;
        }

        // Con una búsqueda de dispositivos en curso, la conexión se vuelve lenta o falla.
        BluetoothDiagnosticoAndroid.CancelarBusqueda();

        var metodos = new (string Nombre, Func<BluetoothSocket?> Crear)[]
        {
            ("seguro (CreateRfcommSocketToServiceRecord)", () => device.CreateRfcommSocketToServiceRecord(UuidSpp)),
            ("inseguro (CreateInsecureRfcommSocketToServiceRecord)", () => device.CreateInsecureRfcommSocketToServiceRecord(UuidSpp)),
            ("canal 1 por reflexion (createRfcommSocket)", () => CrearPorReflexion(device)),
        };

        foreach (var (nombre, crear) in metodos)
        {
            for (var intento = 1; intento <= IntentosPorMetodo; intento++)
            {
                ct.ThrowIfCancellationRequested();
                BluetoothSocket? socket = null;
                try
                {
                    socket = crear();
                    if (socket is null) { Informacion?.Invoke($"Metodo {nombre}: no se pudo crear el socket."); break; }

                    Informacion?.Invoke($"Intento {intento}/{IntentosPorMetodo} con socket {nombre}...");
                    // Connect() bloquea; para poder cancelar, cancelar cierra el socket y eso lo hace fallar.
                    using var registro = ct.Register(() => CerrarSocket(socket));
                    socket.Connect();
                    Informacion?.Invoke($"Conectado con socket {nombre}.");
                    return socket;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Informacion?.Invoke($"Fallo el intento {intento} con socket {nombre}: {Motivo(ex)}");
                    CerrarSocket(socket);
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    Thread.Sleep(600);   // un respiro antes de reintentar
                }
            }
        }
        return null;
    }

    private static BluetoothSocket? CrearPorReflexion(BluetoothDevice device)
    {
        var clase = device.Class;
        var metodo = clase?.GetMethod("createRfcommSocket", Java.Lang.Integer.Type!);
        var resultado = metodo?.Invoke(device, new Java.Lang.Integer(1));
        return Java.Interop.JavaObjectExtensions.JavaCast<BluetoothSocket>(resultado);
    }

    private void Leer(BluetoothSocket socket)
    {
        var buffer = new byte[1024];
        try
        {
            var entrada = socket.InputStream ?? throw new IOException("El socket no tiene flujo de entrada.");
            while (!_cerrando)
            {
                var n = entrada.Read(buffer, 0, buffer.Length);
                if (n < 0) break;   // el bastón cerró el canal
                if (n == 0) continue;
                DatosRecibidos?.Invoke(Nombre, buffer.AsSpan(0, n).ToArray());
            }
            if (!_cerrando) Cambiar(EstadoTransporte.Desconectado, "El bastón cerró la conexión.");
        }
        catch (Exception ex)
        {
            // Si se pidió desconectar, cerrar el socket hace fallar la lectura: no es un error.
            if (!_cerrando) Cambiar(EstadoTransporte.Error, "Se perdio la conexion: " + Motivo(ex));
        }
        finally
        {
            CerrarSocket(socket);
        }
    }

    private static void CerrarSocket(BluetoothSocket? socket)
    {
        try { socket?.Close(); }
        catch (Exception) { /* ya estaba cerrado */ }
    }

    private static string Motivo(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    private void Cambiar(EstadoTransporte estado, string? detalle = null)
    {
        Estado = estado;
        EstadoCambiado?.Invoke(new CambioEstadoTransporte(estado, detalle));
    }
}
