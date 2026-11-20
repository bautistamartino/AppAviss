using System.Globalization;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Regla del CVET de Dairy Comp (la columna que dice por qué una vaca está en la lista de tacto): un CVET en 0 significa que
/// la vaca NO entra en tacto; cualquier otro valor (por ejemplo "PRE", "FRES", "ABT") significa que sí entra, y el valor es el
/// motivo. Si el dato falta o está vacío no se bloquea el trabajo: la vaca entra, porque no hay nada que diga lo contrario.
/// </summary>
public static class ReglasCvet
{
    public static bool EntraEnTacto(string? cvet)
    {
        if (string.IsNullOrWhiteSpace(cvet)) return true;

        // "0", "00", "0.0" y "0,0" son cero; "PRE", "7" u "0X" no.
        var texto = cvet.Trim().Replace(',', '.');
        var esNumero = decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor);
        return !(esNumero && valor == 0m);
    }
}
