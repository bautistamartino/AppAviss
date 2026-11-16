using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Sistema;

/// <summary>El reloj real. En los tests se reemplaza por uno que tiene la hora fija.</summary>
public sealed class RelojSistema : IRelojService
{
    public DateTime UtcAhora => DateTime.UtcNow;
}
