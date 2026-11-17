using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>El temporizador de verdad: espera el tiempo pedido (los tests usan uno falso que no espera).</summary>
public sealed class TemporizadorReal : ITemporizador
{
    public Task EsperarAsync(TimeSpan tiempo, CancellationToken ct) => Task.Delay(tiempo, ct);
}
