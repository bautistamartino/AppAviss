using DominioExportacion = Aviss.Domain.Entidades.Exportacion;

namespace Aviss.Application.Exportacion;

/// <summary>
/// Lo que la consulta arma de un rodeo para exportar (spec 007). <paramref name="ValoresPorCaravana"/> trae SOLO las vacas con
/// evento en la jornada (y que entran en tacto), por <c>Caravana.Clave</c>; toda vaca que no figure ahí sale con las columnas nuevas
/// vacías (RF-11), incluidas las filas excluidas al importar y la fila Total.
/// </summary>
/// <param name="Jornada">El día local del registro vigente más reciente del rodeo; nulo si no hay ningún registro.</param>
/// <param name="TotalVacas">Vacas no anuladas del rodeo (incluye las altas en campo).</param>
/// <param name="VacasConEvento">Cuántas llevan evento (es la cantidad de <paramref name="ValoresPorCaravana"/>).</param>
/// <param name="VacasCvetCeroConRegistro">Vacas con CVET 0 que tienen registro en la jornada: salen vacías y el resumen las cuenta (RF-11).</param>
public sealed record DatosExportacion(
    IReadOnlyDictionary<string, ValoresExportacion> ValoresPorCaravana,
    DateOnly? Jornada,
    int TotalVacas,
    int VacasConEvento,
    int VacasCvetCeroConRegistro)
{
    /// <summary>Vacas que no llevan evento (no pasaron, CVET 0, anulados...): el "sin evento" del resumen.</summary>
    public int VacasSinEvento => TotalVacas - VacasConEvento;
}

/// <summary>Arma los valores de exportación de un rodeo con los registros guardados en SQLite.</summary>
public interface IConsultaExportacion
{
    /// <summary>
    /// Lee todo desde UNA misma instantánea de la base (RF-25): un tacto que se guarde mientras tanto no se mezcla. Un rodeo
    /// inexistente o sin vacas devuelve datos vacíos: quien exporta decide si existe o está de baja.
    /// </summary>
    Task<DatosExportacion> ObtenerAsync(Guid rodeoId, CancellationToken ct = default);
}

/// <summary>Constancias de exportación. Solo se agregan (RF-21). Trabaja dentro de la transacción que abre <c>IUnitOfWork</c>.</summary>
public interface IExportacionRepository
{
    void Agregar(DominioExportacion exportacion);

    /// <summary>La exportación más reciente del rodeo (por fecha), o nulo si nunca se exportó.</summary>
    Task<DominioExportacion?> ObtenerUltimaAsync(Guid rodeoId, CancellationToken ct = default);
}
