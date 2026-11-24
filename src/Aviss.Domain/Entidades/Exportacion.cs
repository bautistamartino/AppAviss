using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// La constancia de un archivo de vuelta entregado (spec 007, RF-21): de qué rodeo, cuándo, con qué nombre y formato, su huella
/// SHA-256 y cuántas filas y vacas con evento llevaba. Solo se agrega: no cambia ni se borra (lo garantizan triggers de la base),
/// por eso es una <see cref="Entidad"/> y no una <see cref="EntidadEditable"/>. No guarda veterinario ni tablet (spec 006).
/// </summary>
public sealed class Exportacion : Entidad
{
    public Guid RodeoId { get; private set; }

    /// <summary>Fecha y hora exactas (UTC) en que se entregó el archivo.</summary>
    public DateTime FechaUtc { get; private set; }

    public string ArchivoNombre { get; private set; } = "";
    public FormatoExportacion Formato { get; private set; }

    /// <summary>SHA-256 del archivo entregado, en hexadecimal (64 caracteres).</summary>
    public string Hash { get; private set; } = "";

    /// <summary>Filas del archivo (las mismas que tenía el original).</summary>
    public int CantidadFilas { get; private set; }

    /// <summary>Vacas que llevan un evento en las columnas nuevas.</summary>
    public int CantidadVacasConEvento { get; private set; }

    private Exportacion() { }
    private Exportacion(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Exportacion Registrar(
        Guid rodeoId, DateTime fechaUtc, string? archivoNombre, FormatoExportacion formato, string? hash,
        int cantidadFilas, int cantidadVacasConEvento, DateTime ahoraUtc)
    {
        var huella = Validar.TextoObligatorio(hash, "La huella del archivo");
        if (huella.Length != 64)
            throw new DominioException("La huella del archivo debe tener 64 caracteres (SHA-256).");
        if (cantidadFilas < 0 || cantidadVacasConEvento < 0)
            throw new DominioException("Las cantidades de la exportación no pueden ser negativas.");
        if (cantidadVacasConEvento > cantidadFilas)
            throw new DominioException("Las vacas con evento no pueden ser más que las filas del archivo.");
        if (!Enum.IsDefined(formato))
            throw new DominioException("El formato de la exportación no es válido.");

        return new Exportacion(ahoraUtc)
        {
            RodeoId = Validar.IdObligatorio(rodeoId, "El rodeo"),
            FechaUtc = Validar.Utc(fechaUtc, nameof(fechaUtc)),
            ArchivoNombre = Validar.TextoObligatorio(archivoNombre, "El nombre del archivo"),
            Formato = formato,
            Hash = huella,
            CantidadFilas = cantidadFilas,
            CantidadVacasConEvento = cantidadVacasConEvento
        };
    }
}
