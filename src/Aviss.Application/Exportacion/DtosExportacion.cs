using Aviss.Application.Comun;
using Aviss.Domain.Enums;

namespace Aviss.Application.Exportacion;

/// <summary>Una fila de la lista de la pantalla "Exportar": un rodeo activo con su establecimiento y su cantidad de vacas (RF-1).</summary>
public sealed record RodeoParaExportarDto(Guid RodeoId, string RodeoNombre, Guid EstablecimientoId, string EstablecimientoNombre, int CantidadVacas);

/// <summary>La última exportación de un rodeo (RF-2).</summary>
public sealed record UltimaExportacionDto(DateTime FechaUtc, string ArchivoNombre, FormatoExportacion Formato, int CantidadVacasConEvento);

/// <summary>
/// El resumen previo a generar (RF-2 y RF-11): cuántas vacas llevan evento, cuántas no, cuántas con CVET 0 tienen registro
/// (salen vacías), el día de la jornada y la última exportación.
/// </summary>
public sealed record ResumenExportacionDto(
    Guid RodeoId, int TotalVacas, int VacasConEvento, int VacasSinEvento, int VacasCvetCeroConRegistro,
    DateOnly? Jornada, UltimaExportacionDto? UltimaExportacion);

/// <summary>
/// Lo que devuelve una exportación bien hecha. <paramref name="ConstanciaGuardada"/> es falso si el archivo quedó guardado pero la
/// constancia no se pudo registrar (nada de lo guardado se pierde, RF-22): la pantalla lo avisa.
/// </summary>
public sealed record ExportacionEntregadaDto(
    ArchivoGuardado Archivo, FormatoExportacion Formato, int Filas, int VacasConEvento, bool ConstanciaGuardada);

/// <summary>El caso de uso "Exportar" (spec 007): listar rodeos, resumen y generar y entregar el archivo de vuelta.</summary>
public interface IExportacionService
{
    /// <summary>Los rodeos activos con su establecimiento y su cantidad de vacas, ordenados por establecimiento y rodeo (RF-1).</summary>
    Task<IReadOnlyList<RodeoParaExportarDto>> ListarRodeosAsync(CancellationToken ct = default);

    /// <summary>El resumen de un rodeo (RF-2). <see cref="CodigoError.NoEncontrado"/> si no existe o está de baja (RF-23).</summary>
    Task<Resultado<ResumenExportacionDto>> ObtenerResumenAsync(Guid rodeoId, CancellationToken ct = default);

    /// <summary>
    /// Genera, valida y entrega el archivo, y recién entonces deja la constancia y marca el rodeo como exportado (RF-18 a RF-24).
    /// Si se cancela, lanza <see cref="OperationCanceledException"/> sin guardar ni registrar nada.
    /// </summary>
    Task<Resultado<ExportacionEntregadaDto>> ExportarAsync(Guid rodeoId, FormatoExportacion formato, CancellationToken ct = default);
}
