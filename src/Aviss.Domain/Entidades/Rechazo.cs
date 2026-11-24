using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// La marca de rechazo (spec 004, RF-6): el veterinario dejó constancia de que esta vaca se rechaza, con un motivo libre
/// en <see cref="Observaciones"/>. Es un registro de campo como <see cref="MarcaPasada"/>: entra en el orden de guardado
/// compartido y en el "último registro vigente", pero NO cambia el estado de la vaca ni su fecha de último tacto.
/// Es inmutable salvo pasar <see cref="Anulado"/> de "no" a "sí" (deshacer), con la fecha de anulación.
/// </summary>
public sealed class Rechazo : EntidadEditable
{
    /// <summary>Mismo límite que la observación de un tacto.</summary>
    public const int MaximoObservaciones = 300;

    public Guid AnimalId { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }

    /// <summary>Orden de guardado compartido con tactos y marcas (desempata registros con igual fecha y hora).</summary>
    public long OrdenInsercion { get; private set; }

    /// <summary>
    /// Spec 005: el evento del catálogo (de tipo SoloEvento: OK, Aborto, Revisar Nuevamente) que guarda este registro.
    /// Nulo = el Rechazo fijo de la aplicación. Nunca cambia una vez guardado.
    /// </summary>
    public Guid? EventoId { get; private set; }

    /// <summary>
    /// Spec 005 (segunda vuelta): los días de aborto (0 a 400) cuando este registro es un Aborto cuyo resultado fue Rechazo
    /// (RF-30). Solo con evento; en un solo evento común o en el Rechazo fijo es nulo.
    /// </summary>
    public int? DiasAborto { get; private set; }

    public string? Observaciones { get; private set; }
    public bool Anulado { get; private set; }
    public DateTime? AnuladoEnUtc { get; private set; }

    private Rechazo() { }
    private Rechazo(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Rechazo Registrar(
        Guid animalId, DateTime fechaHoraUtc, long ordenInsercion, string? observaciones,
        DateTime ahoraUtc, Guid? eventoId = null, int? diasAborto = null)
    {
        var obs = Validar.TextoOpcional(observaciones);
        if (obs is not null && obs.Length > MaximoObservaciones)
            throw new DominioException($"La observación no puede superar los {MaximoObservaciones} caracteres.");
        if (diasAborto is int da && !RangosDias.Aborto.Contiene(da))
            throw new DominioException($"Los días de aborto deben estar entre {RangosDias.Aborto.Minimo} y {RangosDias.Aborto.Maximo}.");
        if (diasAborto is not null && eventoId is null)
            throw new DominioException("Los días de aborto necesitan el evento Aborto.");
        return new(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
            EventoId = eventoId,
            DiasAborto = diasAborto,
            Observaciones = obs,
        };
    }

    /// <summary>Deshace el rechazo. Es el único cambio permitido sobre un rechazo guardado.</summary>
    public void MarcarAnulado(DateTime ahoraUtc)
    {
        if (Anulado)
            throw new DominioException("El rechazo ya está anulado.");
        AnuladoEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
        Anulado = true;
        Tocar(ahoraUtc);
    }
}
