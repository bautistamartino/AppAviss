using Aviss.Application.Campo;
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

    public Task<bool> TieneMarcasVigentesAsync(Guid animalId, CancellationToken ct = default)
        => _db.MarcasPasada.AnyAsync(m => m.AnimalId == animalId && !m.Anulado, ct);

    public Task<Animal?> ObtenerPorIdElectronicoAsync(Guid rodeoId, string idElectronico, CancellationToken ct = default)
        => _db.Animales.FirstOrDefaultAsync(a => a.RodeoId == rodeoId && !a.Anulado && a.IdElectronico == idElectronico, ct);

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

    // El orden de guardado es UNO solo para tactos y marcas de pasada (spec 003, RF-21): ver OrdenCompartido.
    public Task<long> SiguienteOrdenInsercionAsync(CancellationToken ct = default)
        => OrdenCompartido.SiguienteAsync(_db, ct);

    public async Task<IReadOnlyList<Tacto>> ListarPorAnimalAsync(Guid animalId, CancellationToken ct = default)
        => await _db.Tactos.AsNoTracking()
            .Where(t => t.AnimalId == animalId && t.Tipo == TipoTacto.Normal)
            .OrderBy(t => t.FechaHoraUtc).ThenBy(t => t.OrdenInsercion)
            .ToListAsync(ct);

    public void Agregar(Tacto tacto) => _db.Tactos.Add(tacto);
}

/// <summary>Solo agrega: no hay forma de leer-modificar ni borrar historial desde la app (RF-11).</summary>
internal sealed class HistorialRepository : IHistorialRepository
{
    private readonly AvissDbContext _db;
    public HistorialRepository(AvissDbContext db) => _db = db;

    public void Agregar(Historial registro) => _db.Historiales.Add(registro);
}

// ---------------------------------------------------------------------------------------------------------------
// Spec 003: mangas, marcas de pasada y consultas sobre todos los registros de campo.
// ---------------------------------------------------------------------------------------------------------------

/// <summary>
/// El orden de guardado compartido (RF-21): el máximo de <c>OrdenInsercion</c> entre tactos y marcas, más uno. No hay
/// tabla de secuencia: se calcula DENTRO de la transacción de cada operación (SQLite admite un solo escritor), y cada
/// tabla conserva su índice único. La spec 004 suma sus tablas acá.
/// </summary>
internal static class OrdenCompartido
{
    public static async Task<long> SiguienteAsync(AvissDbContext db, CancellationToken ct)
    {
        var tactos = await db.Tactos.MaxAsync(t => (long?)t.OrdenInsercion, ct) ?? 0;
        var marcas = await db.MarcasPasada.MaxAsync(m => (long?)m.OrdenInsercion, ct) ?? 0;
        var rechazos = await db.Rechazos.MaxAsync(r => (long?)r.OrdenInsercion, ct) ?? 0; // spec 004
        return Math.Max(Math.Max(tactos, marcas), rechazos) + 1;
    }
}

internal sealed class MangaRepository : IMangaRepository
{
    private readonly AvissDbContext _db;
    public MangaRepository(AvissDbContext db) => _db = db;

    public Task<Manga?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Mangas.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

    // Abierta = sin ningún cierre vigente. Una manga nunca cambia, así que se lee sin seguimiento.
    public Task<Manga?> ObtenerAbiertaAsync(Guid rodeoId, CancellationToken ct = default)
        => _db.Mangas.AsNoTracking()
            .Where(m => m.RodeoId == rodeoId && !_db.CierresManga.Any(c => c.MangaId == m.Id && !c.Anulado))
            .OrderByDescending(m => m.Numero)
            .FirstOrDefaultAsync(ct);

    public async Task<int> SiguienteNumeroAsync(Guid rodeoId, CancellationToken ct = default)
        => (await _db.Mangas.Where(m => m.RodeoId == rodeoId).MaxAsync(m => (int?)m.Numero, ct) ?? 0) + 1;

    // Con seguimiento: quien lo pide puede anularlo.
    public Task<CierreManga?> ObtenerCierreAsync(Guid cierreId, CancellationToken ct = default)
        => _db.CierresManga.FirstOrDefaultAsync(c => c.Id == cierreId, ct);

    public Task<CierreManga?> ObtenerUltimoCierreVigenteDelRodeoAsync(Guid rodeoId, CancellationToken ct = default)
        => (from c in _db.CierresManga.AsNoTracking()
            join m in _db.Mangas on c.MangaId equals m.Id
            where m.RodeoId == rodeoId && !c.Anulado
            // Por fecha y hora; con el mismo instante, la manga de número mayor (la que se cerró después).
            orderby c.FechaHoraUtc descending, m.Numero descending, c.CreadoEnUtc descending
            select c).FirstOrDefaultAsync(ct);

    public Task<int> ContarVacasAsync(Guid mangaId, CancellationToken ct = default)
        => _db.MarcasPasada.CountAsync(m => m.MangaId == mangaId && !m.Anulado, ct);

    public Task<DateTime?> ObtenerFechaTerminoElTactoAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default)
    {
        var desde = rango.Desde;
        var hasta = rango.Hasta;
        return (from c in _db.CierresManga.AsNoTracking()
                join m in _db.Mangas on c.MangaId equals m.Id
                where m.RodeoId == rodeoId && !c.Anulado && c.Tipo == TipoCierreManga.TerminoElTacto
                      && c.FechaHoraUtc >= desde && c.FechaHoraUtc < hasta
                orderby c.FechaHoraUtc descending
                select (DateTime?)c.FechaHoraUtc).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListarCaravanasMarcadasAsync(Guid mangaId, CancellationToken ct = default)
        => await (from m in _db.MarcasPasada.AsNoTracking()
                  join a in _db.Animales on m.AnimalId equals a.Id
                  where m.MangaId == mangaId && !m.Anulado
                  orderby m.OrdenInsercion
                  select a.NumeroCaravana).ToListAsync(ct);

    public void Agregar(Manga manga) => _db.Mangas.Add(manga);
    public void AgregarCierre(CierreManga cierre) => _db.CierresManga.Add(cierre);
}

internal sealed class MarcaPasadaRepository : IMarcaPasadaRepository
{
    private readonly AvissDbContext _db;
    public MarcaPasadaRepository(AvissDbContext db) => _db = db;

    public Task<MarcaPasada?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.MarcasPasada.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<MarcaPasada?> ObtenerVigenteEnRangoAsync(Guid animalId, RangoUtc rango, CancellationToken ct = default)
    {
        var desde = rango.Desde;
        var hasta = rango.Hasta;
        return _db.MarcasPasada.AsNoTracking()
            .Where(m => m.AnimalId == animalId && !m.Anulado && m.FechaHoraUtc >= desde && m.FechaHoraUtc < hasta)
            .OrderByDescending(m => m.FechaHoraUtc).ThenByDescending(m => m.OrdenInsercion)
            .FirstOrDefaultAsync(ct);
    }

    public Task<MarcaPasada?> ObtenerVigenteEnMangaAsync(Guid animalId, Guid mangaId, CancellationToken ct = default)
        => _db.MarcasPasada.AsNoTracking().FirstOrDefaultAsync(m => m.AnimalId == animalId && m.MangaId == mangaId && !m.Anulado, ct);

    // Con seguimiento: quien las pide (anular un alta) las modifica.
    public async Task<IReadOnlyList<MarcaPasada>> ListarVigentesDeAnimalAsync(Guid animalId, CancellationToken ct = default)
        => await _db.MarcasPasada.Where(m => m.AnimalId == animalId && !m.Anulado).ToListAsync(ct);

    public void Agregar(MarcaPasada marca) => _db.MarcasPasada.Add(marca);
}

/// <summary>
/// Lo que mira todos los tipos de registro de campo juntos. Hoy son dos (tacto y marca de pasada); la spec 004 suma
/// los suyos acá y el resto de la lógica no se entera.
/// </summary>
internal sealed class RegistroCampoRepository : IRegistroCampoRepository
{
    private readonly AvissDbContext _db;
    public RegistroCampoRepository(AvissDbContext db) => _db = db;

    public async Task<RegistroCampoVigente?> ObtenerUltimoVigenteAsync(Guid animalId, CancellationToken ct = default)
    {
        // El último vigente de cada tipo sale de su índice (AnimalId, FechaHoraUtc, OrdenInsercion); después se comparan
        // los dos por (fecha, orden). Como el orden es compartido, nunca empatan.
        var tacto = await _db.Tactos.AsNoTracking()
            .Where(t => t.AnimalId == animalId && t.Tipo == TipoTacto.Normal && !t.Anulado)
            .OrderByDescending(t => t.FechaHoraUtc).ThenByDescending(t => t.OrdenInsercion)
            .Select(t => new RegistroCampoVigente(TipoRegistroCampo.Tacto, t.Id, t.FechaHoraUtc, t.OrdenInsercion, t.EstadoNuevoId, null))
            .FirstOrDefaultAsync(ct);
        var marca = await _db.MarcasPasada.AsNoTracking()
            .Where(m => m.AnimalId == animalId && !m.Anulado)
            .OrderByDescending(m => m.FechaHoraUtc).ThenByDescending(m => m.OrdenInsercion)
            .Select(m => new RegistroCampoVigente(TipoRegistroCampo.Marca, m.Id, m.FechaHoraUtc, m.OrdenInsercion, null, m.MangaId))
            .FirstOrDefaultAsync(ct);

        var rechazo = await _db.Rechazos.AsNoTracking()
            .Where(r => r.AnimalId == animalId && !r.Anulado)
            .OrderByDescending(r => r.FechaHoraUtc).ThenByDescending(r => r.OrdenInsercion)
            .Select(r => new RegistroCampoVigente(TipoRegistroCampo.Rechazo, r.Id, r.FechaHoraUtc, r.OrdenInsercion, null, null, r.Observaciones))
            .FirstOrDefaultAsync(ct);

        // Gana el de (fecha, orden) más alto entre los tres.
        RegistroCampoVigente? ganador = null;
        foreach (var r in new[] { tacto, marca, rechazo })
            if (r is not null && (ganador is null || (r.FechaHoraUtc, r.OrdenInsercion).CompareTo((ganador.FechaHoraUtc, ganador.OrdenInsercion)) > 0))
                ganador = r;
        return ganador;
    }

    public Task<long> SiguienteOrdenAsync(CancellationToken ct = default) => OrdenCompartido.SiguienteAsync(_db, ct);

    public Task<bool> TieneTactoVigenteEnRangoAsync(Guid animalId, RangoUtc rango, CancellationToken ct = default)
    {
        var desde = rango.Desde;
        var hasta = rango.Hasta;
        return _db.Tactos.AnyAsync(
            t => t.AnimalId == animalId && t.Tipo == TipoTacto.Normal && !t.Anulado && t.FechaHoraUtc >= desde && t.FechaHoraUtc < hasta, ct);
    }

    // Procesadas hoy: vacas no anuladas con una marca o un tacto normal vigente dentro del día. El UNION saca los
    // repetidos (una vaca con marca y tacto cuenta una sola vez).
    public Task<int> ContarProcesadasAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default)
    {
        var desde = rango.Desde;
        var hasta = rango.Hasta;
        var porMarca = from m in _db.MarcasPasada
                       join a in _db.Animales on m.AnimalId equals a.Id
                       where a.RodeoId == rodeoId && !a.Anulado && !m.Anulado && m.FechaHoraUtc >= desde && m.FechaHoraUtc < hasta
                       select a.Id;
        var porTacto = from t in _db.Tactos
                       join a in _db.Animales on t.AnimalId equals a.Id
                       where a.RodeoId == rodeoId && !a.Anulado && t.Tipo == TipoTacto.Normal && !t.Anulado
                             && t.FechaHoraUtc >= desde && t.FechaHoraUtc < hasta
                       select a.Id;
        // Los rechazos son pocos: se parte de ellos (EXISTS sobre el animal) y no de los 20.000 animales del rodeo, para que
        // una tabla casi vacía no cueste una pasada por todo el rodeo.
        var porRechazo = from r in _db.Rechazos
                         where !r.Anulado && r.FechaHoraUtc >= desde && r.FechaHoraUtc < hasta
                               && _db.Animales.Any(a => a.Id == r.AnimalId && a.RodeoId == rodeoId && !a.Anulado)
                         select r.AnimalId;
        return porMarca.Union(porTacto).Union(porRechazo).CountAsync(ct);
    }

    // Primer y último movimiento de hoy: se mira la marca y el tacto normal más tempranos y más tardíos (vigentes, de vacas
    // no anuladas del rodeo) y se queda con el menor y el mayor. Se ordena y se toma uno (no MIN/MAX) para no depender de
    // cómo traduce SQLite las fechas guardadas como texto.
    public async Task<HorarioDeRegistros> ObtenerHorarioAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default)
    {
        var desde = rango.Desde;
        var hasta = rango.Hasta;
        var marcas = from m in _db.MarcasPasada.AsNoTracking()
                     join a in _db.Animales on m.AnimalId equals a.Id
                     where a.RodeoId == rodeoId && !a.Anulado && !m.Anulado && m.FechaHoraUtc >= desde && m.FechaHoraUtc < hasta
                     select m.FechaHoraUtc;
        var tactos = from t in _db.Tactos.AsNoTracking()
                     join a in _db.Animales on t.AnimalId equals a.Id
                     where a.RodeoId == rodeoId && !a.Anulado && t.Tipo == TipoTacto.Normal && !t.Anulado
                           && t.FechaHoraUtc >= desde && t.FechaHoraUtc < hasta
                     select t.FechaHoraUtc;
        var rechazos = from r in _db.Rechazos.AsNoTracking()
                       where !r.Anulado && r.FechaHoraUtc >= desde && r.FechaHoraUtc < hasta
                             && _db.Animales.Any(a => a.Id == r.AnimalId && a.RodeoId == rodeoId && !a.Anulado)
                       select r.FechaHoraUtc;

        DateTime? primerRechazo = await rechazos.OrderBy(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);
        DateTime? ultimoRechazo = await rechazos.OrderByDescending(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);
        DateTime? primeraMarca = await marcas.OrderBy(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);
        DateTime? primerTacto = await tactos.OrderBy(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);
        DateTime? ultimaMarca = await marcas.OrderByDescending(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);
        DateTime? ultimoTacto = await tactos.OrderByDescending(f => f).Select(f => (DateTime?)f).FirstOrDefaultAsync(ct);

        return new HorarioDeRegistros(Menor(Menor(primeraMarca, primerTacto), primerRechazo), Mayor(Mayor(ultimaMarca, ultimoTacto), ultimoRechazo));
    }

    private static DateTime? Menor(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a < b ? a : b);
    private static DateTime? Mayor(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a > b ? a : b);
}

// ---------------------------------------------------------------------------------------------------------------
// Spec 004: esperas y rechazos.
// ---------------------------------------------------------------------------------------------------------------

internal sealed class EsperaRepository : IEsperaRepository
{
    private readonly AvissDbContext _db;
    public EsperaRepository(AvissDbContext db) => _db = db;

    // Con seguimiento: quien la pide la resuelve o la saltea. La base no admite dos pendientes de la misma vaca.
    public Task<Espera?> ObtenerPendienteAsync(Guid animalId, CancellationToken ct = default)
        => _db.Esperas.FirstOrDefaultAsync(e => e.AnimalId == animalId && e.Estado == EstadoEspera.Pendiente, ct);

    public async Task<IReadOnlyList<EsperaDto>> ListarPendientesAsync(Guid rodeoId, CancellationToken ct = default)
    {
        var filas = await (from e in _db.Esperas.AsNoTracking()
                           join a in _db.Animales on e.AnimalId equals a.Id
                           where a.RodeoId == rodeoId && e.Estado == EstadoEspera.Pendiente
                           orderby e.FechaHoraUtc, e.CreadoEnUtc
                           select new { e.Id, e.AnimalId, a.NumeroCaravana, a.DatosOriginalesJson, e.FechaHoraUtc }).ToListAsync(ct);
        return filas.Select(f => new EsperaDto(f.Id, f.AnimalId, f.NumeroCaravana, ConstructorFicha.LeerCvet(f.DatosOriginalesJson), f.FechaHoraUtc)).ToList();
    }

    public void Agregar(Espera espera) => _db.Esperas.Add(espera);
}

internal sealed class RechazoRepository : IRechazoRepository
{
    private readonly AvissDbContext _db;
    public RechazoRepository(AvissDbContext db) => _db = db;

    // Con seguimiento: quien lo pide puede anularlo.
    public Task<Rechazo?> ObtenerAsync(Guid id, CancellationToken ct = default)
        => _db.Rechazos.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Rechazo>> ListarPorAnimalAsync(Guid animalId, CancellationToken ct = default)
        => await _db.Rechazos.AsNoTracking()
            .Where(r => r.AnimalId == animalId)
            .OrderBy(r => r.FechaHoraUtc).ThenBy(r => r.OrdenInsercion)
            .ToListAsync(ct);

    public void Agregar(Rechazo rechazo) => _db.Rechazos.Add(rechazo);
}
