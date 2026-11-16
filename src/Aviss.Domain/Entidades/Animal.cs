using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using IdElec = Aviss.Domain.Reglas.IdElectronico; // el alias evita confundir la clase con la propiedad del mismo nombre

namespace Aviss.Domain.Entidades;

/// <summary>
/// Una vaca. Nunca se borra. Las columnas originales del Excel (<see cref="DatosOriginalesJson"/>) y el origen
/// no cambian jamás; el resto solo cambia por los métodos de abajo.
/// </summary>
public sealed class Animal : EntidadEditable
{
    public Guid RodeoId { get; private set; }
    public string NumeroCaravana { get; private set; } = "";
    public string? IdElectronico { get; private set; }
    public Guid? EstadoReproductivoId { get; private set; }

    /// <summary>Vacía hasta el primer tacto hecho en Aviss (no se completa con la fecha de importación).</summary>
    public DateTime? FechaUltimoTactoUtc { get; private set; }

    public string? Observaciones { get; private set; }
    public OrigenAnimal Origen { get; private set; }
    public bool Anulado { get; private set; }
    public DateTime? AnuladoEnUtc { get; private set; }

    /// <summary>Las columnas del Excel tal cual vinieron (JSON). Nulo en las altas en campo.</summary>
    public string? DatosOriginalesJson { get; private set; }

    private Animal() { }
    private Animal(DateTime ahoraUtc) : base(ahoraUtc) { }

    /// <summary>Animal que viene de un Excel importado (lo usará la spec de importación).</summary>
    public static Animal CrearImportado(
        Guid rodeoId, Caravana caravana, string? idElectronico, Guid? estadoReproductivoId,
        string? observaciones, string? datosOriginalesJson, DateTime ahoraUtc)
    {
        if (string.IsNullOrWhiteSpace(datosOriginalesJson))
            throw new DominioException("Un animal importado debe guardar sus columnas originales.");
        return new Animal(ahoraUtc)
        {
            RodeoId = Validar.IdObligatorio(rodeoId, "El rodeo"),
            NumeroCaravana = caravana.Valor,
            IdElectronico = IdElec.NormalizarOpcional(idElectronico),
            EstadoReproductivoId = estadoReproductivoId,
            Observaciones = Validar.TextoOpcional(observaciones),
            Origen = OrigenAnimal.Importado,
            DatosOriginalesJson = datosOriginalesJson
        };
    }

    /// <summary>Vaca que no figura en el rodeo (RF-25): solo rodeo y caravana; estado y fecha de último tacto vacíos.</summary>
    public static Animal CrearAltaEnCampo(
        Guid rodeoId, Caravana caravana, string? idElectronico, string? observaciones, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            RodeoId = Validar.IdObligatorio(rodeoId, "El rodeo"),
            NumeroCaravana = caravana.Valor,
            IdElectronico = IdElec.NormalizarOpcional(idElectronico),
            Observaciones = Validar.TextoOpcional(observaciones),
            Origen = OrigenAnimal.AltaEnCampo
        };

    /// <summary>Efecto de un tacto sobre el animal (RF-8): nuevo estado y fecha del último tacto.</summary>
    public void AplicarTacto(Guid estadoNuevoId, DateTime fechaHoraTactoUtc, DateTime ahoraUtc)
    {
        ExigirNoAnulado();
        EstadoReproductivoId = Validar.IdObligatorio(estadoNuevoId, "El estado nuevo");
        FechaUltimoTactoUtc = Validar.Utc(fechaHoraTactoUtc, nameof(fechaHoraTactoUtc));
        Tocar(ahoraUtc);
    }

    /// <summary>Efecto de anular un tacto (RF-13): vuelve al estado y fecha anteriores (que pueden ser vacíos).</summary>
    public void RevertirTacto(Guid? estadoId, DateTime? fechaUltimoTactoUtc, DateTime ahoraUtc)
    {
        ExigirNoAnulado();
        EstadoReproductivoId = estadoId;
        FechaUltimoTactoUtc = fechaUltimoTactoUtc is DateTime f ? Validar.Utc(f, nameof(fechaUltimoTactoUtc)) : null;
        Tocar(ahoraUtc);
    }

    /// <summary>Anula un alta en campo (RF-25). El que no tenga tactos vigentes lo controla el caso de uso. No se toca la caravana: la libera el índice parcial de la base.</summary>
    public void Anular(DateTime ahoraUtc)
    {
        if (Origen != OrigenAnimal.AltaEnCampo)
            throw new DominioException("Un animal importado no puede anularse.");
        ExigirNoAnulado();
        Anulado = true;
        AnuladoEnUtc = Validar.Utc(ahoraUtc, nameof(ahoraUtc));
        Tocar(ahoraUtc);
    }

    public void CambiarObservaciones(string? observaciones, DateTime ahoraUtc)
    {
        ExigirNoAnulado();
        Observaciones = Validar.TextoOpcional(observaciones);
        Tocar(ahoraUtc);
    }

    public void CambiarIdElectronico(string? idElectronico, DateTime ahoraUtc)
    {
        ExigirNoAnulado();
        IdElectronico = IdElec.NormalizarOpcional(idElectronico);
        Tocar(ahoraUtc);
    }

    /// <summary>Solo las altas en campo pueden cambiar de caravana (RF-26); la de un importado es la del Excel.</summary>
    public void CambiarCaravana(Caravana caravana, DateTime ahoraUtc)
    {
        ExigirNoAnulado();
        if (Origen != OrigenAnimal.AltaEnCampo)
            throw new DominioException("La caravana de un animal importado no se puede cambiar.");
        NumeroCaravana = caravana.Valor;
        Tocar(ahoraUtc);
    }

    private void ExigirNoAnulado()
    {
        if (Anulado) throw new DominioException("El animal está anulado.");
    }
}
