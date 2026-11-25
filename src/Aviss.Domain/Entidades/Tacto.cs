using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Un tacto. Una vez guardado es inmutable (lo garantiza también un trigger de la base): lo único que cambia es la
/// marca <see cref="Anulado"/> de "no" a "sí" cuando otro tacto lo anula. Corregir = anular con otro registro.
/// </summary>
public sealed class Tacto : EntidadEditable
{
    public Guid AnimalId { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }

    /// <summary>Número correlativo interno: desempata tactos con igual fecha y hora (el que se guardó después gana).</summary>
    public long OrdenInsercion { get; private set; }

    public Guid? EstadoAnteriorId { get; private set; }

    /// <summary>
    /// Nulo solo en un tacto de anulación que devuelve a un animal que no tenía estado (RF-13).
    /// Un tacto Normal siempre lo trae.
    /// </summary>
    public Guid? EstadoNuevoId { get; private set; }

    public int? DiasPrenez { get; private set; }
    public string? Observaciones { get; private set; }
    public TipoTacto Tipo { get; private set; }

    /// <summary>En un tacto de anulación, el tacto que anula.</summary>
    public Guid? TactoAnuladoId { get; private set; }

    public bool Anulado { get; private set; }

    /// <summary>
    /// Spec 005 (segunda vuelta): el evento del catálogo que el veterinario eligió (Preñada fecha previa, Aborto), cuando es
    /// distinto del estado en que queda la vaca. Nulo = el evento es el propio <see cref="EstadoNuevoId"/> (todo lo viejo).
    /// Una anulación nunca lo copia: el tacto anulado conserva el suyo.
    /// </summary>
    public Guid? EventoId { get; private set; }

    /// <summary>Días de preñez informados al elegir "Preñada fecha previa" (1 a 400). Distinto de <see cref="DiasPrenez"/>.</summary>
    public int? DiasPreniezInformados { get; private set; }

    /// <summary>Días de aborto informados al elegir "Aborto" (0 a 400).</summary>
    public int? DiasAborto { get; private set; }

    /// <summary>
    /// Spec 011: el segundo evento del mismo tacto (OK + PROSTAG). Nunca cambia el estado de la vaca ni su último tacto: eso lo
    /// decide solo el evento 1. Nulo en todo lo anterior a la spec y en un tacto de anulación (que no lo copia).
    /// </summary>
    public Guid? Evento2Id { get; private set; }

    /// <summary>Spec 011: la observación del evento 2. Solo existe si hay <see cref="Evento2Id"/>.</summary>
    public string? Observaciones2 { get; private set; }

    private Tacto() { }
    private Tacto(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Tacto Registrar(
        Guid animalId, DateTime fechaHoraUtc, long ordenInsercion, Guid? estadoAnteriorId, Guid estadoNuevoId,
        int? diasPrenez, string? observaciones, DateTime ahoraUtc,
        Guid? eventoId = null, int? diasPreniezInformados = null, int? diasAborto = null,
        Guid? evento2Id = null, string? observaciones2 = null)
    {
        if (diasPrenez is int d && (d < ReglasTacto.DiasPrenezMinimo || d > ReglasTacto.DiasPrenezMaximo))
            throw new DominioException("Los días de preñez deben estar entre 0 y 300.");
        if (diasPreniezInformados is int dp && !RangosDias.Preniez.Contiene(dp))
            throw new DominioException($"Los días de preñez deben estar entre {RangosDias.Preniez.Minimo} y {RangosDias.Preniez.Maximo}.");
        if (diasAborto is int da && !RangosDias.Aborto.Contiene(da))
            throw new DominioException($"Los días de aborto deben estar entre {RangosDias.Aborto.Minimo} y {RangosDias.Aborto.Maximo}.");
        if (diasPreniezInformados is not null && diasAborto is not null)
            throw new DominioException("Un tacto no puede informar días de preñez y de aborto a la vez.");
        if (eventoId is null && (diasPreniezInformados is not null || diasAborto is not null))
            throw new DominioException("Los días informados necesitan el evento que los pide.");
        // Spec 011: el evento 2 es un segundo evento "liviano": ni pasos, ni el mismo evento, ni observación suelta.
        var obs2 = Validar.TextoOpcional(observaciones2);
        if (obs2 is not null && evento2Id is null)
            throw new DominioException("La observación del evento 2 necesita el evento 2.");
        if (obs2 is not null && obs2.Length > ReglasSegundoEvento.MaximoObservaciones)
            throw new DominioException($"La observación no puede superar los {ReglasSegundoEvento.MaximoObservaciones} caracteres.");
        if (evento2Id is Guid e2)
        {
            if (e2 == estadoNuevoId || e2 == eventoId)
                throw new DominioException("El evento 2 no puede ser el mismo que el evento 1.");
            if (diasPreniezInformados is not null || diasAborto is not null)
                throw new DominioException("Un evento con pasos no admite un segundo evento.");
        }
        return new Tacto(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
            EstadoAnteriorId = estadoAnteriorId,
            EstadoNuevoId = Validar.IdObligatorio(estadoNuevoId, "El estado nuevo"),
            DiasPrenez = diasPrenez,
            Observaciones = Validar.TextoOpcional(observaciones),
            Tipo = TipoTacto.Normal,
            EventoId = eventoId,
            DiasPreniezInformados = diasPreniezInformados,
            DiasAborto = diasAborto,
            Evento2Id = evento2Id,
            Observaciones2 = obs2
        };
    }

    /// <summary>
    /// Tacto de anulación (RF-13): estado anterior = el estado actual del animal, estado nuevo = el estado anterior
    /// del tacto anulado, sin días de preñez. No se puede anular un tacto ya anulado ni otra anulación (RF-14).
    /// </summary>
    public static Tacto CrearAnulacion(
        Tacto anulado, Guid? estadoActualDelAnimalId, DateTime fechaHoraUtc, long ordenInsercion, DateTime ahoraUtc)
    {
        if (anulado.Tipo == TipoTacto.Anulacion)
            throw new DominioException("Un tacto de anulación no puede anularse.");
        if (anulado.Anulado)
            throw new DominioException("El tacto ya está anulado.");
        return new Tacto(ahoraUtc)
        {
            AnimalId = anulado.AnimalId,
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
            EstadoAnteriorId = estadoActualDelAnimalId,
            EstadoNuevoId = anulado.EstadoAnteriorId,
            DiasPrenez = null,
            Observaciones = null,
            Tipo = TipoTacto.Anulacion,
            TactoAnuladoId = anulado.Id
        };
    }

    /// <summary>Marca este tacto como anulado. Es el único cambio permitido sobre un tacto guardado.</summary>
    public void MarcarAnulado(DateTime ahoraUtc)
    {
        if (Tipo == TipoTacto.Anulacion)
            throw new DominioException("Un tacto de anulación no puede anularse.");
        if (Anulado)
            throw new DominioException("El tacto ya está anulado.");
        Anulado = true;
        Tocar(ahoraUtc);
    }
}
