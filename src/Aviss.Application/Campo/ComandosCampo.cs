namespace Aviss.Application.Campo;

// Comandos del flujo de campo (spec 003). Todos llevan el veterinario activo (UsuarioId); la hora la toma el servicio del reloj.

/// <summary>Una lectura del bastón o una búsqueda manual: el código es la caravana (o, si no coincide, el identificador electrónico).</summary>
public sealed record ProcesarLecturaCommand(Guid RodeoId, string Codigo, Guid UsuarioId);

/// <summary><see cref="CantidadReal"/> nula = "Coincide" (la cantidad confirmada es la que contó Aviss).</summary>
public sealed record FinalizarMangaCommand(Guid RodeoId, int? CantidadReal, Guid UsuarioId);

public sealed record TerminarElTactoCommand(Guid RodeoId, int? CantidadReal, Guid UsuarioId);

public sealed record DeshacerCommand(TipoDeshacer Tipo, Guid Id, Guid UsuarioId);

// ---- En Espera y Rechazo (spec 004) ----

/// <summary>Poner en espera a una vaca (RF-1). La fecha y hora las toma el servicio del reloj.</summary>
public sealed record PonerEnEsperaCommand(Guid AnimalId, Guid UsuarioId);

/// <summary>Saltear la espera pendiente de una vaca: no hacía falta cargar un evento (RF-3).</summary>
public sealed record SaltearEsperaCommand(Guid AnimalId, Guid UsuarioId);

/// <summary>La marca de rechazo de una vaca; <see cref="Observaciones"/> es el motivo escrito a mano (RF-6).</summary>
public sealed record RegistrarRechazoCommand(Guid AnimalId, string? Observaciones, Guid UsuarioId);

public sealed record AnularRechazoCommand(Guid RechazoId, Guid UsuarioId);

/// <summary>
/// "Editar" un movimiento (RF-8): se anula el registro (un tacto o un rechazo) y se guarda uno nuevo. <see cref="Evento"/> es el
/// Id de un estado del catálogo (se guarda un tacto) o <see cref="EventosCampo.Rechazo"/> (se guarda un rechazo).
/// </summary>
public sealed record CorregirRegistroCommand(Guid RegistroId, Guid Evento, string? Observaciones, Guid UsuarioId);

/// <summary>Eventos fijos de la lista de eventos de la ficha que no pertenecen al catálogo de estados.</summary>
public static class EventosCampo
{
    /// <summary>El evento "Rechazo": un Guid fijo y conocido (no es una fila del catálogo, spec 004 decisión 1).</summary>
    public static readonly Guid Rechazo = new("5EC4A200-0000-0000-0000-000000000001");
}
