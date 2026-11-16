using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure;

/// <summary>
/// Una operación = un DbContext (scoped) = una transacción. Dentro de la transacción la operación lee, valida y "agrega"
/// entidades; al terminar se hace UN solo SaveChanges y se confirma. Si algo lanza una excepción, o si la operación
/// devuelve un <see cref="Resultado"/> de rechazo, la transacción se revierte y no queda nada (RF-8, RF-13).
/// </summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly AvissDbContext _db;
    public UnitOfWork(AvissDbContext db) => _db = db;

    public async Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct = default)
    {
        // "await using": si no se llega al CommitAsync (excepción), al liberar la transacción SQLite la revierte sola.
        await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

        var resultado = await operacion();

        // Un rechazo de negocio no debe guardar nada, aunque la operación hubiera dejado entidades marcadas.
        if (resultado is Resultado { EsCorrecto: false })
        {
            await transaccion.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return resultado;
        }

        await _db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return resultado;
    }

    public async Task EjecutarAsync(Func<Task> operacion, CancellationToken ct = default)
        => await EjecutarAsync<object?>(async () => { await operacion(); return null; }, ct);
}
