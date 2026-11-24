using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Registro de auditoría: qué cambió, de qué valor a cuál y cuándo (RF-10). Solo se agrega:
/// un trigger de la base rechaza modificarlo o borrarlo (RF-11). Los estados se guardan por Id, no por nombre.
/// </summary>
public sealed class Historial : Entidad
{
    public string Entidad { get; private set; } = "";
    public Guid EntidadId { get; private set; }
    public string Campo { get; private set; } = "";
    public string? ValorAnterior { get; private set; }
    public string? ValorNuevo { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }
    public AccionHistorial Accion { get; private set; }

    private Historial() { }
    private Historial(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Historial Crear(
        string entidad, Guid entidadId, string campo, string? valorAnterior, string? valorNuevo,
        DateTime fechaHoraUtc, AccionHistorial accion)
        // CreadoEnUtc = FechaHoraUtc: se pasa la misma fecha a la base (uniformidad con la constitución).
        => new(fechaHoraUtc)
        {
            Entidad = Validar.TextoObligatorio(entidad, "La entidad"),
            EntidadId = Validar.IdObligatorio(entidadId, "El id de la entidad"),
            Campo = Validar.TextoObligatorio(campo, "El campo"),
            ValorAnterior = valorAnterior,
            ValorNuevo = valorNuevo,
            FechaHoraUtc = fechaHoraUtc,
            Accion = accion
        };
}
