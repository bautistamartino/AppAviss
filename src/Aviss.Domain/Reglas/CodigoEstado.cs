using System.Globalization;
using System.Text;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Arma el código interno de un estado reproductivo a partir de su nombre, para que el veterinario no tenga que escribirlo
/// cuando crea un evento en campo. Sigue la misma regla que valida <c>EstadoReproductivo</c>: solo A-Z, 0-9 y guion bajo.
/// </summary>
public static class CodigoEstado
{
    /// <summary>
    /// "Aborto" → "ABORTO"; "Sin servicio" → "SIN_SERVICIO"; "Preñada 2" → "PRENADA_2". Mayúsculas, sin acentos y con un
    /// guion bajo en lugar de cada grupo de espacios o símbolos. Devuelve vacío si el nombre no tiene ningún carácter útil.
    /// </summary>
    public static string DesdeNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "";

        var sinAcentos = new StringBuilder();
        foreach (var c in nombre.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sinAcentos.Append(c);
        }

        var resultado = new StringBuilder();
        var pendienteGuion = false;
        foreach (var c in sinAcentos.ToString().ToUpperInvariant())
        {
            var esUtil = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9');
            if (esUtil)
            {
                if (pendienteGuion && resultado.Length > 0) resultado.Append('_');
                pendienteGuion = false;
                resultado.Append(c);
            }
            else
            {
                pendienteGuion = true;
            }
        }
        return resultado.ToString();
    }
}
