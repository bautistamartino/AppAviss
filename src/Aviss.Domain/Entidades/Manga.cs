using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Una manga: un tramo de trabajo de un rodeo (las vacas que pasan juntas por la manga del tambo). Spec 003, RF-4.
/// Nunca cambia después de creada (lo garantiza también un trigger de la base). Se crea en la misma transacción que
/// la primera marca de pasada: no existen mangas vacías. Su <see cref="Entidad.CreadoEnUtc"/> es la apertura.
/// "Abierta" o "cerrada" no se guarda acá: se deduce de si tiene un <see cref="CierreManga"/> vigente.
/// </summary>
public sealed class Manga : Entidad
{
    public Guid RodeoId { get; private set; }

    /// <summary>Número correlativo dentro del rodeo, desde 1. Único por rodeo (índice único en la base).</summary>
    public int Numero { get; private set; }

    public Guid AbiertaPorUsuarioId { get; private set; }
    public Guid DispositivoId { get; private set; }

    private Manga() { }
    private Manga(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Manga Abrir(Guid rodeoId, int numero, Guid usuarioId, Guid dispositivoId, DateTime ahoraUtc)
    {
        if (numero < 1)
            throw new DominioException("El número de manga debe ser 1 o más.");
        return new Manga(ahoraUtc)
        {
            RodeoId = Validar.IdObligatorio(rodeoId, "El rodeo"),
            Numero = numero,
            AbiertaPorUsuarioId = Validar.IdObligatorio(usuarioId, "El usuario"),
            DispositivoId = Validar.IdObligatorio(dispositivoId, "El dispositivo")
        };
    }
}
