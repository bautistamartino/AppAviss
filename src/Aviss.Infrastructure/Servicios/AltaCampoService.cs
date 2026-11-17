using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Alta en campo de una vaca no encontrada, con su marca en la manga actual (RF-25), y su anulación junto con la
/// marca (RF-29). Todo en una única transacción.
/// </summary>
internal sealed class AltaCampoService
{
    private readonly IUnitOfWork _uow;
    private readonly AnimalService _animalService;
    private readonly IAnimalRepository _animales;
    private readonly IUsuarioRepository _usuarios;
    private readonly IMarcaPasadaRepository _marcas;
    private readonly IMangaRepository _mangas;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;
    private readonly Marcador _marcador;
    private readonly ArmadorFicha _ficha;

    public AltaCampoService(IUnitOfWork uow, AnimalService animalService, IAnimalRepository animales, IUsuarioRepository usuarios,
        IMarcaPasadaRepository marcas, IMangaRepository mangas, IHistorialRepository historial, IRelojService reloj, Marcador marcador, ArmadorFicha ficha)
    {
        _uow = uow; _animalService = animalService; _animales = animales; _usuarios = usuarios; _marcas = marcas; _mangas = mangas;
        _historial = historial; _reloj = reloj; _marcador = marcador; _ficha = ficha;
    }

    public Task<Resultado<ResultadoAltaDto>> AltaConMarcaAsync(AltaConMarcaCommand c, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            // Si la caravana tipeada difiere del código leído, el código leído es el identificador electrónico (RF-25).
            var id = ReglasAltaCampo.IdElectronicoDeLectura(c.NumeroCaravana, c.CodigoLeido);

            // El núcleo del alta valida todo (usuario, rodeo, caravana, repetidos) sin abrir otra transacción.
            var alta = await _animalService.CrearAltaAsync(
                new AltaEnCampoCommand(c.RodeoId, c.NumeroCaravana, id.IdElectronico, c.Observaciones, c.UsuarioId), ct);
            if (!alta.EsCorrecto) return Resultado.Error<ResultadoAltaDto>(alta.Codigo!.Value, alta.Mensaje!);
            var animal = alta.Valor;

            try
            {
                // Una alta sin estado ni tacto igual pasa por la manga: queda marcada (y abre la manga si era por empezar).
                var marca = await _marcador.MarcarAsync(animal.Id, c.RodeoId, _reloj.UtcAhora, c.UsuarioId, ct);
                var ficha = await _ficha.ArmarAsync(animal, ct);
                return Resultado.Ok(new ResultadoAltaDto(animal.Id, ficha, marca.Marca.Id, marca.Manga.Numero, marca.Contador, id.OmitidoPorFormato));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoAltaDto>(ex); }
        }, ct);

    /// <summary>
    /// Anula un alta en campo (RF-29). Si la vaca tiene tactos vigentes u otras marcas además de la del alta, se
    /// rechaza (primero hay que deshacerlos); si solo tiene su marca, se anulan la marca y el alta juntas.
    /// </summary>
    public Task<Resultado<ResultadoDeshacerDto>> AnularAltaAsync(Guid animalId, Guid usuarioId, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            var animal = await _animales.ObtenerAsync(animalId, ct);
            if (animal is null) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.NoEncontrado, "El animal no existe.");
            if (animal.Origen != OrigenAnimal.AltaEnCampo)
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.AnimalImportadoNoSeAnula, "Un animal importado no puede anularse.");
            if (animal.Anulado) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.AnimalAnulado, "El animal ya está anulado.");

            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, usuarioId, ct);
            if (errorUsuario is not null) return Resultado.Error<ResultadoDeshacerDto>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

            if (await _animales.TieneTactosVigentesAsync(animal.Id, ct))
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.AnimalConTactosVigentes, "El animal tiene tactos vigentes: anulá primero esos tactos.");
            var marcas = await _marcas.ListarVigentesDeAnimalAsync(animal.Id, ct);
            if (marcas.Count > 1)
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.AnimalConMarcasVigentes, MensajesCampo.Para(CodigoError.AnimalConMarcasVigentes));

            try
            {
                var ahora = _reloj.UtcAhora;
                Guid? mangaId = null;
                if (marcas.Count == 1)
                {
                    // La marca del alta se anula junto con el alta, en esta misma transacción.
                    marcas[0].MarcarAnulada(ahora);
                    mangaId = marcas[0].MangaId;
                    _historial.Agregar(Historial.Crear("MarcaPasada", marcas[0].Id, "Anulado", "0", "1", ahora, usuarioId, AccionHistorial.AnulacionMarca));
                }
                animal.Anular(ahora);
                _historial.Agregar(Historial.Crear("Animal", animal.Id, "Anulado", "0", "1", ahora, usuarioId, AccionHistorial.AnulacionAlta));

                // El contador de la manga ya no cuenta esa marca. La anulación todavía no está guardada, así que se resta a mano
                // lo que la base todavía cuenta.
                int? contador = null;
                int? numero = null;
                if (mangaId is Guid m)
                {
                    contador = (await _mangas.ContarVacasAsync(m, ct)) - 1;
                    numero = (await _mangas.ObtenerAsync(m, ct))?.Numero;
                }
                return Resultado.Ok(new ResultadoDeshacerDto(TipoDeshacer.Alta, animal.Id, mangaId, numero, contador));
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoDeshacerDto>(ex); }
        }, ct);

}
