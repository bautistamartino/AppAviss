namespace Aviss.Application.Campo;

// Comandos del flujo de campo (spec 003). Todos llevan el veterinario activo (UsuarioId); la hora la toma el servicio del reloj.

/// <summary>Una lectura del bastón o una búsqueda manual: el código es la caravana (o, si no coincide, el identificador electrónico).</summary>
public sealed record ProcesarLecturaCommand(Guid RodeoId, string Codigo, Guid UsuarioId);

/// <summary>
/// Alta de una vaca no encontrada, con su marca en la manga actual. <see cref="CodigoLeido"/> es el código que leyó el
/// lector (nulo si viene de "Buscar a mano"): si difiere de la caravana tipeada, se guarda como identificador electrónico.
/// </summary>
public sealed record AltaConMarcaCommand(Guid RodeoId, string NumeroCaravana, string? CodigoLeido, string? Observaciones, Guid UsuarioId);

/// <summary><see cref="CantidadReal"/> nula = "Coincide" (la cantidad confirmada es la que contó Aviss).</summary>
public sealed record FinalizarMangaCommand(Guid RodeoId, int? CantidadReal, Guid UsuarioId);

public sealed record TerminarElTactoCommand(Guid RodeoId, int? CantidadReal, Guid UsuarioId);

public sealed record DeshacerCommand(TipoDeshacer Tipo, Guid Id, Guid UsuarioId);
