using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// El cierre de una manga (spec 003, RF-6 a RF-10). Es un registro aparte de <see cref="Manga"/> para respetar
/// "solo agregar": la manga nunca cambia y un cierre se deshace marcándolo anulado (de "no" a "sí", igual que un
/// <see cref="Tacto"/>), con lo que la manga vuelve a estar abierta. Una manga está cerrada si tiene un cierre con
/// <see cref="Anulado"/> en falso; la base garantiza que no haya dos vigentes.
/// </summary>
public sealed class CierreManga : EntidadEditable
{
    public Guid MangaId { get; private set; }
    public TipoCierreManga Tipo { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }

    /// <summary>Cuántas vacas distintas contaba Aviss en la manga al cerrarla.</summary>
    public int CantidadAviss { get; private set; }

    /// <summary>Lo que el veterinario dijo que había (en "Coincide", igual a la de Aviss). Nulo en el cierre automático.</summary>
    public int? CantidadConfirmada { get; private set; }

    /// <summary>Confirmada menos Aviss. Nula si no hay cantidad confirmada.</summary>
    public int? Diferencia { get; private set; }

    public bool Anulado { get; private set; }

    private CierreManga() { }
    private CierreManga(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static CierreManga Cerrar(
        Guid mangaId, TipoCierreManga tipo, DateTime fechaHoraUtc, int cantidadAviss, int? cantidadConfirmada, DateTime ahoraUtc)
    {
        if (!Enum.IsDefined(tipo))
            throw new DominioException("El tipo de cierre no es válido.");
        if (cantidadAviss < 0)
            throw new DominioException("La cantidad de vacas no puede ser negativa.");
        if (cantidadConfirmada is < 0)
            throw new DominioException("La cantidad confirmada no puede ser negativa.");
        // El cierre automático no le pregunta nada a nadie: por eso no tiene cantidad confirmada ni diferencia (RF-10).
        if (tipo == TipoCierreManga.Automatico && cantidadConfirmada is not null)
            throw new DominioException("Un cierre automático no tiene cantidad confirmada.");

        return new CierreManga(ahoraUtc)
        {
            MangaId = Validar.IdObligatorio(mangaId, "La manga"),
            Tipo = tipo,
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            CantidadAviss = cantidadAviss,
            CantidadConfirmada = cantidadConfirmada,
            Diferencia = cantidadConfirmada is int c ? c - cantidadAviss : null,
        };
    }

    /// <summary>Anula el cierre (reabre la manga). Es el único cambio permitido sobre un cierre guardado.</summary>
    public void MarcarAnulado(DateTime ahoraUtc)
    {
        if (Anulado)
            throw new DominioException("El cierre ya está anulado.");
        Anulado = true;
        Tocar(ahoraUtc);
    }
}
