using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// La marca de rechazo (spec 004, RF-6): el veterinario dejó constancia de que esta vaca se rechaza, con un motivo libre
/// en <see cref="Observaciones"/>. Es un registro de campo como <see cref="MarcaPasada"/>: entra en el orden de guardado
/// compartido y en el "último registro vigente", pero NO cambia el estado de la vaca ni su fecha de último tacto.
/// Es inmutable salvo pasar <see cref="Anulado"/> de "no" a "sí" (deshacer), con quién y cuándo.
/// </summary>
public sealed class Rechazo : EntidadEditable
{
    /// <summary>Mismo límite que la observación de un tacto.</summary>
    public const int MaximoObservaciones = 300;

    public Guid AnimalId { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }

    /// <summary>Orden de guardado compartido con tactos y marcas (desempata registros con igual fecha y hora).</summary>
    public long OrdenInsercion { get; private set; }

    public string? Observaciones { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid DispositivoId { get; private set; }
    public bool Anulado { get; private set; }
    public DateTime? AnuladoEnUtc { get; private set; }
    public Guid? AnuladoPorUsuarioId { get; private set; }

    private Rechazo() { }
    private Rechazo(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Rechazo Registrar(
        Guid animalId, DateTime fechaHoraUtc, long ordenInsercion, string? observaciones,
        Guid usuarioId, Guid dispositivoId, DateTime ahoraUtc)
    {
        var obs = Validar.TextoOpcional(observaciones);
        if (obs is not null && obs.Length > MaximoObservaciones)
            throw new DominioException($"La observación no puede superar los {MaximoObservaciones} caracteres.");
        return new(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
            Observaciones = obs,
            UsuarioId = Validar.IdObligatorio(usuarioId, "El usuario"),
            DispositivoId = Validar.IdObligatorio(dispositivoId, "El dispositivo")
        };
    }

    /// <summary>Deshace el rechazo. Es el único cambio permitido sobre un rechazo guardado.</summary>
    public void MarcarAnulado(Guid usuarioId, DateTime ahoraUtc)
    {
        if (Anulado)
            throw new DominioException("El rechazo ya está anulado.");
        AnuladoPorUsuarioId = Validar.IdObligatorio(usuarioId, "El usuario");
        AnuladoEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
        Anulado = true;
        Tocar(ahoraUtc);
    }
}
