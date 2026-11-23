using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Registra el texto de cada comando de escritura (INSERT, CREATE, DROP, PRAGMA...) que EF ejecuta y, si se pide, hace
/// fallar el comando número N (empezando en 1). Sirve para dos cosas de la spec 006: comprobar que ninguna migración apaga
/// <c>foreign_keys</c> (RF-14) y probar que si la migración se corta en cualquier punto la base queda como estaba (RF-12).
/// </summary>
public sealed class InterceptorDeComandos : DbCommandInterceptor
{
    public List<string> Textos { get; } = new();

    /// <summary>Número (desde 1) del comando que debe fallar; null = ninguno.</summary>
    public int? FallarEnComando { get; set; }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Registrar(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Registrar(command);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    private void Registrar(DbCommand command)
    {
        Textos.Add(command.CommandText);
        if (FallarEnComando == Textos.Count)
            throw new InvalidOperationException($"falla simulada en el comando {Textos.Count}");
    }
}
