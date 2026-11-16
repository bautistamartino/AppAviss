using Aviss.Infrastructure.Sistema;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>Espacio libre de prueba: el test dice cuántos bytes "hay" en el disco.</summary>
public sealed class FakeEspacioLibre : IEspacioLibreProvider
{
    /// <summary>Por defecto, de sobra.</summary>
    public long Bytes { get; set; } = long.MaxValue / 2;

    public long BytesLibres(string directorio) => Bytes;
}
