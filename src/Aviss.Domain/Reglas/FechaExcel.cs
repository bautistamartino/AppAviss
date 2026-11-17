using System.Globalization;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Fechas tal como vienen en el Excel de Dairy Comp (spec 003, RF-14): texto <c>día/mes/año</c> con "/",
/// con espacios de más (" 5/11/26") y año de 2 o 4 cifras. El año de 2 cifras es 20aa (26 es 2026).
/// Lo que no se puede interpretar (otro formato, día o mes imposible) no falla: devuelve false y quien llama
/// lo muestra como texto aparte, sin fecha ordenable.
/// </summary>
public static class FechaExcel
{
    public static bool TryParsear(string? texto, out DateOnly fecha)
    {
        fecha = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var partes = texto.Split('/');
        if (partes.Length != 3) return false;

        if (!TryNumero(partes[0], 1, 2, out var dia)) return false;
        if (!TryNumero(partes[1], 1, 2, out var mes)) return false;
        // Año: 2 cifras (20aa) o 4 cifras; 1 o 3 cifras son ambiguas y se rechazan.
        var anioTexto = partes[2].Trim();
        if (anioTexto.Length is not (2 or 4)) return false;
        if (!TryNumero(anioTexto, 2, 4, out var anio)) return false;
        if (anioTexto.Length == 2) anio += 2000;

        // Día o mes imposible ("31/2/26", mes 13) = no es una fecha.
        if (anio < 1 || anio > 9999 || mes < 1 || mes > 12 || dia < 1 || dia > DateTime.DaysInMonth(anio, mes))
            return false;

        fecha = new DateOnly(anio, mes, dia);
        return true;
    }

    /// <summary>Solo dígitos ASCII (sin signos ni separadores) y con el largo pedido, ya sin espacios de los extremos.</summary>
    private static bool TryNumero(string texto, int minDigitos, int maxDigitos, out int valor)
    {
        valor = 0;
        var t = texto.Trim();
        if (t.Length < minDigitos || t.Length > maxDigitos) return false;
        foreach (var c in t)
            if (c is < '0' or > '9') return false;
        valor = int.Parse(t, CultureInfo.InvariantCulture);
        return true;
    }
}
