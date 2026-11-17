using Aviss.Domain.Enums;

namespace Aviss.Domain.Reglas;

/// <summary>Todo lo que hace falta saber para validar una marca de pasada; lo arma el servicio con datos ya leídos.</summary>
/// <param name="FechaUltimoRegistroVigenteUtc">
/// Fecha del último registro de campo vigente de la vaca (tacto normal o marca), o null si no tiene.
/// </param>
public sealed record DatosValidacionMarca(
    bool AnimalAnulado,
    bool RodeoActivo,
    bool EstablecimientoActivo,
    DateTime FechaHoraUtc,
    DateTime AhoraUtc,
    DateTime? FechaUltimoRegistroVigenteUtc);

/// <summary>
/// Reglas puras de la marca de pasada (spec 003, RF-21): las mismas que las del tacto (<see cref="ReglasTacto"/>)
/// en lo que aplica a una marca (no tiene estado ni días de preñez).
/// </summary>
public static class ReglasMarcaPasada
{
    /// <summary>Devuelve el primer motivo de rechazo o null si la marca es válida.</summary>
    public static MotivoRechazoMarca? Validar(DatosValidacionMarca d)
    {
        if (d.AnimalAnulado) return MotivoRechazoMarca.AnimalAnulado;
        if (!d.RodeoActivo) return MotivoRechazoMarca.RodeoDeBaja;
        if (!d.EstablecimientoActivo) return MotivoRechazoMarca.EstablecimientoDeBaja;

        // Misma tolerancia que el tacto: hasta 5 minutos adelante se perdona (diferencias de reloj).
        if (d.FechaHoraUtc > d.AhoraUtc + ReglasTacto.ToleranciaFuturo)
            return MotivoRechazoMarca.FechaFutura;

        // Anterior al último registro de la vaca indica un reloj atrasado; igual se acepta.
        if (d.FechaUltimoRegistroVigenteUtc is DateTime ultimo && d.FechaHoraUtc < ultimo)
            return MotivoRechazoMarca.FechaAnteriorAlUltimoRegistro;

        return null;
    }
}
