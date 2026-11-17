using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aviss.Application.Puertos;

namespace Aviss.Rfid;

/// <summary>
/// Lector simulado (spec 003, RF-31). Es a la vez el lector (<see cref="IRfidReader"/>) y el simulador
/// (<see cref="ISimuladorLecturas"/>): quien llama a <see cref="Emitir"/> "lee" una caravana.
/// Se usa en el emulador (solo en la compilación de desarrollo) y en las pruebas, que también pueden darle una lista
/// de lecturas grabadas desde el principio.
/// </summary>
public sealed class FakeRfidReader : IRfidReader, ISimuladorLecturas
{
    // Canal sin límite: las lecturas que llegan antes de que alguien escuche quedan esperando (como el buffer del bastón).
    private readonly Channel<TagRead> _canal = Channel.CreateUnbounded<TagRead>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly Func<DateTime> _ahoraUtc;

    /// <param name="grabadas">Códigos que el lector entrega apenas se lo escucha (lecturas grabadas para pruebas).</param>
    /// <param name="ahoraUtc">Reloj para la hora de cada lectura; por omisión, la hora real.</param>
    public FakeRfidReader(IEnumerable<string>? grabadas = null, Func<DateTime>? ahoraUtc = null)
    {
        _ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        if (grabadas is not null)
            foreach (var codigo in grabadas) Emitir(codigo);
    }

    public EstadoConexionRfid Estado { get; private set; } = EstadoConexionRfid.Desconectado;

    /// <summary>"Lee" un código: queda disponible para quien esté escuchando <see cref="Lecturas"/>.</summary>
    public void Emitir(string codigo) => _canal.Writer.TryWrite(new TagRead(codigo, _ahoraUtc()));

    public Task ConectarAsync(CancellationToken ct = default)
    {
        Estado = EstadoConexionRfid.Conectado;
        return Task.CompletedTask;
    }

    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Estado = EstadoConexionRfid.Desconectado;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TagRead> Lecturas([EnumeratorCancellation] CancellationToken ct)
    {
        var lector = _canal.Reader;
        while (!ct.IsCancellationRequested)
        {
            // Esperar sin consumir; al cancelar, la secuencia termina sin lanzar (no es un error cortar la escucha).
            try
            {
                if (!await lector.WaitToReadAsync(ct).ConfigureAwait(false)) yield break;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            while (lector.TryRead(out var lectura))
                yield return lectura;
        }
    }
}
