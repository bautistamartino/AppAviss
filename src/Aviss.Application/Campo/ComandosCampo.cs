namespace Aviss.Application.Campo;

// Comandos del flujo de campo (spec 003). La hora la toma el servicio del reloj.

/// <summary>Una lectura del bastón o una búsqueda manual: el código es la caravana (o, si no coincide, el identificador electrónico).</summary>
public sealed record ProcesarLecturaCommand(Guid RodeoId, string Codigo);

/// <summary><see cref="CantidadReal"/> nula = "Coincide" (la cantidad confirmada es la que contó Aviss).</summary>
public sealed record FinalizarMangaCommand(Guid RodeoId, int? CantidadReal);

public sealed record TerminarElTactoCommand(Guid RodeoId, int? CantidadReal);

public sealed record DeshacerCommand(TipoDeshacer Tipo, Guid Id);

// ---- En Espera y Rechazo (spec 004) ----

/// <summary>Poner en espera a una vaca (RF-1). La fecha y hora las toma el servicio del reloj.</summary>
public sealed record PonerEnEsperaCommand(Guid AnimalId);

/// <summary>Saltear la espera pendiente de una vaca: no hacía falta cargar un evento (RF-3).</summary>
public sealed record SaltearEsperaCommand(Guid AnimalId);

/// <summary>
/// Un "solo evento" (spec 005): un registro que no cambia el estado de la vaca. <see cref="EventoId"/> es un evento del catálogo de tipo
/// SoloEvento (OK, Aborto, Revisar Nuevamente) o <see cref="EventosCampo.Rechazo"/> (el Rechazo fijo de la spec 004). <see cref="Observaciones"/> es el motivo o la nota.
/// </summary>
/// <remarks>Segunda vuelta: con <see cref="Eleccion"/> (por ejemplo un Aborto con resultado Rechazo y sus días) el <c>EventoId</c> debe ser el mismo que el de la elección; el servicio lo revalida.</remarks>
public sealed record RegistrarSoloEventoCommand(Guid AnimalId, Guid EventoId, string? Observaciones, EleccionEvento? Eleccion = null);

public sealed record AnularRechazoCommand(Guid RechazoId);

/// <summary>
/// "Editar" un movimiento (RF-8): se anula el registro (un tacto o un rechazo) y se guarda uno nuevo. <see cref="Evento"/> es el
/// Id de un evento del catálogo (un estado guarda un tacto; un solo evento guarda un registro sin cambio de estado) o <see cref="EventosCampo.Rechazo"/> (se guarda un rechazo).
/// </summary>
public sealed record CorregirRegistroCommand(Guid RegistroId, Guid Evento, string? Observaciones, EleccionEvento? Eleccion = null);

/// <summary>Eventos fijos de la lista de eventos de la ficha que no pertenecen al catálogo de estados.</summary>
public static class EventosCampo
{
    /// <summary>El evento "Rechazo": un Guid fijo y conocido (no es una fila del catálogo, spec 004 decisión 1).</summary>
    public static readonly Guid Rechazo = new("5EC4A200-0000-0000-0000-000000000001");

    /// <summary>Cómo se muestra el Rechazo fijo en la lista de "Otro evento", el casillero, el historial, los movimientos y los avisos (decisión del 2026-11-24).</summary>
    public const string NombreRechazo = "RECHAZO";
}
