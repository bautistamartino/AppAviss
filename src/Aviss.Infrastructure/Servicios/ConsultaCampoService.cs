using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Consultas de solo lectura del trabajo de campo: ficha, últimos movimientos, rodeos para trabajar, veterinarios,
/// estados y la caravana de muestra del simulador (RF-1, RF-2, RF-13, RF-14, RF-28, RF-30). No guardan nada, así que
/// no abren transacción. Las que mezclan varias tablas (movimientos, muestras) leen directo del contexto, sin seguimiento.
/// </summary>
internal sealed class ConsultaCampoService
{
    private const int MaximoMovimientos = 50;

    private readonly AvissDbContext _db;
    private readonly IAnimalRepository _animales;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IRodeoRepository _rodeos;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEstadoReproductivoRepository _estados;
    private readonly IMangaRepository _mangas;
    private readonly IMarcaPasadaRepository _marcas;
    private readonly IRegistroCampoRepository _registros;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;
    private readonly CalculadorAvance _avance;
    private readonly ArmadorFicha _ficha;

    public ConsultaCampoService(AvissDbContext db, IAnimalRepository animales, IEstablecimientoRepository establecimientos,
        IRodeoRepository rodeos, IUsuarioRepository usuarios, IEstadoReproductivoRepository estados, IMangaRepository mangas,
        IMarcaPasadaRepository marcas, IRegistroCampoRepository registros, IRelojService reloj, IZonaHoraria zona,
        CalculadorAvance avance, ArmadorFicha ficha)
    {
        _db = db; _animales = animales; _establecimientos = establecimientos; _rodeos = rodeos; _usuarios = usuarios;
        _estados = estados; _mangas = mangas; _marcas = marcas; _registros = registros; _reloj = reloj; _zona = zona;
        _avance = avance; _ficha = ficha;
    }

    // ------------------------------------------------------------------ Ficha

    public async Task<Resultado<FichaAnimalDto>> ObtenerFichaAsync(Guid animalId, CancellationToken ct)
    {
        var animal = await _animales.ObtenerAsync(animalId, ct);
        if (animal is null) return Resultado.Error<FichaAnimalDto>(CodigoError.NoEncontrado, "El animal no existe.");
        return Resultado.Ok(await _ficha.ArmarAsync(animal, ct));
    }

    // ------------------------------------------------------------------ Preparar el trabajo

    /// <summary>Rodeos activos de los establecimientos activos, con su avance. Un rodeo Exportado se puede elegir igual (RF-2).</summary>
    public async Task<IReadOnlyList<RodeoParaTrabajarDto>> ListarRodeosParaTrabajarAsync(CancellationToken ct)
    {
        var resultado = new List<RodeoParaTrabajarDto>();
        foreach (var est in await _establecimientos.ListarAsync(ct))
        {
            if (!est.Activo) continue;
            foreach (var rodeo in await _rodeos.ListarPorEstablecimientoAsync(est.Id, ct))
            {
                if (!rodeo.Activo) continue;
                resultado.Add(new RodeoParaTrabajarDto(est.Id, est.Nombre, rodeo.Id, rodeo.Nombre,
                    rodeo.FechaImportacionUtc, rodeo.Estado, await _avance.CalcularAsync(rodeo.Id, ct)));
            }
        }
        return resultado;
    }

    /// <summary>Veterinarios activos para elegir: "Sin asignar" no se ofrece para trabajar (RF-1).</summary>
    public async Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosActivosAsync(CancellationToken ct)
        => (await _usuarios.ListarAsync(ct))
            .Where(u => u.Activo && u.Id != Usuario.SinAsignarId)
            .Select(u => new VeterinarioDto(u.Id, u.Nombre, u.Matricula))
            .ToList();

    public async Task<IReadOnlyList<EstadoActivoDto>> ListarEstadosActivosAsync(CancellationToken ct)
        => (await _estados.ListarAsync(ct)).Where(e => e.Activo).Select(e => new EstadoActivoDto(e.Id, e.Nombre)).ToList();

    // ------------------------------------------------------------------ Últimos movimientos (RF-28)

    public async Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct)
    {
        var usuarios = (await _usuarios.ListarAsync(ct)).ToDictionary(u => u.Id, u => u.Nombre);
        var estados = (await _estados.ListarAsync(ct)).ToDictionary(e => e.Id, e => e.Nombre);
        var numerosManga = await _db.Mangas.AsNoTracking().Where(m => m.RodeoId == rodeoId).ToDictionaryAsync(m => m.Id, m => m.Numero, ct);
        string Veterinario(Guid id) => usuarios.TryGetValue(id, out var n) ? n : "";

        // Cada tipo trae sus 50 más recientes; después se mezclan y se quedan los 50 más recientes en total.
        // La clave de desempate pone el cierre y la marca antes de su alta cuando comparten el mismo instante.
        var candidatos = new List<(MovimientoDto Movimiento, long Desempate)>();

        var tactos = await (from t in _db.Tactos.AsNoTracking()
                            join a in _db.Animales on t.AnimalId equals a.Id
                            where a.RodeoId == rodeoId && t.Tipo == TipoTacto.Normal
                            orderby t.FechaHoraUtc descending, t.OrdenInsercion descending
                            select new { t.Id, t.AnimalId, a.NumeroCaravana, t.FechaHoraUtc, t.OrdenInsercion, t.UsuarioId, t.EstadoNuevoId, t.Anulado })
            .Take(MaximoMovimientos).ToListAsync(ct);
        foreach (var t in tactos)
        {
            var estado = t.EstadoNuevoId is Guid e && estados.TryGetValue(e, out var nombre) ? nombre : null;
            candidatos.Add((new MovimientoDto(TipoMovimiento.Tacto, t.Id, t.AnimalId, t.NumeroCaravana, null, estado,
                t.FechaHoraUtc, Veterinario(t.UsuarioId), t.Anulado, false, null, null, null), t.OrdenInsercion + 1));
        }

        var marcas = await (from m in _db.MarcasPasada.AsNoTracking()
                            join a in _db.Animales on m.AnimalId equals a.Id
                            where a.RodeoId == rodeoId
                            orderby m.FechaHoraUtc descending, m.OrdenInsercion descending
                            select new { m.Id, m.AnimalId, a.NumeroCaravana, m.MangaId, m.FechaHoraUtc, m.OrdenInsercion, m.UsuarioId, m.Anulado })
            .Take(MaximoMovimientos).ToListAsync(ct);
        foreach (var m in marcas)
        {
            candidatos.Add((new MovimientoDto(TipoMovimiento.Marca, m.Id, m.AnimalId, m.NumeroCaravana,
                numerosManga.GetValueOrDefault(m.MangaId), null, m.FechaHoraUtc, Veterinario(m.UsuarioId), m.Anulado, false, null, null, null),
                m.OrdenInsercion + 1));
        }

        // Altas en campo: su fecha es la de creación y el veterinario sale del registro de historial del alta.
        var altas = await _db.Animales.AsNoTracking()
            .Where(a => a.RodeoId == rodeoId && a.Origen == OrigenAnimal.AltaEnCampo)
            .OrderByDescending(a => a.CreadoEnUtc)
            .Take(MaximoMovimientos).ToListAsync(ct);
        foreach (var a in altas)
        {
            var usuarioAlta = await _db.Historiales.AsNoTracking()
                .Where(h => h.Entidad == "Animal" && h.EntidadId == a.Id && h.Accion == AccionHistorial.Alta)
                .Select(h => (Guid?)h.UsuarioId).FirstOrDefaultAsync(ct);
            candidatos.Add((new MovimientoDto(TipoMovimiento.Alta, a.Id, a.Id, a.NumeroCaravana, null, null, a.CreadoEnUtc,
                usuarioAlta is Guid u ? Veterinario(u) : "", a.Anulado, false, null, null, null), 0));
        }

        var cierres = await (from c in _db.CierresManga.AsNoTracking()
                             join m in _db.Mangas on c.MangaId equals m.Id
                             where m.RodeoId == rodeoId
                             orderby c.FechaHoraUtc descending
                             select c).Take(MaximoMovimientos).ToListAsync(ct);
        foreach (var c in cierres)
        {
            var contador = await _mangas.ContarVacasAsync(c.MangaId, ct);
            candidatos.Add((new MovimientoDto(TipoMovimiento.Cierre, c.Id, null, null, numerosManga.GetValueOrDefault(c.MangaId),
                c.Tipo.ToString(), c.FechaHoraUtc, Veterinario(c.UsuarioId), c.Anulado, false,
                c.CantidadAviss, c.CantidadConfirmada, contador), long.MaxValue));
        }

        var recientes = candidatos
            .OrderByDescending(x => x.Movimiento.FechaHoraUtc).ThenByDescending(x => x.Desempate)
            .Take(MaximoMovimientos)
            .Select(x => x.Movimiento)
            .ToList();

        return await ConPuedeDeshacerAsync(rodeoId, recientes, ct);
    }

    /// <summary>
    /// Completa "PuedeDeshacer" con las mismas reglas que aplica cada deshacer (RF-26, RF-28, RF-29, RF-9): la lista
    /// solo ofrece lo que después se va a aceptar.
    /// </summary>
    private async Task<IReadOnlyList<MovimientoDto>> ConPuedeDeshacerAsync(Guid rodeoId, List<MovimientoDto> lista, CancellationToken ct)
    {
        var ahora = _reloj.UtcAhora;
        var zona = _zona.Zona;
        var ultimosPorAnimal = new Dictionary<Guid, RegistroCampoVigente?>();
        var ultimoCierre = await _mangas.ObtenerUltimoCierreVigenteDelRodeoAsync(rodeoId, ct);
        var abierta = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);

        var resultado = new List<MovimientoDto>(lista.Count);
        foreach (var m in lista)
        {
            var puede = false;
            switch (m.Tipo)
            {
                case TipoMovimiento.Tacto:
                case TipoMovimiento.Marca:
                {
                    // Solo el último registro vigente de la vaca, de cualquier tipo.
                    var animalId = m.AnimalId!.Value;
                    if (!ultimosPorAnimal.TryGetValue(animalId, out var ultimo))
                        ultimosPorAnimal[animalId] = ultimo = await _registros.ObtenerUltimoVigenteAsync(animalId, ct);
                    var tipo = m.Tipo == TipoMovimiento.Tacto ? TipoRegistroCampo.Tacto : TipoRegistroCampo.Marca;
                    puede = !m.Anulado && ultimo is not null && ultimo.Tipo == tipo && ultimo.Id == m.RegistroId;
                    break;
                }
                case TipoMovimiento.Alta:
                    // Se anula si no tiene tactos vigentes y a lo sumo la marca del alta (RF-29).
                    puede = !m.Anulado
                        && !await _animales.TieneTactosVigentesAsync(m.RegistroId, ct)
                        && (await _marcas.ListarVigentesDeAnimalAsync(m.RegistroId, ct)).Count <= 1;
                    break;
                case TipoMovimiento.Cierre:
                {
                    var cierre = await _mangas.ObtenerCierreAsync(m.RegistroId, ct);
                    if (cierre is null) break;
                    var motivo = ReglasManga.MotivoParaNoReabrir(new DatosReapertura(
                        cierre.Anulado, ultimoCierre?.Id == cierre.Id, cierre.Tipo, cierre.FechaHoraUtc, ahora, zona,
                        abierta is not null && abierta.Id != cierre.MangaId));
                    puede = motivo is null;
                    break;
                }
            }
            resultado.Add(m with { PuedeDeshacer = puede });
        }
        return resultado;
    }

    // ------------------------------------------------------------------ Caravana de muestra (solo para el simulador)

    public async Task<string?> CaravanaDeMuestraAsync(Guid rodeoId, MuestraCaravana tipo, CancellationToken ct)
    {
        var hoy = DiaLocal.RangoUtcDeHoy(_reloj.UtcAhora, _zona.Zona);
        var desde = hoy.Desde;
        var hasta = hoy.Hasta;

        if (tipo == MuestraCaravana.YaLeida)
        {
            // La vaca marcada hoy más recientemente.
            return await (from m in _db.MarcasPasada.AsNoTracking()
                          join a in _db.Animales on m.AnimalId equals a.Id
                          where a.RodeoId == rodeoId && !a.Anulado && !m.Anulado && m.FechaHoraUtc >= desde && m.FechaHoraUtc < hasta
                          orderby m.FechaHoraUtc descending, m.OrdenInsercion descending
                          select a.NumeroCaravana).FirstOrDefaultAsync(ct);
        }

        // Una vaca todavía pendiente (sin marca ni tacto de hoy).
        return await _db.Animales.AsNoTracking()
            .Where(a => a.RodeoId == rodeoId && !a.Anulado
                && !_db.MarcasPasada.Any(m => m.AnimalId == a.Id && !m.Anulado && m.FechaHoraUtc >= desde && m.FechaHoraUtc < hasta)
                && !_db.Tactos.Any(t => t.AnimalId == a.Id && t.Tipo == TipoTacto.Normal && !t.Anulado && t.FechaHoraUtc >= desde && t.FechaHoraUtc < hasta))
            .OrderBy(a => a.NumeroCaravana)
            .Select(a => a.NumeroCaravana)
            .FirstOrDefaultAsync(ct);
    }
}
