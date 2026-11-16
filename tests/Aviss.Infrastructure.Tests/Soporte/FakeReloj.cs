using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>Reloj de prueba: la hora es la que el test dice (siempre UTC), así los resultados son deterministas.</summary>
public sealed class FakeReloj : IRelojService
{
    public DateTime UtcAhora { get; private set; } = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Fijar(DateTime utc) => UtcAhora = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    public void Avanzar(TimeSpan tiempo) => UtcAhora = UtcAhora.Add(tiempo);
}
