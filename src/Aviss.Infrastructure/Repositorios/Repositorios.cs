using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Repositorios;

// Repositorios sobre EF Core. Comparten el DbContext del scope (el mismo que usa la UnitOfWork), así que todo lo que
// "agregan" o modifican se guarda junto, con un único SaveChanges, dentro de la transacción de la operación.
// Los métodos "Agregar" solo marcan la entidad: no tocan la base hasta el SaveChanges de la UnitOfWork.

internal sealed class EstablecimientoRepository : IEstablecimientoRepository
{
    private readonly AvissDbContext _db;
    public EstablecimientoRepository(AvissDbContext db) => _db = db;

    public Task<Establecimiento?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Establecimientos.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Establecimiento>> ListarAsync(CancellationToken ct = default)
        => await _db.Establecimientos.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(ct);

    public void Agregar(Establecimiento establecimiento) => _db.Establecimientos.Add(establecimiento);
}

internal sealed class RodeoRepository : IRodeoRepository
{
    private readonly AvissDbContext _db;
    public RodeoRepository(AvissDbContext db) => _db = db;

    public Task<Rodeo?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Rodeos.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Rodeo>> ListarPorEstablecimientoAsync(Guid establecimientoId, CancellationToken ct = default)
        => await _db.Rodeos.AsNoTracking().Where(r => r.EstablecimientoId == establecimientoId).OrderBy(r => r.Nombre).ToListAsync(ct);

    public Task<int> ContarAnimalesAsync(Guid rodeoId, CancellationToken ct = default)
        => _db.Animales.CountAsync(a => a.RodeoId == rodeoId && !a.Anulado, ct);

    // Proyecta solo nombre y fecha: el blob del archivo (hasta 25 MB) no se lee.
    public async Task<IReadOnlyList<RodeoImportadoResumen>> BuscarPorHashAsync(string hash, CancellationToken ct = default)
    {
        var filas = await _db.Rodeos.AsNoTracking()
            .Where(r => r.ArchivoOrigenHash == hash)
            .Select(r => new { r.Nombre, r.FechaImportacionUtc })
            .ToListAsync(ct);
        // El orden se hace en memoria (más reciente primero): SQLite no ordena bien DateTime guardado como texto en todos los casos.
        return filas.OrderByDescending(r => r.FechaImportacionUtc)
            .Select(r => new RodeoImportadoResumen(r.Nombre, r.FechaImportacionUtc)).ToList();
    }

    public void Agregar(Rodeo rodeo) => _db.Rodeos.Add(rodeo);
}

internal sealed class UsuarioRepository : IUsuarioRepository
{
    private readonly AvissDbContext _db;
    public UsuarioRepository(AvissDbContext db) => _db = db;

    public Task<Usuario?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<Usuario>> ListarAsync(CancellationToken ct = default)
        => await _db.Usuarios.AsNoTracking().OrderBy(u => u.Nombre).ToListAsync(ct);

    public Task<int> ContarActivosExceptoAsync(Guid usuarioId, CancellationToken ct = default)
        => _db.Usuarios.CountAsync(u => u.Activo && u.Id != usuarioId, ct);

    public void Agregar(Usuario usuario) => _db.Usuarios.Add(usuario);
}

internal sealed class EstadoReproductivoRepository : IEstadoReproductivoRepository
{
    private readonly AvissDbContext _db;
    public EstadoReproductivoRepository(AvissDbContext db) => _db = db;

    public Task<EstadoReproductivo?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Estados.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<EstadoReproductivo?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default)
        => _db.Estados.FirstOrDefaultAsync(e => e.Codigo == codigo, ct);

    public async Task<IReadOnlyList<EstadoReproductivo>> ListarAsync(CancellationToken ct = default)
        => await _db.Estados.AsNoTracking().OrderBy(e => e.Nombre).ToListAsync(ct);

    public Task<bool> ExisteNombreAsync(string nombre, Guid? excluirId, CancellationToken ct = default)
    {
        // Se compara por la columna generada NombreClave (mayúsculas incluida la Ñ), con la misma regla que usa la base.
        var clave = Caravana.CalcularClave(nombre.Trim());
        return _db.Estados.AnyAsync(e => EF.Property<string>(e, "NombreClaveBd") == clave && (excluirId == null || e.Id != excluirId), ct);
    }

    public Task<bool> ExisteCodigoDairyCompActivoAsync(string codigoDairyComp, Guid? excluirId, CancellationToken ct = default)
    {
        var codigo = codigoDairyComp.Trim();
        return _db.Estados.AnyAsync(e => e.Activo && e.CodigoDairyComp == codigo && (excluirId == null || e.Id != excluirId), ct);
    }

    public void Agregar(EstadoReproductivo estado) => _db.Estados.Add(estado);
}

internal sealed class AnimalRepository : IAnimalRepository
{
    private readonly AvissDbContext _db;
    public AnimalRepository(AvissDbContext db) => _db = db;

    public Task<Animal?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Animales.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Animal?> ObtenerPorCaravanaAsync(Guid rodeoId, Caravana caravana, CancellationToken ct = default)
    {
        // "!a.Anulado" es parte de la condición del índice único parcial: sin ella SQLite no podría usarlo.
        var clave = caravana.Clave;
        return _db.Animales.FirstOrDefaultAsync(
            a => a.RodeoId == rodeoId && !a.Anulado && EF.Property<string>(a, "NumeroCaravanaClave") == clave, ct);
    }

    public Task<bool> ExisteIdElectronicoAsync(Guid rodeoId, string idElectronico, Guid? excluirAnimalId, CancellationToken ct = default)
        => _db.Animales.AnyAsync(
            a => a.RodeoId == rodeoId && !a.Anulado && a.IdElectronico == idElectronico
                 && (excluirAnimalId == null || a.Id != excluirAnimalId), ct);

    public Task<bool> TieneTactosVigentesAsync(Guid animalId, CancellationToken ct = default)
        => _db.Tactos.AnyAsync(t => t.AnimalId == animalId && t.Tipo == TipoTacto.Normal && !t.Anulado, ct);

    public void Agregar(Animal animal) => _db.Animales.Add(animal);

    // La detección automática de cambios se apaga solo durante la carga: con 20.000 animales, Add() sin esto es muy lento.
    public void AgregarVarios(IEnumerable<Animal> animales)
    {
        var anterior = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try { _db.Animales.AddRange(animales); }
        finally { _db.ChangeTracker.AutoDetectChangesEnabled = anterior; }
    }
}

internal sealed class TactoRepository : ITactoRepository
{
    private readonly AvissDbContext _db;
    public TactoRepository(AvissDbContext db) => _db = db;

    public Task<Tacto?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Tactos.FirstOrDefaultAsync(t => t.Id == id, ct);

    // Último vigente: normal, no anulado, fecha más reciente; con igual fecha gana el de mayor OrdenInsercion.
    // Es la misma regla que ReglasTacto.UltimoVigente, pero resuelta en la base con el índice (AnimalId, FechaHoraUtc, OrdenInsercion).
    public Task<Tacto?> ObtenerUltimoVigenteAsync(Guid animalId, CancellationToken ct = default)
        => _db.Tactos
            .Where(t => t.AnimalId == animalId && t.Tipo == TipoTacto.Normal && !t.Anulado)
            .OrderByDescending(t => t.FechaHoraUtc).ThenByDescending(t => t.OrdenInsercion)
            .FirstOrDefaultAsync(ct);

    public Task<Tacto?> ObtenerVigenteAnteriorAsync(Guid animalId, Guid excluirTactoId, CancellationToken ct = default)
        => _db.Tactos
            .Where(t => t.AnimalId == animalId && t.Id != excluirTactoId && t.Tipo == TipoTacto.Normal && !t.Anulado)
            .OrderByDescending(t => t.FechaHoraUtc).ThenByDescending(t => t.OrdenInsercion)
            .FirstOrDefaultAsync(ct);

    public async Task<long> SiguienteOrdenInsercionAsync(CancellationToken ct = default)
        => (await _db.Tactos.MaxAsync(t => (long?)t.OrdenInsercion, ct) ?? 0) + 1;

    public void Agregar(Tacto tacto) => _db.Tactos.Add(tacto);
}

/// <summary>Solo agrega: no hay forma de leer-modificar ni borrar historial desde la app (RF-11).</summary>
internal sealed class HistorialRepository : IHistorialRepository
{
    private readonly AvissDbContext _db;
    public HistorialRepository(AvissDbContext db) => _db = db;

    public void Agregar(Historial registro) => _db.Historiales.Add(registro);
}
