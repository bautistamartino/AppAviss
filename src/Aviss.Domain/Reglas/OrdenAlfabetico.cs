using System.Globalization;
using System.Text;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Orden alfabético de los eventos de "Otro evento" (spec 005, RF-7): sin distinguir mayúsculas ni tildes y con la Ñ
/// entre la N y la O. Se compara la clave en orden ordinal. Es una clave DISTINTA de <c>Caravana.CalcularClave</c>
/// (esa conserva las tildes porque sirve para detectar repetidos).
/// </summary>
public static class OrdenAlfabetico
{
    /// <summary>
    /// "Ñandú" da "N{ANDU". La Ñ se vuelve N + '{' (el carácter que sigue a la 'z' en ASCII, mayor que cualquier letra):
    /// así queda después de "NZ..." y antes de la O. Después se quitan las tildes y diéresis y se pasa a mayúsculas.
    /// </summary>
    public static string Clave(string? nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return "";

        var sb = new StringBuilder(nombre.Length);
        // FormC junta "n" + tilde combinante en una sola "ñ", así la Ñ se reconoce venga como venga.
        foreach (var c in nombre.Normalize(NormalizationForm.FormC))
        {
            if (c is 'ñ' or 'Ñ') sb.Append("N{");
            else sb.Append(c);
        }

        var sinTildes = new StringBuilder(sb.Length);
        foreach (var c in sb.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sinTildes.Append(c);
        }
        return sinTildes.ToString().ToUpperInvariant();
    }

    /// <summary>Comparador ordinal sobre la clave, para <c>OrderBy(x => x.Nombre, OrdenAlfabetico.Comparador)</c>.</summary>
    public static readonly IComparer<string?> Comparador = Comparer<string?>.Create(
        (a, b) => string.CompareOrdinal(Clave(a), Clave(b)));
}
