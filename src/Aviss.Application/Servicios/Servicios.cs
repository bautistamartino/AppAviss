using Aviss.Application.Comandos;
using Aviss.Application.Comun;
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

public interface ITrabajoCampoService
{
    /// <summary>Registra un tacto: animal + tacto + historial en una sola transacción (RF-8).</summary>
    Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand comando, CancellationToken ct = default);

    /// <summary>Anula el último tacto vigente de un animal con otro registro (RF-13, RF-14). Devuelve el Id del tacto de anulación.</summary>
    Task<Resultado<Guid>> AnularTactoAsync(AnularTactoCommand comando, CancellationToken ct = default);
}
