using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Leer una caravana la marca sola (RF-11, RF-12, RF-22) y desmarcar (RF-26). Cada operación es UNA transacción:
/// marca + manga (si era por empezar) + historial se guardan juntos o no se guarda nada.
/// </summary>
internal sealed class MarcaPasadaService
{
    private readonly IUnitOfWork _uow;
    private readonly IAnimalRepository _animales;
    private readonly IRodeoRepository _rodeos;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEstadoReproductivoRepository _estados;
    private readonly IMangaRepository _mangas;
    private readonly IMarcaPasadaRepository _marcas;
    private readonly IRegistroCampoRepository _registros;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;
    private readonly Marcador _marcador;
    private readonly ArmadorFicha _ficha;
    private readonly IEsperaRepository _esperas;

    public MarcaPasadaService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos,
        IEstablecimientoRepository establecimientos, IUsuarioRepository usuarios, IEstadoReproductivoRepository estados,
        IMangaRepository mangas, IMarcaPasadaRepository marcas, IRegistroCampoRepository registros,
        IHistorialRepository historial, IRelojService reloj, IZonaHoraria zona, Marcador marcador, ArmadorFicha ficha,
        IEsperaRepository esperas)
    {
        _uow = uow; _animales = animales; _rodeos = rodeos; _establecimientos = establecimientos; _usuarios = usuarios;
        _estados = estados; _mangas = mangas; _marcas = marcas; _registros = registros; _historial = historial;
        _reloj = reloj; _zona = zona; _marcador = marcador; _ficha = ficha; _esperas = esperas;
    }

    public Task<Resultado<ResultadoLecturaDto>> ProcesarLecturaAsync(ProcesarLecturaCommand c, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
            if (errorUsuario is not null) return Resultado.Error<ResultadoLecturaDto>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

            var rodeo = await _rodeos.ObtenerAsync(c.RodeoId, ct);
            if (rodeo is null) return Resultado.Error<ResultadoLecturaDto>(CodigoError.NoEncontrado, "El rodeo no existe.");
            var establecimiento = await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
            if (establecimiento is null) return Resultado.Error<ResultadoLecturaDto>(CodigoError.NoEncontrado, "El establecimiento del rodeo no existe.");

            var codigo = c.Codigo ?? "";
            var animal = await BuscarAsync(c.RodeoId, codigo.Trim(), ct);
            if (animal is null)
            {
                // No existe en este rodeo: no se guarda nada; la pantalla ofrece alta o búsqueda manual (RF-24).
                return Resultado.Ok(new ResultadoLecturaDto(TipoResultadoLectura.NoEncontrada, codigo,
                    null, null, null, null, null, null, null, null, false));
            }

            // Una vaca en espera abre directo su ficha (spec 004, RF-5): sin aviso de repetida, sin marcarla otra vez ni sumarla al conteo.
            // Va antes que cualquier otra cosa: una vaca pendiente de veredicto siempre se abre.
            if (await _esperas.ObtenerPendienteAsync(animal.Id, ct) is not null)
            {
                var fichaEspera = await _ficha.ArmarAsync(animal, ct);
                return Resultado.Ok(new ResultadoLecturaDto(TipoResultadoLectura.EnEspera, codigo, animal.Id, animal.NumeroCaravana,
                    fichaEspera, null, null, null, null, null, false));
            }

            // Un único "ahora" para todo: el día local, la validación y la hora de la marca.
            var ahora = _reloj.UtcAhora;
            var zona = _zona.Zona;
            var hoy = DiaLocal.RangoUtcDeHoy(ahora, zona);

            // ¿Ya procesada hoy? Con una marca vigente de hoy, o con un tacto vigente de hoy (datos de la spec 001).
            var marcaDeHoy = await _marcas.ObtenerVigenteEnRangoAsync(animal.Id, hoy, ct);
            var mangaAbierta = await _mangas.ObtenerAbiertaAsync(c.RodeoId, ct);

            // Una manga que cruza la medianoche sigue abierta (RF-10): si la vaca ya está marcada en ELLA, aunque la marca
            // sea de ayer, no se la marca dos veces en la misma manga (la base tampoco lo admite, RF-5): es "repetida".
            if (marcaDeHoy is null && mangaAbierta is not null)
                marcaDeHoy = await _marcas.ObtenerVigenteEnMangaAsync(animal.Id, mangaAbierta.Id, ct);

            var procesada = marcaDeHoy is not null || await _registros.TieneTactoVigenteEnRangoAsync(animal.Id, hoy, ct);
            var ultimo = await _registros.ObtenerUltimoVigenteAsync(animal.Id, ct);

            if (procesada)
                return Resultado.Ok(await ArmarAvisoAsync(codigo, animal, marcaDeHoy, mangaAbierta, ultimo, ct));

            var motivo = ReglasMarcaPasada.Validar(new DatosValidacionMarca(
                AnimalAnulado: animal.Anulado,
                RodeoActivo: rodeo.Activo,
                EstablecimientoActivo: establecimiento.Activo,
                FechaHoraUtc: ahora,
                AhoraUtc: ahora,
                FechaUltimoRegistroVigenteUtc: ultimo?.FechaHoraUtc));
            if (motivo is MotivoRechazoMarca m)
            {
                var (cod, mensaje) = MensajesCampo.Para(m);
                return Resultado.Error<ResultadoLecturaDto>(cod, mensaje);
            }

            try
            {
                var preparada = await _marcador.MarcarAsync(animal.Id, c.RodeoId, ahora, c.UsuarioId, ct);

                // CVET 0: la vaca no entra en tacto. Igual queda marcada (suma al conteo de la manga y se puede deshacer),
                // pero no se arma ni se devuelve la ficha: la pantalla solo hace sonar la chicharra y sigue con la próxima.
                if (!Aviss.Domain.Reglas.ReglasCvet.EntraEnTacto(ConstructorFicha.LeerCvet(animal.DatosOriginalesJson)))
                    return Resultado.Ok(new ResultadoLecturaDto(TipoResultadoLectura.SinTacto, codigo, animal.Id, animal.NumeroCaravana,
                        null, preparada.Marca.Id, preparada.Manga.Numero, preparada.Contador, null, null, false));

                var ficha = await _ficha.ArmarAsync(animal, ct);
                return Resultado.Ok(new ResultadoLecturaDto(TipoResultadoLectura.Marcada, codigo, animal.Id, animal.NumeroCaravana,
                    ficha, preparada.Marca.Id, preparada.Manga.Numero, preparada.Contador, null, null, false));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoLecturaDto>(ex); }
        }, ct);

    public Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(Guid marcaId, Guid usuarioId, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            var marca = await _marcas.ObtenerAsync(marcaId, ct);
            if (marca is null) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.NoEncontrado, "La marca de pasada no existe.");
            if (marca.Anulado) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.MarcaYaAnulada, MensajesCampo.Para(CodigoError.MarcaYaAnulada));

            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, usuarioId, ct);
            if (errorUsuario is not null) return Resultado.Error<ResultadoDeshacerDto>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

            // Una vaca en espera no se desmarca: primero hay que resolver o saltear la espera (spec 004, RF-9).
            if (!ReglasEspera.PuedeDesmarcarse(await _esperas.ObtenerPendienteAsync(marca.AnimalId, ct) is not null))
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.VacaEnEspera, MensajesCampo.Para(CodigoError.VacaEnEspera));

            // Solo se desmarca si la marca es el último registro vigente de la vaca, de cualquier tipo (RF-26).
            var ultimo = await _registros.ObtenerUltimoVigenteAsync(marca.AnimalId, ct);
            if (ultimo is null || ultimo.Tipo != TipoRegistroCampo.Marca || ultimo.Id != marca.Id)
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.MarcaNoEsLaUltima, MensajesCampo.Para(CodigoError.MarcaNoEsLaUltima));

            try
            {
                var ahora = _reloj.UtcAhora;
                var manga = await _mangas.ObtenerAsync(marca.MangaId, ct);
                var contador = await _mangas.ContarVacasAsync(marca.MangaId, ct) - 1; // todavía cuenta esta marca
                marca.MarcarAnulada(ahora); // el único cambio que la base permite sobre una marca guardada
                _historial.Agregar(Historial.Crear("MarcaPasada", marca.Id, "Anulado", "0", "1", ahora, usuarioId, AccionHistorial.AnulacionMarca));
                return Resultado.Ok(new ResultadoDeshacerDto(TipoDeshacer.Marca, marca.AnimalId, marca.MangaId, manga?.Numero, contador));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoDeshacerDto>(ex); }
        }, ct);

    /// <summary>
    /// Primero por número de caravana; si no hay, por identificador electrónico (RF-11). Un código que no sirve como
    /// caravana (por ejemplo, de más de 20 caracteres) igual se prueba como identificador.
    /// </summary>
    private async Task<Animal?> BuscarAsync(Guid rodeoId, string codigo, CancellationToken ct)
    {
        if (codigo.Length == 0) return null;
        Animal? animal = null;
        if (Caravana.TryCrear(codigo, out var caravana, out _))
            animal = await _animales.ObtenerPorCaravanaAsync(rodeoId, caravana, ct);
        if (animal is null && IdElectronico.TryCrear(codigo, out var id, out _))
            animal = await _animales.ObtenerPorIdElectronicoAsync(rodeoId, id.Valor, ct);
        return animal;
    }

    /// <summary>El aviso de una vaca ya procesada hoy: no se guarda nada (RF-22).</summary>
    private async Task<ResultadoLecturaDto> ArmarAvisoAsync(
        string codigo, Animal animal, MarcaPasada? marcaDeHoy, Manga? mangaActual, RegistroCampoVigente? ultimo, CancellationToken ct)
    {
        var aviso = ReglasAviso.Clasificar(marcaDeHoy?.MangaId, mangaActual?.Id);

        // La manga que se dice es la de la marca vigente de hoy; sin marca (solo un tacto) no hay manga registrada.
        int? numeroManga = marcaDeHoy is null ? null : (await _mangas.ObtenerAsync(marcaDeHoy.MangaId, ct))?.Numero;

        UltimoRegistroDto? ultimoDto = null;
        if (ultimo is not null)
        {
            string? estado = null;
            if (ultimo.EstadoId is Guid estadoId)
                estado = (await _estados.ObtenerAsync(estadoId, ct))?.Nombre;
            ultimoDto = new UltimoRegistroDto(ultimo.Tipo, ultimo.Id, ultimo.FechaHoraUtc, estado, numeroManga, ultimo.Observaciones);
        }

        var tipo = aviso == TipoAviso.Repetido ? TipoResultadoLectura.Repetido : TipoResultadoLectura.YaPaso;
        // "Corregir" solo cuando lo último no es la marca de pasada (si lo es, basta con "Seguir").
        var puedeCorregir = ultimo is not null && ultimo.Tipo != TipoRegistroCampo.Marca;
        return new ResultadoLecturaDto(tipo, codigo, animal.Id, animal.NumeroCaravana, null, null, null, null,
            aviso, ultimoDto, puedeCorregir);
    }
}
