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
        return Task.CompletedTask;
    }

    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Desconexiones++;
        Estado = EstadoConexionRfid.Desconectado;
        return Task.CompletedTask;
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
    public void Confirmacion() => Confirmaciones++;
    public void Repetido() => Repetidos++;
    public void SinTacto() => SinTactos++;
    public void MangaCompleta() => MangasCompletas++;
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
    public Task IrAEleccionTrabajoAsync() { Destinos.Add("Eleccion"); return Task.CompletedTask; }
    public Task IrATrabajoAsync() { Destinos.Add("Trabajo"); return Task.CompletedTask; }
    public Task IrAMovimientosAsync() { Destinos.Add("Movimientos"); return Task.CompletedTask; }
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
