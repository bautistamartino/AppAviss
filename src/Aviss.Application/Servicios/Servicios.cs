using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Domain.Entidades;

namespace Aviss.Application.Servicios;

// Casos de uso. Cada método es UNA transacción y devuelve un Resultado: los rechazos de negocio no lanzan excepciones.
// La implementación vive en Infrastructure (tarea T3).

public interface IEstablecimientoService
{
    Task<Resultado<Guid>> CrearAsync(string nombre, string? notas, CancellationToken ct = default);
    Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default);
    Task<Resultado> ReactivarAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Establecimiento>> ListarAsync(CancellationToken ct = default);
}

public interface IRodeoService
{
    Task<Resultado<Guid>> CrearAsync(CrearRodeoCommand comando, CancellationToken ct = default);
    Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default);
    Task<int> ContarAnimalesAsync(Guid rodeoId, CancellationToken ct = default);
}

public interface IUsuarioService
{
    Task<Resultado<Guid>> CrearAsync(string nombre, string? matricula, CancellationToken ct = default);
    Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default);
    Task<Resultado> ReactivarAsync(Guid id, CancellationToken ct = default);
}

public interface ICatalogoEstadosService
{
    Task<Resultado<Guid>> CrearAsync(string codigo, string nombre, string codigoDairyComp, CancellationToken ct = default);
    Task<Resultado> ModificarAsync(Guid id, string nombre, string codigoDairyComp, CancellationToken ct = default);
    Task<Resultado> DesactivarAsync(Guid id, CancellationToken ct = default);
    Task<Resultado> ActivarAsync(Guid id, CancellationToken ct = default);
}

public interface IAnimalService
{
    Task<Resultado<Guid>> AltaEnCampoAsync(AltaEnCampoCommand comando, CancellationToken ct = default);
    Task<Resultado> EditarAsync(EditarAnimalCommand comando, CancellationToken ct = default);
    Task<Resultado> AnularAltaAsync(AnularAltaCommand comando, CancellationToken ct = default);
}

/// <summary>
/// Importación de un .xls (spec 002). Dos tiempos: <see cref="PrepararAsync"/> lee, valida y arma una vista previa en memoria
/// (sin guardar nada); <see cref="ConfirmarAsync"/> la guarda en una sola transacción. Se confirma una sola vez.
/// </summary>
public interface IImportacionService
{
    /// <summary>
    /// Lee el flujo UNA vez (tope 25 MB), calcula la huella SHA-256 (minúscula) y valida en el orden de RF-2.
    /// Si rechaza el archivo devuelve error con código <see cref="CodigoError.ArchivoRechazado"/> y el mensaje del motivo.
    /// </summary>
    Task<Resultado<VistaPreviaImportacion>> PrepararAsync(string nombreArchivo, Stream contenido, CancellationToken ct = default);

    Task<IReadOnlyList<EstablecimientoResumen>> ListarEstablecimientosActivosAsync(CancellationToken ct = default);

    Task<Resultado<ResultadoImportacion>> ConfirmarAsync(ConfirmarImportacionCommand comando, CancellationToken ct = default);

    /// <summary>Descarta la vista previa sin guardar nada (RF-22).</summary>
    void Descartar(Guid vistaPreviaId);
}

public interface ITrabajoCampoService
{
    /// <summary>Registra un tacto: animal + tacto + historial en una sola transacción (RF-8).</summary>
    Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand comando, CancellationToken ct = default);

    /// <summary>Anula el último tacto vigente de un animal con otro registro (RF-13, RF-14). Devuelve el Id del tacto de anulación.</summary>
    Task<Resultado<Guid>> AnularTactoAsync(AnularTactoCommand comando, CancellationToken ct = default);
}

/// <summary>
/// La fachada del trabajo de campo (spec 003): lo único que usan los ViewModels. Es un singleton que abre un scope
/// (un contexto y una transacción) por operación, igual que <see cref="IImportacionService"/>: los ViewModels viven
/// toda la vida de la app y no pueden compartir un DbContext. Los rechazos de negocio vuelven como
/// <see cref="Resultado"/> con un mensaje fijo en español; las fallas reales (disco, base) lanzan excepción.
/// </summary>
public interface ICampoService
{
    // ---- Preparar el trabajo (RF-1, RF-2) ----
    Task<IReadOnlyList<RodeoParaTrabajarDto>> ListarRodeosParaTrabajarAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosActivosAsync(CancellationToken ct = default);
    Task<Resultado<Guid>> CrearVeterinarioAsync(string nombre, string? matricula, CancellationToken ct = default);
    Task<IReadOnlyList<EstadoActivoDto>> ListarEstadosActivosAsync(CancellationToken ct = default);

    // ---- Mangas (RF-4 a RF-10) ----
    /// <summary>Empieza a trabajar un rodeo: cierra como automática la manga abierta de un día anterior y devuelve el estado.</summary>
    Task<Resultado<EstadoTrabajoDto>> IniciarTrabajoAsync(Guid rodeoId, Guid usuarioId, CancellationToken ct = default);
    /// <summary>Manga actual, contador y avance de hoy (se vuelve a pedir cada 60 s y al volver de segundo plano).</summary>
    Task<Resultado<EstadoTrabajoDto>> ObtenerEstadoTrabajoAsync(Guid rodeoId, CancellationToken ct = default);
    /// <summary>Contador y caravanas de la manga abierta, para la verificación de "Finalizar manga". Sin manga abierta: <c>NoHayMangaAbierta</c>.</summary>
    Task<Resultado<ResumenMangaDto>> ObtenerResumenMangaAsync(Guid rodeoId, CancellationToken ct = default);
    Task<Resultado<ResultadoCierreDto>> FinalizarMangaAsync(FinalizarMangaCommand comando, CancellationToken ct = default);
    Task<Resultado<ResultadoCierreDto>> TerminarElTactoAsync(TerminarElTactoCommand comando, CancellationToken ct = default);

    // ---- Lectura, ficha y tacto (RF-11 a RF-19, RF-22 a RF-25) ----
    Task<Resultado<ResultadoLecturaDto>> ProcesarLecturaAsync(ProcesarLecturaCommand comando, CancellationToken ct = default);
    Task<Resultado<FichaAnimalDto>> ObtenerFichaAsync(Guid animalId, CancellationToken ct = default);
    Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand comando, CancellationToken ct = default);
    Task<Resultado<ResultadoAltaDto>> AltaEnCampoConMarcaAsync(AltaConMarcaCommand comando, CancellationToken ct = default);

    // ---- Deshacer y movimientos (RF-26 a RF-29) ----
    Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(DeshacerCommand comando, CancellationToken ct = default);
    Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct = default);

    /// <summary>Una caravana real del rodeo para el panel del simulador (solo desarrollo). Nulo si no hay.</summary>
    Task<string?> CaravanaDeMuestraAsync(Guid rodeoId, MuestraCaravana tipo, CancellationToken ct = default);
}
