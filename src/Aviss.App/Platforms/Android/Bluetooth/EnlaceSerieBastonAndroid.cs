using Android.Bluetooth;
using Aviss.Application.Puertos;
using Aviss.Rfid;

namespace Aviss.App.Bluetooth;

/// <summary>
/// Enlace SPP (RFCOMM) con el bastón (spec 008, RF-7 y RF-8). Abre el socket inseguro o el seguro con el UUID del perfil de
/// puerto serie. <c>Connect()</c> bloquea hasta 11 o 60 s y no admite cancelación: el intento corre en un hilo de fondo y
/// cancelar el token <b>cierra el socket</b>, que hace fallar el <c>Connect()</c> (así el lector corta a los 8 s).
/// Sin reflexión ni el truco del canal 1: no hizo falta en la prueba.
/// </summary>
public sealed class EnlaceSerieBastonAndroid : IEnlaceSerieBaston
{
    // UUID estándar del perfil de puerto serie (SPP).
    private static readonly Java.Util.UUID UuidSpp = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    public async Task<IConexionSerie> AbrirAsync(string direccion, MetodoSocket metodo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dispositivo = BluetoothBastonAndroid.Buscar(direccion) ?? throw new ErrorEnlace(MotivoSinBaston.NoEmparejado);

        BluetoothSocket? socket = null;
        try
        {
            socket = metodo == MetodoSocket.Inseguro
                ? dispositivo.CreateInsecureRfcommSocketToServiceRecord(UuidSpp)
                : dispositivo.CreateRfcommSocketToServiceRecord(UuidSpp);
            if (socket is null) throw new ErrorEnlace(MotivoSinBaston.NoResponde);

            var elegido = socket;
            // Cancelar cierra el socket: es la única forma de sacar a Connect() de la espera.
            using var registro = ct.Register(() => Cerrar(elegido));
            await Task.Run(() => elegido.Connect(), CancellationToken.None).ConfigureAwait(false);

            // Si se canceló justo cuando conectaba, no se entrega una conexión que nadie va a cerrar.
            ct.ThrowIfCancellationRequested();
            return new ConexionSerieAndroid(elegido);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ErrorEnlace)
        {
            Cerrar(socket);
            throw;
        }
        catch (Exception)
        {
            Cerrar(socket);
            // Si fue una cancelación, el cierre del socket produce una IOException: se informa como cancelación.
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            // Sin datos del bastón en el mensaje (RF-26): el lector solo necesita saber que no respondió.
            throw new ErrorEnlace(MotivoSinBaston.NoResponde);
        }
    }

    internal static void Cerrar(BluetoothSocket? socket)
    {
        try { socket?.Close(); }
        catch (Exception) { /* ya estaba cerrado */ }
    }

    private sealed class ConexionSerieAndroid(BluetoothSocket socket) : IConexionSerie
    {
        private readonly Stream? _entrada = socket.InputStream;
        private int _cerrado;

        public async ValueTask<int> LeerAsync(Memory<byte> destino, CancellationToken ct)
        {
            if (_entrada is null) return -1;
            // InputStream.Read no admite cancelación: cancelar cierra el socket y la lectura bloqueada falla.
            using var registro = ct.Register(() => _ = CerrarAsync());
            var buffer = new byte[Math.Min(destino.Length, 1024)];
            try
            {
                var n = await Task.Run(() => _entrada.Read(buffer, 0, buffer.Length), CancellationToken.None).ConfigureAwait(false);
                if (n > 0) buffer.AsMemory(0, n).CopyTo(destino);
                return n;
            }
            catch (Exception)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                return -1;   // el socket se cortó: para el lector es lo mismo que "el bastón cerró"
            }
        }

        public ValueTask CerrarAsync()
        {
            // Idempotente: solo la primera llamada cierra.
            if (Interlocked.Exchange(ref _cerrado, 1) == 0) Cerrar(socket);
            return ValueTask.CompletedTask;
        }
    }
}
