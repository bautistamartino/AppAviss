using Aviss.Domain.Enums;

namespace Aviss.Application.Exportacion;

/// <summary>De dónde sale un registro exportable: un tacto normal o un rechazo (que incluye los eventos "solo evento").</summary>
public enum TipoRegistroExportable { Tacto, Rechazo }

/// <summary>
/// El dato crudo de un registro vigente de una vaca, como lo trae la consulta (spec 007). Ya viene filtrado: no hay tactos anulados,
/// anulaciones, rechazos anulados, marcas de pasada ni esperas. <paramref name="CaravanaClave"/> es <c>Caravana.Clave</c> (la misma
/// regla de unicidad de la base y de la importación). <paramref name="NombreEvento"/> es el nombre que el catálogo tiene AHORA.
/// <paramref name="NombreEvento2"/> es el nombre (de hoy) del segundo evento del registro (spec 011), o vacío si no tiene.
/// <paramref name="DiasPreniezInformados"/> solo existe en "Preñada fecha previa"; <paramref name="DiasAborto"/> y
/// <paramref name="ResultadoAborto"/> solo en un Aborto con sus pasos (un Aborto viejo, sin días, no los trae).
/// </summary>
public sealed record RegistroExportable(
    string CaravanaClave,
    TipoRegistroExportable Tipo,
    DateTime FechaHoraUtc,
    long OrdenInsercion,
    string NombreEvento,
    int? DiasPreniezInformados,
    int? DiasAborto,
    ResultadoAborto? ResultadoAborto,
    string NombreEvento2 = "");
