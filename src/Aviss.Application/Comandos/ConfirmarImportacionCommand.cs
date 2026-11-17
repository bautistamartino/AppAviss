namespace Aviss.Application.Comandos;

/// <summary>
/// Confirmar una vista previa. Se elige un establecimiento existente (<see cref="EstablecimientoId"/>) o se escribe uno
/// nuevo (<see cref="NuevoEstablecimientoNombre"/>); si vienen los dos, manda el existente.
/// <see cref="ConfirmaImportarArchivoRepetido"/>: el veterinario aceptó importar de nuevo un archivo con la misma huella (RF-16).
/// </summary>
public sealed record ConfirmarImportacionCommand(
    Guid VistaPreviaId,
    Guid? EstablecimientoId,
    string? NuevoEstablecimientoNombre,
    string NombreRodeo,
    bool ConfirmaImportarArchivoRepetido);
