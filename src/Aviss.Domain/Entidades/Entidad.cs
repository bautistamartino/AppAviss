using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Base de todas las entidades: identificador GUID (no correlativo, para que registros de distintas tablets
/// no se pisen, RF-18) y fecha de creación en UTC (RF-17).
/// Los setters son privados: una entidad solo cambia por sus métodos, que validan las reglas.
/// El constructor sin parámetros protegido es para que EF Core pueda materializarla.
/// </summary>
public abstract class Entidad
{
    public Guid Id { get; protected set; }
    public DateTime CreadoEnUtc { get; protected set; }

    protected Entidad() { }

    protected Entidad(DateTime ahoraUtc)
    {
        Id = Guid.NewGuid();
        CreadoEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
    }
}

/// <summary>Entidad que admite ediciones: además guarda cuándo se modificó por última vez (igual a la creación al principio).</summary>
public abstract class EntidadEditable : Entidad
{
    public DateTime ActualizadoEnUtc { get; protected set; }

    protected EntidadEditable() { }

    protected EntidadEditable(DateTime ahoraUtc) : base(ahoraUtc)
    {
        ActualizadoEnUtc = ahoraUtc;
    }

    protected void Tocar(DateTime ahoraUtc) => ActualizadoEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
}
