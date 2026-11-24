namespace Aviss.Application.Exportacion;

/// <summary>
/// De los registros vigentes de UNA vaca dentro de la jornada, elige el que se exporta (spec 007, RF-12): el de fecha y hora más
/// recientes y, si empatan, el que se guardó después (orden de guardado). Los anulados ya no llegan: los descarta la consulta, así que
/// un registro corregido con "Editar" (anulado + nuevo) exporta el nuevo y uno anulado no exporta nada.
/// </summary>
public static class SelectorRegistroExportable
{
    public static RegistroExportable? Elegir(IEnumerable<RegistroExportable> registrosDeLaVaca)
    {
        RegistroExportable? ganador = null;
        foreach (var r in registrosDeLaVaca)
            if (ganador is null || (r.FechaHoraUtc, r.OrdenInsercion).CompareTo((ganador.FechaHoraUtc, ganador.OrdenInsercion)) > 0)
                ganador = r;
        return ganador;
    }
}
