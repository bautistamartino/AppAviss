using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Una importación de Excel de un establecimiento. Guarda el archivo original completo y su huella SHA-256 (RF-2).
/// La cantidad de animales no se guarda: se cuenta.
/// </summary>
public sealed class Rodeo : EntidadEditable
{
    public Guid EstablecimientoId { get; private set; }
    public string Nombre { get; private set; } = "";
    public DateTime FechaImportacionUtc { get; private set; }
    public string ArchivoOrigenNombre { get; private set; } = "";
    public string ArchivoOrigenHash { get; private set; } = "";
    public byte[] ArchivoOrigenBlob { get; private set; } = Array.Empty<byte>();
    public string PerfilExcel { get; private set; } = "";
    public EstadoRodeo Estado { get; private set; }
    public bool Activo { get; private set; }

    private Rodeo() { }
    private Rodeo(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Rodeo Crear(
        Guid establecimientoId, string? nombre, DateTime fechaImportacionUtc, string? archivoOrigenNombre,
        string? archivoOrigenHash, byte[]? archivoOrigenBlob, string? perfilExcel, DateTime ahoraUtc)
    {
        var hash = Validar.TextoObligatorio(archivoOrigenHash, "La huella del archivo");
        if (hash.Length != 64)
            throw new DominioException("La huella del archivo debe tener 64 caracteres (SHA-256).");
        if (archivoOrigenBlob is null || archivoOrigenBlob.Length == 0)
            throw new DominioException("El archivo original es obligatorio.");

        return new Rodeo(ahoraUtc)
        {
            EstablecimientoId = Validar.IdObligatorio(establecimientoId, "El establecimiento"),
            Nombre = Validar.TextoObligatorio(nombre, "El nombre del rodeo"),
            FechaImportacionUtc = Validar.Utc(fechaImportacionUtc, nameof(fechaImportacionUtc)),
            ArchivoOrigenNombre = Validar.TextoObligatorio(archivoOrigenNombre, "El nombre del archivo de origen"),
            ArchivoOrigenHash = hash,
            ArchivoOrigenBlob = archivoOrigenBlob,
            PerfilExcel = Validar.TextoObligatorio(perfilExcel, "El perfil de Excel"),
            Estado = EstadoRodeo.Abierto,
            Activo = true
        };
    }

    public void DarDeBaja(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Reactivar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }

    /// <summary>Lo usa solo la función de exportación. Es informativo: no impide seguir tactando.</summary>
    public void MarcarExportado(DateTime ahoraUtc) { Estado = EstadoRodeo.Exportado; Tocar(ahoraUtc); }
}
