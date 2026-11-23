using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Puertos;

// Puertos: interfaces que Application necesita y que Infrastructure implementa.
// Todos los métodos de repositorio trabajan dentro de la transacción que abre IUnitOfWork.

/// <summary>La hora actual en UTC. Existe como puerto para poder fijarla en los tests (RF-17).</summary>
public interface IRelojService
{
    DateTime UtcAhora { get; }
}

/// <summary>
/// Ejecuta una operación completa dentro de UNA transacción: si termina bien confirma todo, si lanza una excepción
/// revierte todo (RF-8, RF-13). Un contexto y una transacción por operación.
/// </summary>
public interface IUnitOfWork
{
    Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct = default);
    Task EjecutarAsync(Func<Task> operacion, CancellationToken ct = default);
}

public interface IEstablecimientoRepository
{
    Task<Establecimiento?> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Establecimiento>> ListarAsync(CancellationToken ct = default);
    void Agregar(Establecimiento establecimiento);
}

public interface IRodeoRepository
{
    Task<Rodeo?> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Rodeo>> ListarPorEstablecimientoAsync(Guid establecimientoId, CancellationToken ct = default);
    /// <summary>Cantidad de animales no anulados del rodeo (incluye las altas en campo).</summary>
    Task<int> ContarAnimalesAsync(Guid rodeoId, CancellationToken ct = default);
    /// <summary>
    /// Rodeos guardados con esa huella SHA-256 (de cualquier establecimiento), más recientes primero (RF-16).
    /// Devuelve solo nombre y fecha: no trae el archivo guardado.
    /// </summary>
    Task<IReadOnlyList<RodeoImportadoResumen>> BuscarPorHashAsync(string hash, CancellationToken ct = default);
    void Agregar(Rodeo rodeo);
}

public interface IEstadoReproductivoRepository
{
    Task<EstadoReproductivo?> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<EstadoReproductivo?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<EstadoReproductivo>> ListarAsync(CancellationToken ct = default);
    /// <summary>¿Hay otro estado con el mismo nombre (sin distinguir mayúsculas)? <paramref name="excluirId"/> es el estado que se está editando.</summary>
    Task<bool> ExisteNombreAsync(string nombre, Guid? excluirId, CancellationToken ct = default);
    /// <summary>¿Hay otro estado ACTIVO con el mismo código de Dairy Comp?</summary>
    Task<bool> ExisteCodigoDairyCompActivoAsync(string codigoDairyComp, Guid? excluirId, CancellationToken ct = default);
    void Agregar(EstadoReproductivo estado);
}

public interface IAnimalRepository
{
    Task<Animal?> ObtenerAsync(Guid id, CancellationToken ct = default);
    /// <summary>Busca entre los animales NO anulados del rodeo, por la clave normalizada de la caravana.</summary>
    Task<Animal?> ObtenerPorCaravanaAsync(Guid rodeoId, Caravana caravana, CancellationToken ct = default);
    /// <summary>¿Otro animal no anulado del rodeo ya tiene ese identificador electrónico?</summary>
    Task<bool> ExisteIdElectronicoAsync(Guid rodeoId, string idElectronico, Guid? excluirAnimalId, CancellationToken ct = default);
    Task<bool> TieneTactosVigentesAsync(Guid animalId, CancellationToken ct = default);
    /// <summary>¿Tiene el animal alguna marca de pasada vigente? (spec 003)</summary>
    Task<bool> TieneMarcasVigentesAsync(Guid animalId, CancellationToken ct = default);
    /// <summary>Busca entre los animales NO anulados del rodeo por identificador electrónico (spec 003, RF-11).</summary>
    Task<Animal?> ObtenerPorIdElectronicoAsync(Guid rodeoId, string idElectronico, CancellationToken ct = default);
    void Agregar(Animal animal);
    /// <summary>Marca muchos animales de una vez (importación). Se guardan con el SaveChanges de la transacción.</summary>
    void AgregarVarios(IEnumerable<Animal> animales);
}

public interface ITactoRepository
{
    Task<Tacto?> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<Tacto?> ObtenerUltimoVigenteAsync(Guid animalId, CancellationToken ct = default);
    /// <summary>Último vigente del animal sin contar el tacto indicado (el que se está anulando).</summary>
    Task<Tacto?> ObtenerVigenteAnteriorAsync(Guid animalId, Guid excluirTactoId, CancellationToken ct = default);
    /// <summary>Todos los tactos del animal (sin seguimiento de cambios), para armar el historial de la ficha.</summary>
    Task<IReadOnlyList<Tacto>> ListarPorAnimalAsync(Guid animalId, CancellationToken ct = default);
    /// <summary>Orden de guardado compartido con las marcas de pasada (máximo de los dos + 1); se llama dentro de la transacción.</summary>
    Task<long> SiguienteOrdenInsercionAsync(CancellationToken ct = default);
    void Agregar(Tacto tacto);
}

/// <summary>El historial solo se agrega: no hay métodos para leer-modificar ni borrar (RF-11).</summary>
public interface IHistorialRepository
{
    void Agregar(Historial registro);
}

/// <summary>Por qué se hace una copia de seguridad; queda en el nombre del archivo.</summary>
public enum MotivoBackup { PreviaActualizacion, APedido }

public interface IBackupService
{
    /// <summary>Hace una copia completa y verificada de la base, y rota dejando las últimas 10 (RF-21).</summary>
    Task<Resultado> CrearAsync(MotivoBackup motivo, CancellationToken ct = default);
}

public interface IArranqueService
{
    /// <summary>Crea o actualiza la base al abrir la app (RF-19, RF-20, RF-22, RF-27) y deja el resultado en <see cref="IArranqueResultado"/>.</summary>
    Task<ResultadoArranque> IniciarAsync(CancellationToken ct = default);
}

/// <summary>Un archivo elegido por el veterinario: su nombre y un flujo de lectura (en Android no hay ruta, es un content://).</summary>
public sealed record ArchivoElegido(string Nombre, Stream Contenido);

/// <summary>Selector de archivos de la plataforma (en la app, el de Android). Existe como puerto para probar los ViewModels.</summary>
public interface ISelectorArchivo
{
    /// <summary>Devuelve el archivo elegido o nulo si el veterinario cancela.</summary>
    Task<ArchivoElegido?> ElegirAsync(CancellationToken ct = default);
}

/// <summary>Resultado del último arranque, para que una pantalla lo muestre (RF-28).</summary>
public interface IArranqueResultado
{
    /// <summary>Nulo mientras el arranque no terminó.</summary>
    ResultadoArranque? Actual { get; }
}
