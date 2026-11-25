using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Application.Puertos;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

// Piezas compartidas por los servicios de campo (spec 003). Todas viven en el mismo scope (un DbContext y una
// transacción por operación) que el servicio que las usa.

/// <summary>El avance del rodeo en el día local de hoy (RF-30). No guarda nada: se calcula en cada consulta.</summary>
internal sealed class CalculadorAvance
{
    private readonly IRodeoRepository _rodeos;
    private readonly IRegistroCampoRepository _registros;
    private readonly IMangaRepository _mangas;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;

    public CalculadorAvance(IRodeoRepository rodeos, IRegistroCampoRepository registros, IMangaRepository mangas, IRelojService reloj, IZonaHoraria zona)
        => (_rodeos, _registros, _mangas, _reloj, _zona) = (rodeos, registros, mangas, reloj, zona);

    public async Task<AvanceRodeoDto> CalcularAsync(Guid rodeoId, CancellationToken ct)
    {
        var ahora = _reloj.UtcAhora;
        var zona = _zona.Zona;
        var hoy = DiaLocal.RangoUtcDeHoy(ahora, zona);
        var total = await _rodeos.ContarAnimalesAsync(rodeoId, ct);
        var procesadas = await _registros.ContarProcesadasAsync(rodeoId, hoy, ct);
        var horario = await _registros.ObtenerHorarioAsync(rodeoId, hoy, ct);
        var termino = await _mangas.ObtenerFechaTerminoElTactoAsync(rodeoId, hoy, ct);

        // El fin es el último movimiento o, si es posterior, la hora en que se apretó "Terminar el tacto".
        var terminado = termino is not null && (horario.UltimoUtc is null || termino >= horario.UltimoUtc);
        var fin = terminado ? termino : horario.UltimoUtc;
        return Armar(total, procesadas, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ahora, zona)), horario.PrimeroUtc, fin, terminado);
    }

    public static AvanceRodeoDto Armar(int total, int procesadas, DateOnly fecha, DateTime? primeraVacaUtc = null, DateTime? finUtc = null, bool terminado = false)
    {
        var pendientes = Math.Max(0, total - procesadas);
        // Porcentaje entero hacia abajo: nunca se muestra 100 % hasta que no falte ninguna. Sin vacas, 0 (no falla).
        var porcentaje = total == 0 ? 0 : procesadas * 100 / total;
        return new AvanceRodeoDto(fecha, total, procesadas, pendientes, porcentaje, primeraVacaUtc, finUtc, terminado);
    }
}

/// <summary>Arma la ficha de una vaca: sus datos, las columnas del Excel y el historial (RF-13, RF-14).</summary>
internal sealed class ArmadorFicha
{
    private readonly ITactoRepository _tactos;
    private readonly IEstadoReproductivoRepository _estados;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;
    private readonly IRechazoRepository _rechazos;
    private readonly IEsperaRepository _esperas;

    public ArmadorFicha(ITactoRepository tactos, IEstadoReproductivoRepository estados, IRelojService reloj, IZonaHoraria zona,
        IRechazoRepository rechazos, IEsperaRepository esperas)
        => (_tactos, _estados, _reloj, _zona, _rechazos, _esperas) = (tactos, estados, reloj, zona, rechazos, esperas);

    public async Task<FichaAnimalDto> ArmarAsync(Animal animal, CancellationToken ct)
    {
        var catalogo = (await _estados.ListarAsync(ct)).ToDictionary(e => e.Id);
        string? Nombre(Guid? id) => id is Guid g && catalogo.TryGetValue(g, out var n) ? n.Nombre : null;

        var tactos = (await _tactos.ListarPorAnimalAsync(animal.Id, ct))
            // Con eventos con pasos el título dice todo: "Tacto: Aborto, 45 días, Vacía OK" (RF-25, RF-30).
            .Select(t => new TactoParaFicha(t.FechaHoraUtc,
                DescriptorEventos.Describir(catalogo, t.EventoId, t.EstadoNuevoId, t.DiasPreniezInformados, t.DiasAborto, false, t.Evento2Id) ?? Nombre(t.EstadoNuevoId),
                t.Anulado));
        var rechazos = (await _rechazos.ListarPorAnimalAsync(animal.Id, ct))
            .Select(r => new RechazoParaFicha(r.Id, r.FechaHoraUtc, r.OrdenInsercion, r.Observaciones, r.Anulado, r.EventoId,
                DescriptorEventos.Describir(catalogo, r.EventoId, null, null, r.DiasAborto, esRechazo: true, evento2Id: r.Evento2Id)));
        var armada = ConstructorFicha.Armar(animal.DatosOriginalesJson, tactos, _reloj.UtcAhora, _zona.Zona, rechazos);
        var enEspera = await _esperas.ObtenerPendienteAsync(animal.Id, ct) is not null;

        return new FichaAnimalDto(
            animal.Id, animal.NumeroCaravana, animal.IdElectronico, animal.EstadoReproductivoId,
            Nombre(animal.EstadoReproductivoId), animal.FechaUltimoTactoUtc,
            armada.Campos, armada.Inseminaciones, armada.UltimoToro, armada.Historial,
            armada.Cvet, Aviss.Domain.Reglas.ReglasCvet.EntraEnTacto(armada.Cvet), armada.Extras, enEspera, armada.RechazoVigente);
    }
}

/// <summary>Una marca recién preparada (todavía sin guardar: la guarda la transacción de quien la pidió).</summary>
internal sealed record MarcaPreparada(MarcaPasada Marca, Manga Manga, int Contador);

/// <summary>
/// El paso común de "marcar a una vaca en la manga actual" (RF-4, RF-12): obtiene la manga abierta o crea la siguiente
/// (la manga se abre con su primera marca), toma el orden compartido y deja marca, manga e historial listos para que
/// la transacción del que llama los guarde juntos. Lo usa la lectura.
/// </summary>
internal sealed class Marcador
{
    private readonly IMangaRepository _mangas;
    private readonly IMarcaPasadaRepository _marcas;
    private readonly IRegistroCampoRepository _registros;
    private readonly IHistorialRepository _historial;

    public Marcador(IMangaRepository mangas, IMarcaPasadaRepository marcas, IRegistroCampoRepository registros,
        IHistorialRepository historial)
        => (_mangas, _marcas, _registros, _historial) = (mangas, marcas, registros, historial);

    public async Task<MarcaPreparada> MarcarAsync(Guid animalId, Guid rodeoId, DateTime ahora, CancellationToken ct)
    {
        var manga = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);
        var yaMarcadas = manga is null ? 0 : await _mangas.ContarVacasAsync(manga.Id, ct);

        if (manga is null)
        {
            // Manga por empezar: esta marca la abre, en la misma transacción.
            manga = Manga.Abrir(rodeoId, await _mangas.SiguienteNumeroAsync(rodeoId, ct), ahora);
            _mangas.Agregar(manga);
            _historial.Agregar(Historial.Crear("Manga", manga.Id, "Numero", null,
                manga.Numero.ToString(CultureInfo.InvariantCulture), ahora, AccionHistorial.MangaAbierta));
        }

        var orden = await _registros.SiguienteOrdenAsync(ct);
        var marca = MarcaPasada.Registrar(animalId, manga.Id, ahora, orden, ahora);
        _marcas.Agregar(marca);
        _historial.Agregar(Historial.Crear("MarcaPasada", marca.Id, "Manga", null, Ayudas.IdATexto(manga.Id),
            ahora, AccionHistorial.MarcaPasada));

        // La marca todavía no está en la base: el contador es lo que ya había más esta.
        return new MarcaPreparada(marca, manga, yaMarcadas + 1);
    }
}
