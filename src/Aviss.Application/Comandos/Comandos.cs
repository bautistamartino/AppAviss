using Aviss.Domain.Reglas;

namespace Aviss.Application.Comandos;

// Comandos: los datos que entran a cada caso de uso. Todos llevan el UsuarioId explícito
// (todavía no existe "usuario actual"; "Sin asignar" tiene Id fijo, ver plan).

public sealed record CrearRodeoCommand(
    Guid EstablecimientoId,
    string Nombre,
    DateTime FechaImportacionUtc,
    string ArchivoOrigenNombre,
    string ArchivoOrigenHash,
    byte[] ArchivoOrigenBlob,
    string PerfilExcel);

/// <summary>
/// El tacto no trae fecha: la hora la toma el servicio de su reloj (RF-8b).
/// <see cref="EstadoAnteriorEsperado"/>: nulo = no informado (se toma el actual del animal);
/// si no es nulo, lleva el estado que la pantalla creía que tenía el animal (o vacío si creía que no tenía).
/// </summary>
public sealed record RegistrarTactoCommand(
    Guid AnimalId,
    Guid EstadoNuevoId,
    int? DiasPrenez,
    string? Observaciones,
    Guid UsuarioId,
    EstadoEsperado? EstadoAnteriorEsperado = null);

public sealed record AnularTactoCommand(Guid TactoId, Guid UsuarioId);

public sealed record AltaEnCampoCommand(
    Guid RodeoId,
    string NumeroCaravana,
    string? IdElectronico,
    string? Observaciones,
    Guid UsuarioId);

/// <summary>Trae los valores que deben quedar; el servicio compara con los actuales y registra solo lo que cambió.</summary>
public sealed record EditarAnimalCommand(
    Guid AnimalId,
    string NumeroCaravana,
    string? IdElectronico,
    string? Observaciones,
    Guid UsuarioId);

public sealed record AnularAltaCommand(Guid AnimalId, Guid UsuarioId);
