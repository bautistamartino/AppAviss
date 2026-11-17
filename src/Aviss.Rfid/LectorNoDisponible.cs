using System.Runtime.CompilerServices;
using Aviss.Application.Puertos;

namespace Aviss.Rfid;

/// <summary>
/// Lector que nunca entrega nada. Es lo que se registra en la versión que se distribuye hasta que exista el bastón
/// real (spec posterior): así el flujo de trabajo arranca igual, solo con la búsqueda manual (RF-23, RF-31).
/// </summary>
public sealed class LectorNoDisponible : IRfidReader
{
    public EstadoConexionRfid Estado => EstadoConexionRfid.Desconectado;

    public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async IAsyncEnumerable<TagRead> Lecturas([EnumeratorCancellation] CancellationToken ct)
    {
        // Espera hasta que se cancele y termina sin lanzar, como cualquier lector.
        try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        yield break;
    }
}
