using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Las mangas: empezar a trabajar, estado y contador, resumen, finalizar, terminar el tacto y reabrir (RF-4 a RF-10).
/// Una manga nunca cambia; "abierta" es no tener un cierre vigente, y reabrir es anular ese cierre.
/// </summary>
internal sealed class MangaService
{
    private readonly IUnitOfWork _uow;
    private readonly IRodeoRepository _rodeos;
    private readonly IMangaRepository _mangas;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;
    private readonly CalculadorAvance _avance;

    public MangaService(IUnitOfWork uow, IRodeoRepository rodeos, IMangaRepository mangas,
        IHistorialRepository historial, IRelojService reloj, IZonaHoraria zona, CalculadorAvance avance)
    {
        _uow = uow; _rodeos = rodeos; _mangas = mangas; _historial = historial;
        _reloj = reloj; _zona = zona; _avance = avance;
    }

    // ------------------------------------------------------------------ Empezar y estado

    /// <summary>
    /// Empezar a trabajar un rodeo (RF-4, RF-10): si la manga abierta viene de un día local anterior (el veterinario no
    /// terminó el tacto), se cierra sola como "automática, sin confirmar". Después se devuelve el estado.
    /// </summary>
    public async Task<Resultado<EstadoTrabajoDto>> IniciarTrabajoAsync(Guid rodeoId, CancellationToken ct)
    {
        var cierre = await _uow.EjecutarAsync(async () =>
        {
            var rodeo = await _rodeos.ObtenerAsync(rodeoId, ct);
            if (rodeo is null) return Resultado.Error(CodigoError.NoEncontrado, "El rodeo no existe.");
            if (!rodeo.Activo) return Resultado.Error(CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no se puede trabajar.");

            var abierta = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);
            var ahora = _reloj.UtcAhora;
            if (abierta is not null && DiaLocal.EsDeDiaAnterior(abierta.CreadoEnUtc, ahora, _zona.Zona))
            {
                try
                {
                    var contador = await _mangas.ContarVacasAsync(abierta.Id, ct);
                    GuardarCierre(abierta.Id, TipoCierreManga.Automatico, ahora, contador, null);
                }
                catch (DominioException ex) { return Ayudas.DatosInvalidos(ex); }
            }
            return Resultado.Ok();
        }, ct);

        if (!cierre.EsCorrecto) return Resultado.Error<EstadoTrabajoDto>(cierre.Codigo!.Value, cierre.Mensaje!);
        return await ObtenerEstadoAsync(rodeoId, ct);
    }

    /// <summary>La manga actual (o la siguiente "por empezar"), su contador y el avance del día (RF-3, RF-4, RF-30).</summary>
    public async Task<Resultado<EstadoTrabajoDto>> ObtenerEstadoAsync(Guid rodeoId, CancellationToken ct)
    {
        if (await _rodeos.ObtenerAsync(rodeoId, ct) is null)
            return Resultado.Error<EstadoTrabajoDto>(CodigoError.NoEncontrado, "El rodeo no existe.");

        var avance = await _avance.CalcularAsync(rodeoId, ct);
        var abierta = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);
        if (abierta is null)
        {
            // Por empezar: "Manga N · 0". No existe todavía en la base: la primera marca la crea.
            var siguiente = await _mangas.SiguienteNumeroAsync(rodeoId, ct);
            return Resultado.Ok(new EstadoTrabajoDto(rodeoId, siguiente, false, null, 0, avance));
        }
        var contador = await _mangas.ContarVacasAsync(abierta.Id, ct);
        return Resultado.Ok(new EstadoTrabajoDto(rodeoId, abierta.Numero, true, abierta.Id, contador, avance));
    }

    /// <summary>Lo que se muestra al verificar antes de finalizar: el contador y las caravanas (RF-6, RF-7).</summary>
    public async Task<Resultado<ResumenMangaDto>> ObtenerResumenAsync(Guid rodeoId, CancellationToken ct)
    {
        var abierta = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);
        if (abierta is null)
            return Resultado.Error<ResumenMangaDto>(CodigoError.NoHayMangaAbierta, MensajesCampo.Para(CodigoError.NoHayMangaAbierta));
        var caravanas = await _mangas.ListarCaravanasMarcadasAsync(abierta.Id, ct);
        return Resultado.Ok(new ResumenMangaDto(abierta.Id, abierta.Numero, caravanas.Count, caravanas));
    }

    // ------------------------------------------------------------------ Cerrar

    /// <summary>"Finalizar manga": cierra la abierta y la siguiente queda por empezar (RF-6).</summary>
    public async Task<Resultado<ResultadoCierreDto>> FinalizarAsync(FinalizarMangaCommand c, CancellationToken ct)
    {
        var r = await CerrarAsync(c.RodeoId, c.CantidadReal, TipoCierreManga.Manual, sinMangaEsError: true, ct);
        return await ConAvanceAsync(r, c.RodeoId, ct);
    }

    /// <summary>
    /// "Terminar el tacto": con las dos confirmaciones ya hechas por quien llama, cierra la manga abierta marcándola
    /// como "terminó el tacto" (RF-7). Sin manga abierta no cierra nada. No bloquea el rodeo ni reinicia el avance.
    /// </summary>
    public async Task<Resultado<ResultadoCierreDto>> TerminarAsync(TerminarElTactoCommand c, CancellationToken ct)
    {
        var r = await CerrarAsync(c.RodeoId, c.CantidadReal, TipoCierreManga.TerminoElTacto, sinMangaEsError: false, ct);
        return await ConAvanceAsync(r, c.RodeoId, ct);
    }

    private Task<Resultado<CierreMangaDto?>> CerrarAsync(
        Guid rodeoId, int? cantidadReal, TipoCierreManga tipo, bool sinMangaEsError, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            if (cantidadReal is < 0)
                return Resultado.Error<CierreMangaDto?>(CodigoError.CantidadInvalida, MensajesCampo.Para(CodigoError.CantidadInvalida));

            var abierta = await _mangas.ObtenerAbiertaAsync(rodeoId, ct);
            if (abierta is null)
            {
                return sinMangaEsError
                    ? Resultado.Error<CierreMangaDto?>(CodigoError.NoHayMangaAbierta, MensajesCampo.Para(CodigoError.NoHayMangaAbierta))
                    : Resultado.Ok<CierreMangaDto?>(null); // "Terminar el tacto" sin manga abierta: no hay nada que cerrar
            }

            try
            {
                var contador = await _mangas.ContarVacasAsync(abierta.Id, ct);
                // "Coincide" = sin cantidad real: lo confirmado es lo que contó Aviss.
                var cierre = GuardarCierre(abierta.Id, tipo, _reloj.UtcAhora, contador, cantidadReal ?? contador);
                return Resultado.Ok<CierreMangaDto?>(ADto(cierre, abierta.Numero, contador));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<CierreMangaDto?>(ex); }
        }, ct);

    /// <summary>Arma el cierre y su historial (sin guardar: lo guarda la transacción del que llama).</summary>
    private CierreManga GuardarCierre(Guid mangaId, TipoCierreManga tipo, DateTime ahora, int cantidadAviss, int? cantidadConfirmada)
    {
        var cierre = CierreManga.Cerrar(mangaId, tipo, ahora, cantidadAviss, cantidadConfirmada, ahora);
        _mangas.AgregarCierre(cierre);
        _historial.Agregar(Historial.Crear("CierreManga", cierre.Id, "Tipo", null, tipo.ToString(), ahora, AccionHistorial.MangaCerrada));
        return cierre;
    }

    private async Task<Resultado<ResultadoCierreDto>> ConAvanceAsync(Resultado<CierreMangaDto?> r, Guid rodeoId, CancellationToken ct)
    {
        if (!r.EsCorrecto) return Resultado.Error<ResultadoCierreDto>(r.Codigo!.Value, r.Mensaje!);
        // El avance se calcula después de guardar: cerrar una manga no lo cambia, pero así queda siempre al día.
        return Resultado.Ok(new ResultadoCierreDto(r.Valor, await _avance.CalcularAsync(rodeoId, ct)));
    }

    // ------------------------------------------------------------------ Reabrir (deshacer un cierre)

    public Task<Resultado<ResultadoDeshacerDto>> ReabrirAsync(Guid cierreId, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            var cierre = await _mangas.ObtenerCierreAsync(cierreId, ct);
            if (cierre is null) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.NoEncontrado, "El cierre de manga no existe.");
            var manga = await _mangas.ObtenerAsync(cierre.MangaId, ct);
            if (manga is null) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.NoEncontrado, "La manga del cierre no existe.");

            // El servicio lee los datos y la regla pura decide (RF-9).
            var ultimoCierre = await _mangas.ObtenerUltimoCierreVigenteDelRodeoAsync(manga.RodeoId, ct);
            var abierta = await _mangas.ObtenerAbiertaAsync(manga.RodeoId, ct);
            var ahora = _reloj.UtcAhora;
            var motivo = ReglasManga.MotivoParaNoReabrir(new DatosReapertura(
                CierreAnulado: cierre.Anulado,
                EsElUltimoCierreVigenteDelRodeo: ultimoCierre?.Id == cierre.Id,
                TipoDeCierre: cierre.Tipo,
                FechaCierreUtc: cierre.FechaHoraUtc,
                AhoraUtc: ahora,
                Zona: _zona.Zona,
                HayOtraMangaAbierta: abierta is not null && abierta.Id != manga.Id));
            if (motivo is MotivoNoReabrir m)
            {
                var (codigo, mensaje) = MensajesCampo.Para(m);
                return Resultado.Error<ResultadoDeshacerDto>(codigo, mensaje);
            }

            try
            {
                cierre.MarcarAnulado(ahora); // el único cambio permitido sobre un cierre guardado: la manga vuelve a estar abierta
                _historial.Agregar(Historial.Crear("CierreManga", cierre.Id, "Anulado", "0", "1", ahora, AccionHistorial.AnulacionCierre));
                var contador = await _mangas.ContarVacasAsync(manga.Id, ct);
                return Resultado.Ok(new ResultadoDeshacerDto(TipoDeshacer.Cierre, null, manga.Id, manga.Numero, contador));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoDeshacerDto>(ex); }
        }, ct);

    internal static CierreMangaDto ADto(CierreManga cierre, int numeroManga, int contadorActual)
        => new(cierre.Id, cierre.MangaId, numeroManga, cierre.Tipo, cierre.FechaHoraUtc, cierre.CantidadAviss,
            cierre.CantidadConfirmada, cierre.Diferencia, contadorActual);
}
