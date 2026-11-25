using System.Threading.Channels;
using Aviss.Application.Puertos;
using Aviss.Rfid.Diagnostico;

namespace Aviss.Rfid.Tests.Soporte;

// Falsos del lector del bastón (spec 008). Ninguno espera de verdad: el temporizador y el reloj los mueve el test a mano.

/// <summary>Temporizador controlado a mano: cada espera queda pendiente hasta que el test la libera o se cancela.</summary>
public sealed class FakeTemporizador : ITemporizador
{
    private readonly object _candado = new();
    private readonly List<(TimeSpan Tiempo, TaskCompletionSource Fin)> _esperas = [];
    private readonly List<TimeSpan> _pedidas = [];

    public Task EsperarAsync(TimeSpan tiempo, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_candado)
        {
            _esperas.Add((tiempo, tcs));
            _pedidas.Add(tiempo);
        }
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    /// <summary>Todas las esperas que se pidieron, en orden (incluidas las ya terminadas).</summary>
    public IReadOnlyList<TimeSpan> Pedidas { get { lock (_candado) return [.. _pedidas]; } }

    public int Pendientes(TimeSpan tiempo)
    {
        lock (_candado) return _esperas.Count(e => e.Tiempo == tiempo && !e.Fin.Task.IsCompleted);
    }

    public int PendientesTotal
    {
        get { lock (_candado) return _esperas.Count(e => !e.Fin.Task.IsCompleted); }
    }

    /// <summary>Termina la espera pendiente más antigua de ese tiempo.</summary>
    public bool Liberar(TimeSpan tiempo)
    {
        TaskCompletionSource? tcs;
        lock (_candado) tcs = _esperas.FirstOrDefault(e => e.Tiempo == tiempo && !e.Fin.Task.IsCompleted).Fin;
        return tcs is not null && tcs.TrySetResult();
    }
}

public sealed class RelojManual(DateTime inicio)
{
    private DateTime _ahora = inicio;
    public DateTime Ahora() => _ahora;
    public void Avanzar(TimeSpan t) => _ahora += t;
}

public enum ModoEnlace { Conecta, Falla, NoResponde }

/// <summary>Enlace serie falso: según el modo de cada método conecta, falla o se queda colgado hasta que lo cancelan.</summary>
public sealed class FakeEnlaceSerie : IEnlaceSerieBaston
{
    private readonly object _candado = new();
    private readonly List<MetodoSocket> _llamadas = [];
    private readonly List<FakeConexionSerie> _conexiones = [];
    private int _cancelados;

    public ModoEnlace ModoInseguro { get; set; } = ModoEnlace.Conecta;
    public ModoEnlace ModoSeguro { get; set; } = ModoEnlace.Conecta;
    public Exception? ErrorAlAbrir { get; set; }

    public void PonerModo(ModoEnlace modo) => ModoInseguro = ModoSeguro = modo;

    public IReadOnlyList<MetodoSocket> Llamadas { get { lock (_candado) return [.. _llamadas]; } }
    public IReadOnlyList<FakeConexionSerie> Conexiones { get { lock (_candado) return [.. _conexiones]; } }
    public int Cancelados => Volatile.Read(ref _cancelados);

    public Task<IConexionSerie> AbrirAsync(string direccion, MetodoSocket metodo, CancellationToken ct)
    {
        lock (_candado) _llamadas.Add(metodo);
        if (ErrorAlAbrir is not null) return Task.FromException<IConexionSerie>(ErrorAlAbrir);

        var modo = metodo == MetodoSocket.Inseguro ? ModoInseguro : ModoSeguro;
        switch (modo)
        {
            case ModoEnlace.Conecta:
                var conexion = new FakeConexionSerie();
                lock (_candado) _conexiones.Add(conexion);
                return Task.FromResult<IConexionSerie>(conexion);
            case ModoEnlace.Falla:
                return Task.FromException<IConexionSerie>(new IOException("falla simulada"));
            default:
                var tcs = new TaskCompletionSource<IConexionSerie>(TaskCreationOptions.RunContinuationsAsynchronously);
                ct.Register(() =>
                {
                    if (tcs.TrySetCanceled(ct)) Interlocked.Increment(ref _cancelados);
                });
                return tcs.Task;
        }
    }
}

/// <summary>Una conexión falsa: el test "manda paquetes" con Enviar y "apaga el bastón" con Cortar.</summary>
public sealed class FakeConexionSerie : IConexionSerie
{
    private readonly Channel<byte[]> _paquetes = Channel.CreateUnbounded<byte[]>();
    private byte[] _resto = [];
    private int _cerrada;

    public bool Cerrada => Volatile.Read(ref _cerrada) == 1;

    public void Enviar(params byte[] paquete) => _paquetes.Writer.TryWrite(paquete);
    public void Enviar(string texto) => Enviar(System.Text.Encoding.ASCII.GetBytes(texto));

    /// <summary>El bastón se apagó o se alejó: la lectura devuelve 0.</summary>
    public void Cortar() => _paquetes.Writer.TryComplete();

    public async ValueTask<int> LeerAsync(Memory<byte> destino, CancellationToken ct)
    {
        if (_resto.Length == 0)
        {
            if (!await _paquetes.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return 0;
            if (!_paquetes.Reader.TryRead(out var paquete)) return 0;
            _resto = paquete;
        }
        var n = Math.Min(destino.Length, _resto.Length);
        _resto.AsMemory(0, n).CopyTo(destino);
        _resto = _resto[n..];
        return n;
    }

    public ValueTask CerrarAsync()
    {
        Interlocked.Exchange(ref _cerrada, 1);
        _paquetes.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeBluetoothBaston : IBluetoothBaston
{
    public bool Hay { get; set; } = true;
    public bool Encendido { get; set; } = true;
    public bool Permisos { get; set; } = true;
    public bool Emparejado { get; set; } = true;
    /// <summary>Lo que contesta el usuario al diálogo del permiso.</summary>
    public bool ConcederAlPedir { get; set; } = true;
    public int PedidosDePermiso { get; private set; }
    public List<DispositivoBluetooth> Dispositivos { get; } = [];

    public Task<bool> PedirPermisosAsync()
    {
        PedidosDePermiso++;
        if (ConcederAlPedir) Permisos = true;
        return Task.FromResult(Permisos);
    }

    public bool TienePermisos() => Permisos;
    public bool HayBluetooth() => Hay;
    public bool EstaEncendido() => Encendido;
    public IReadOnlyList<DispositivoBluetooth> ListarEmparejados() => Dispositivos;
    public bool EstaEmparejado(string direccion) => Emparejado;
    public void AbrirAjustesBluetooth() { }
    public void AbrirAjustesDeLaApp() { }
}

public sealed class FakePreferenciasBaston : IPreferenciasBaston
{
    public BastonRecordado? Actual { get; set; }
    public BastonRecordado? Obtener() => Actual;
    public void Guardar(BastonRecordado baston) => Actual = baston;
}

public sealed class FakeAvisos : IAvisosCampo
{
    private int _perdidos;
    public int BastonesPerdidos => Volatile.Read(ref _perdidos);
    public void Confirmacion() { }
    public void Repetido() { }
    public void SinTacto() { }
    public void MangaCompleta() { }
    public void BastonPerdido() => Interlocked.Increment(ref _perdidos);
}

public static class Esperas
{
    public static async Task HastaAsync(Func<bool> condicion, int milisegundos = 5000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(milisegundos);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(2);
        }
    }
}
