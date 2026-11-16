using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Arranque;

/// <summary>Guarda cómo terminó el último arranque, para que una pantalla futura lo muestre (RF-28). Se registra como singleton.</summary>
public sealed class ArranqueResultado : IArranqueResultado
{
    private volatile ResultadoArranque? _actual;

    public ResultadoArranque? Actual => _actual;

    public void Establecer(ResultadoArranque resultado) => _actual = resultado;
}
