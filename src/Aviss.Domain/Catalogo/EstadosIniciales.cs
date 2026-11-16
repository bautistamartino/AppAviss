namespace Aviss.Domain.Catalogo;

/// <summary>Un estado que se siembra al crear la base (RF-6).</summary>
public sealed record EstadoInicial(string Codigo, string Nombre, string CodigoDairyComp);

/// <summary>
/// Los seis estados vistos en Dairy Comp. SUPUESTO sin validar (prueba 0.2): el código de Dairy Comp
/// es igual al nombre que trae el Excel. Si resulta distinto, se corrige en el catálogo sin tocar el programa.
/// El código interno es estable (mayúsculas, sin acentos): por eso PRENADA y no PREÑADA.
/// </summary>
public static class EstadosIniciales
{
    public static readonly IReadOnlyList<EstadoInicial> Todos = new[]
    {
        new EstadoInicial("PRENADA", "PREÑADA", "PREÑADA"),
        new EstadoInicial("INSEMIN", "INSEMIN", "INSEMIN"),
        new EstadoInicial("SECA", "SECA", "SECA"),
        new EstadoInicial("VACIA", "VACIA", "VACIA"),
        new EstadoInicial("FRESCA", "FRESCA", "FRESCA"),
        new EstadoInicial("NOINSEM", "NOINSEM", "NOINSEM"),
    };
}
