using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Reglas;

/// <summary>
/// El estado anterior que informa quien registra el tacto. "No informado" es <c>null</c> (se toma el actual del animal);
/// si se informa, trae el id del estado o <c>null</c> dentro del registro para decir "el animal no tenía estado".
/// </summary>
public sealed record EstadoEsperado(Guid? EstadoId);

/// <summary>Todo lo que hace falta saber para validar un tacto; lo arma el caso de uso con datos ya leídos.</summary>
public sealed record DatosValidacionTacto(
    bool AnimalAnulado,
    bool RodeoActivo,
    bool EstablecimientoActivo,
    bool EstadoNuevoActivo,
    Guid? EstadoActualDelAnimal,
    EstadoEsperado? EstadoAnteriorInformado,
    DateTime FechaHoraUtc,
    DateTime AhoraUtc,
    DateTime? FechaUltimoVigenteUtc,
    int? DiasPrenez);

/// <summary>Reglas puras (sin base de datos) de los tactos: RF-8b, y "último tacto vigente" (Definiciones de la spec).</summary>
public static class ReglasTacto
{
    public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromMinutes(5);
    public const int DiasPrenezMinimo = 0;
    public const int DiasPrenezMaximo = 300;

    /// <summary>Devuelve el primer motivo de rechazo (en el orden de RF-8b) o null si el tacto es válido.</summary>
    public static MotivoRechazoTacto? Validar(DatosValidacionTacto d)
    {
        if (d.AnimalAnulado) return MotivoRechazoTacto.AnimalAnulado;
        if (!d.RodeoActivo) return MotivoRechazoTacto.RodeoDeBaja;
        if (!d.EstablecimientoActivo) return MotivoRechazoTacto.EstablecimientoDeBaja;
        if (!d.EstadoNuevoActivo) return MotivoRechazoTacto.EstadoNuevoDeBaja;

        if (d.EstadoAnteriorInformado is not null && d.EstadoAnteriorInformado.EstadoId != d.EstadoActualDelAnimal)
            return MotivoRechazoTacto.EstadoAnteriorNoCoincide;

        // Hasta 5 minutos adelante se tolera (diferencias de reloj); más que eso es una hora mal puesta.
        if (d.FechaHoraUtc > d.AhoraUtc + ToleranciaFuturo)
            return MotivoRechazoTacto.FechaFutura;

        // Igual al último se acepta (dos tactos seguidos del mismo animal son válidos); anterior, no.
        if (d.FechaUltimoVigenteUtc is DateTime ultimo && d.FechaHoraUtc < ultimo)
            return MotivoRechazoTacto.FechaAnteriorAlUltimoTacto;

        if (d.DiasPrenez is int dias && (dias < DiasPrenezMinimo || dias > DiasPrenezMaximo))
            return MotivoRechazoTacto.DiasPrenezFueraDeRango;

        return null;
    }

    /// <summary>
    /// Último tacto vigente: normal, no anulado, el de fecha más reciente; con igual fecha gana el que se guardó
    /// después (mayor <c>OrdenInsercion</c>). Los tactos de anulación nunca cuentan.
    /// </summary>
    public static Tacto? UltimoVigente(IEnumerable<Tacto> tactos)
        => tactos
            .Where(t => t.Tipo == TipoTacto.Normal && !t.Anulado)
            .OrderByDescending(t => t.FechaHoraUtc)
            .ThenByDescending(t => t.OrdenInsercion)
            .FirstOrDefault();

    /// <summary>El último tacto vigente entre todos los demás, sin contar el indicado (sirve al anular).</summary>
    public static Tacto? VigenteAnteriorA(IEnumerable<Tacto> tactos, Tacto excluido)
        => UltimoVigente(tactos.Where(t => t.Id != excluido.Id));
}
