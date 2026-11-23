using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Una vaca puesta "en espera" (spec 004, RF-1, RF-13): el veterinario la dejó para decidir después. NO es un registro
/// de campo (no entra en el orden de guardado ni en el "último registro vigente"): solo recuerda que falta un veredicto.
/// Una vez guardada es inmutable (también lo garantiza un trigger), salvo pasar UNA sola vez de
/// <see cref="EstadoEspera.Pendiente"/> a <see cref="EstadoEspera.Resuelta"/> o <see cref="EstadoEspera.Salteada"/>.
/// Una vaca tiene a lo sumo una espera pendiente (índice único parcial en la base).
/// </summary>
public sealed class Espera : EntidadEditable
{
    public Guid AnimalId { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid DispositivoId { get; private set; }
    public EstadoEspera Estado { get; private set; }

    /// <summary>Cuándo y quién la resolvió o salteó; nulos mientras está pendiente.</summary>
    public DateTime? ResueltaEnUtc { get; private set; }
    public Guid? ResueltaPorUsuarioId { get; private set; }

    /// <summary>Id del tacto o del rechazo que la resolvió (sin clave foránea: apunta a dos tablas). Nulo si se salteó.</summary>
    public Guid? RegistroResolucionId { get; private set; }

    private Espera() { }
    private Espera(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Espera Crear(Guid animalId, DateTime fechaHoraUtc, Guid usuarioId, Guid dispositivoId, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            UsuarioId = Validar.IdObligatorio(usuarioId, "El usuario"),
            DispositivoId = Validar.IdObligatorio(dispositivoId, "El dispositivo"),
            Estado = EstadoEspera.Pendiente
        };

    /// <summary>La espera se resolvió porque se guardó un tacto o un rechazo (<paramref name="registroId"/>).</summary>
    public void Resolver(Guid registroId, Guid usuarioId, DateTime ahoraUtc)
    {
        ExigirPendiente();
        RegistroResolucionId = Validar.IdObligatorio(registroId, "El registro");
        Cerrar(EstadoEspera.Resuelta, usuarioId, ahoraUtc);
    }

    /// <summary>La espera se saltó: no hacía falta cargar ningún evento. No guarda registro.</summary>
    public void Saltear(Guid usuarioId, DateTime ahoraUtc)
    {
        ExigirPendiente();
        Cerrar(EstadoEspera.Salteada, usuarioId, ahoraUtc);
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoEspera.Pendiente)
            throw new DominioException("La espera ya no está pendiente.");
    }

    private void Cerrar(EstadoEspera nuevo, Guid usuarioId, DateTime ahoraUtc)
    {
        ResueltaPorUsuarioId = Validar.IdObligatorio(usuarioId, "El usuario");
        ResueltaEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
        Estado = nuevo;
        Tocar(ahoraUtc);
    }
}
