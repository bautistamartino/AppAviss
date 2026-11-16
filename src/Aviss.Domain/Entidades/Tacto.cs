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
    public Guid UsuarioId { get; private set; }
    public Guid DispositivoId { get; private set; }
    public TipoTacto Tipo { get; private set; }

    /// <summary>En un tacto de anulación, el tacto que anula.</summary>
    public Guid? TactoAnuladoId { get; private set; }

    public bool Anulado { get; private set; }

    private Tacto() { }
    private Tacto(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Tacto Registrar(
        Guid animalId, DateTime fechaHoraUtc, long ordenInsercion, Guid? estadoAnteriorId, Guid estadoNuevoId,
        int? diasPrenez, string? observaciones, Guid usuarioId, Guid dispositivoId, DateTime ahoraUtc)
    {
        if (diasPrenez is int d && (d < ReglasTacto.DiasPrenezMinimo || d > ReglasTacto.DiasPrenezMaximo))
            throw new DominioException("Los días de preñez deben estar entre 0 y 300.");
        return new Tacto(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
            EstadoAnteriorId = estadoAnteriorId,
            EstadoNuevoId = Validar.IdObligatorio(estadoNuevoId, "El estado nuevo"),
            DiasPrenez = diasPrenez,
            Observaciones = Validar.TextoOpcional(observaciones),
            UsuarioId = Validar.IdObligatorio(usuarioId, "El usuario"),
            DispositivoId = Validar.IdObligatorio(dispositivoId, "El dispositivo"),
            Tipo = TipoTacto.Normal
        };
    }

    /// <summary>
    /// Tacto de anulación (RF-13): estado anterior = el estado actual del animal, estado nuevo = el estado anterior
    /// del tacto anulado, sin días de preñez. No se puede anular un tacto ya anulado ni otra anulación (RF-14).
    /// </summary>
    public static Tacto CrearAnulacion(
        Tacto anulado, Guid? estadoActualDelAnimalId, DateTime fechaHoraUtc, long ordenInsercion,
        Guid usuarioId, Guid dispositivoId, DateTime ahoraUtc)
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
            UsuarioId = Validar.IdObligatorio(usuarioId, "El usuario"),
            DispositivoId = Validar.IdObligatorio(dispositivoId, "El dispositivo"),
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
