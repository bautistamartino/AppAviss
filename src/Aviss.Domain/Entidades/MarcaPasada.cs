using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// La marca de pasada: "esta vaca ya pasó por esta manga" (spec 003, RF-12, RF-20). Se guarda sola al leer la
/// caravana, sin ningún toque. Como el tacto, es inmutable salvo pasar <see cref="Anulado"/> de "no" a "sí"
/// (desmarcar). Una vaca tiene a lo sumo una marca vigente por manga (índice único parcial en la base).
/// </summary>
public sealed class MarcaPasada : EntidadEditable
{
    public Guid AnimalId { get; private set; }
    public Guid MangaId { get; private set; }
    public DateTime FechaHoraUtc { get; private set; }

    /// <summary>
    /// Orden de guardado compartido con los tactos (y los registros de campo futuros): desempata registros de la
    /// misma vaca con igual fecha y hora. Lo calcula quien guarda, dentro de la transacción (RF-21).
    /// </summary>
    public long OrdenInsercion { get; private set; }

    public bool Anulado { get; private set; }

    private MarcaPasada() { }
    private MarcaPasada(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static MarcaPasada Registrar(
        Guid animalId, Guid mangaId, DateTime fechaHoraUtc, long ordenInsercion, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            AnimalId = Validar.IdObligatorio(animalId, "El animal"),
            MangaId = Validar.IdObligatorio(mangaId, "La manga"),
            FechaHoraUtc = Validar.Utc(fechaHoraUtc, nameof(fechaHoraUtc)),
            OrdenInsercion = ordenInsercion,
        };

    /// <summary>Desmarca a la vaca. Es el único cambio permitido sobre una marca guardada.</summary>
    public void MarcarAnulada(DateTime ahoraUtc)
    {
        if (Anulado)
            throw new DominioException("La marca de pasada ya está anulada.");
        Anulado = true;
        Tocar(ahoraUtc);
    }
}
