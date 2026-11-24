using Aviss.Application.Exportacion;
using Aviss.Domain.Entidades;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Repositorios;

/// <summary>
/// Constancias de exportación (spec 007, RF-21). Solo se agregan: no hay forma de modificarlas ni borrarlas desde acá (y la base lo
/// impide con triggers). Comparte el DbContext de la operación, así que se guarda junto con el resto en el SaveChanges de la unidad de trabajo.
/// </summary>
internal sealed class ExportacionRepository : IExportacionRepository
{
    private readonly AvissDbContext _db;
    public ExportacionRepository(AvissDbContext db) => _db = db;

    public void Agregar(Exportacion exportacion) => _db.Exportaciones.Add(exportacion);

    public Task<Exportacion?> ObtenerUltimaAsync(Guid rodeoId, CancellationToken ct = default)
        => _db.Exportaciones.AsNoTracking()
            .Where(e => e.RodeoId == rodeoId)
            .OrderByDescending(e => e.FechaUtc).ThenByDescending(e => e.CreadoEnUtc)
            .FirstOrDefaultAsync(ct);
}
