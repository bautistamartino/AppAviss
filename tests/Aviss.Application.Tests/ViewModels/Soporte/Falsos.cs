using System.Threading.Channels;
using Aviss.App.ViewModels;
using Aviss.Application.Puertos;

namespace Aviss.Application.Tests.ViewModels.Soporte;

// Falsos simples de los puertos de plataforma para probar los ViewModels de campo sin MAUI.

/// <summary>Lector de prueba: lo que el test emite lo recibe la pantalla (como el bastón).</summary>
public sealed class FakeLector : IRfidReader, ISimuladorLecturas
{
    private readonly Channel<TagRead> _canal = Channel.CreateUnbounded<TagRead>();
    public EstadoConexionRfid Estado { get; private set; }
    public MotivoSinBaston Motivo { get; private set; }
    public event Action? EstadoCambiado;
    public int Reconexiones { get; private set; }
    public int Conexiones { get; private set; }
    public int Desconexiones { get; private set; }
    public List<string> Emitidos { get; } = [];

    public void Emitir(string codigo)
    {
        Emitidos.Add(codigo);
        _canal.Writer.TryWrite(new TagRead(codigo, DateTime.UtcNow));
    }

    public Task ConectarAsync(CancellationToken ct = default)
    {
        Conexiones++;
        Estado = EstadoConexionRfid.Conectado;
        Motivo = MotivoSinBaston.Ninguno;
        EstadoCambiado?.Invoke();
        return Task.CompletedTask;
    }

    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Desconexiones++;
        Estado = EstadoConexionRfid.Desconectado;
        EstadoCambiado?.Invoke();
        return Task.CompletedTask;
    }

    public Task ReconectarAhoraAsync(CancellationToken ct = default)
    {
        Reconexiones++;
        return Task.CompletedTask;
    }

    /// <summary>Para los tests: deja el lector en el estado y motivo indicados y avisa del cambio.</summary>
    public void Poner(EstadoConexionRfid estado, MotivoSinBaston motivo = MotivoSinBaston.Ninguno)
    {
        Estado = estado;
        Motivo = motivo;
        EstadoCambiado?.Invoke();
    }

    public async IAsyncEnumerable<TagRead> Lecturas([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await _canal.Reader.WaitToReadAsync(ct)) yield break;
            }
            catch (OperationCanceledException) { yield break; }
            while (_canal.Reader.TryRead(out var tag)) yield return tag;
        }
    }
}

public sealed class FakePreferencias : IPreferenciasMangas
{
    // Control por manga: una preferencia por establecimiento. Si nunca se eligió, se devuelve el valor por defecto de la prueba.
    public Dictionary<Guid, bool> ControlPorManga { get; } = [];
    public bool? ControlPorMangaPorDefecto { get; set; }
    public List<(Guid EstablecimientoId, bool Valor)> ControlGuardado { get; } = [];
    public bool? ObtenerControlPorManga(Guid establecimientoId) => ControlPorManga.TryGetValue(establecimientoId, out var v) ? v : ControlPorMangaPorDefecto;
    public void GuardarControlPorManga(Guid establecimientoId, bool valor) { ControlPorManga[establecimientoId] = valor; ControlGuardado.Add((establecimientoId, valor)); }
}

public sealed class FakeAvisos : IAvisosCampo
{
    public int Confirmaciones { get; private set; }
    public int Repetidos { get; private set; }
    public int SinTactos { get; private set; }
    public int MangasCompletas { get; private set; }
    public int BastonesPerdidos { get; private set; }
    public void Confirmacion() => Confirmaciones++;
    public void Repetido() => Repetidos++;
    public void SinTacto() => SinTactos++;
    public void MangaCompleta() => MangasCompletas++;
    public void BastonPerdido() => BastonesPerdidos++;
}

public sealed class FakePantalla : IPantalla
{
    public bool? Encendida { get; private set; }
    public void MantenerEncendida(bool encendida) => Encendida = encendida;
}

public sealed class FakeNavegacion : INavegacion
{
    public List<string> Destinos { get; } = [];
    public Task IrAInicioAsync() { Destinos.Add("Inicio"); return Task.CompletedTask; }
    public Task IrAImportarAsync() { Destinos.Add("Importar"); return Task.CompletedTask; }
    public Task IrAExportarAsync() { Destinos.Add("Exportar"); return Task.CompletedTask; }
    public Task IrAEleccionTrabajoAsync() { Destinos.Add("Eleccion"); return Task.CompletedTask; }
    public Task IrATrabajoAsync() { Destinos.Add("Trabajo"); return Task.CompletedTask; }
    public Task IrAMovimientosAsync() { Destinos.Add("Movimientos"); return Task.CompletedTask; }
    public Task IrAElegirBastonAsync() { Destinos.Add("ElegirBaston"); return Task.CompletedTask; }
    public Task VolverAsync() { Destinos.Add("Volver"); return Task.CompletedTask; }
}

/// <summary>Bluetooth de la tablet de mentira (spec 008): se le dice qué hay, qué contesta el diálogo de permiso y qué ajustes se abrieron.</summary>
public sealed class FakeBluetoothBaston : Aviss.Rfid.IBluetoothBaston
{
    public bool Hay { get; set; } = true;
    public bool Encendido { get; set; } = true;
    public bool Permisos { get; set; } = true;
    /// <summary>Lo que contesta el usuario al diálogo del permiso.</summary>
    public bool ConcederAlPedir { get; set; } = true;
    public int PedidosDePermiso { get; private set; }
    public int AjustesBluetoothAbiertos { get; private set; }
    public int AjustesAppAbiertos { get; private set; }
    public List<Aviss.Rfid.Diagnostico.DispositivoBluetooth> Dispositivos { get; } = [];

    public Task<bool> PedirPermisosAsync()
    {
        PedidosDePermiso++;
        if (ConcederAlPedir) Permisos = true;
        return Task.FromResult(Permisos);
    }

    public bool TienePermisos() => Permisos;
    public bool HayBluetooth() => Hay;
    public bool EstaEncendido() => Encendido;
    public IReadOnlyList<Aviss.Rfid.Diagnostico.DispositivoBluetooth> ListarEmparejados() => Dispositivos;
    public bool EstaEmparejado(string direccion) => Dispositivos.Any(d => d.Direccion == direccion);
    public void AbrirAjustesBluetooth() => AjustesBluetoothAbiertos++;
    public void AbrirAjustesDeLaApp() => AjustesAppAbiertos++;

    public FakeBluetoothBaston Con(string nombre, string direccion)
    {
        Dispositivos.Add(new Aviss.Rfid.Diagnostico.DispositivoBluetooth(nombre, direccion, Aviss.Rfid.Diagnostico.TipoDispositivoBluetooth.Dual, []));
        return this;
    }
}

public sealed class FakePreferenciasBaston : IPreferenciasBaston
{
    public BastonRecordado? Actual { get; set; }
    public int Guardados { get; private set; }
    public BastonRecordado? Obtener() => Actual;
    public void Guardar(BastonRecordado baston) { Actual = baston; Guardados++; }
}

/// <summary>
/// Temporizador controlado por el test: cada espera queda pendiente hasta que el test la libera, así el aviso de 5
/// segundos y el refresco de 60 no esperan de verdad. Respeta la cancelación como el real.
/// </summary>
public sealed class FakeTemporizador : ITemporizador
{
    private readonly object _candado = new();
    private readonly List<(TimeSpan Tiempo, TaskCompletionSource Fin)> _esperas = [];

    public Task EsperarAsync(TimeSpan tiempo, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_candado) _esperas.Add((tiempo, tcs));
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    /// <summary>Cuántas esperas de ese tiempo siguen pendientes (no terminadas ni canceladas).</summary>
    public int Pendientes(TimeSpan tiempo)
    {
        lock (_candado) return _esperas.Count(e => e.Tiempo == tiempo && !e.Fin.Task.IsCompleted);
    }

    /// <summary>Termina la espera pendiente más antigua de ese tiempo. Devuelve false si no había ninguna.</summary>
    public bool Liberar(TimeSpan tiempo)
    {
        TaskCompletionSource? tcs;
        lock (_candado) tcs = _esperas.FirstOrDefault(e => e.Tiempo == tiempo && !e.Fin.Task.IsCompleted).Fin;
        return tcs is not null && tcs.TrySetResult();
    }
}

/// <summary>Esperas con tope para los tests que dependen de otro hilo (el bucle del lector, el refresco).</summary>
public static class Esperas
{
    public static async Task HastaAsync(Func<bool> condicion, int milisegundos = 5000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(milisegundos);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(5);
        }
    }
}
