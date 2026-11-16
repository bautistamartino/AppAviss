using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Un estado del catálogo (PREÑADA, SECA, ...). Los tactos lo referencian por Id, así que renombrarlo no rompe nada (RF-5).
/// El <see cref="Codigo"/> interno es estable; <see cref="CodigoDairyComp"/> es el texto exacto que se usará en el Excel.
/// </summary>
public sealed class EstadoReproductivo : EntidadEditable
{
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public string CodigoDairyComp { get; private set; } = "";
    public bool Activo { get; private set; }

    /// <summary>
    /// Nombre normalizado para detectar repetidos sin distinguir mayúsculas (incluida la Ñ).
    /// Solo lectura: en la base es una columna generada con la misma regla.
    /// </summary>
    public string NombreClave => Caravana.CalcularClave(Nombre);

    private EstadoReproductivo() { }
    private EstadoReproductivo(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static EstadoReproductivo Crear(string? codigo, string? nombre, string? codigoDairyComp, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            Codigo = ValidarCodigo(codigo),
            Nombre = Validar.TextoObligatorio(nombre, "El nombre del estado"),
            CodigoDairyComp = Validar.TextoObligatorio(codigoDairyComp, "El código de Dairy Comp"),
            Activo = true
        };

    /// <summary>Cambia nombre y código de Dairy Comp. El código interno no cambia nunca.</summary>
    public void Modificar(string? nombre, string? codigoDairyComp, DateTime ahoraUtc)
    {
        Nombre = Validar.TextoObligatorio(nombre, "El nombre del estado");
        CodigoDairyComp = Validar.TextoObligatorio(codigoDairyComp, "El código de Dairy Comp");
        Tocar(ahoraUtc);
    }

    public void Desactivar(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Activar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }

    /// <summary>Solo A-Z, 0-9 y guion bajo (mayúsculas, sin acentos ni espacios). Mismo criterio que el CHECK de la base.</summary>
    private static string ValidarCodigo(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
            throw new DominioException("El código interno del estado es obligatorio.");
        foreach (var c in codigo)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
            if (!ok)
                throw new DominioException("El código interno solo admite mayúsculas sin acentos, números y guion bajo.");
        }
        return codigo;
    }
}
