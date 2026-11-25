using System.Text.RegularExpressions;

namespace Aviss.Rfid.Diagnostico;

/// <summary>Cómo se buscan los candidatos. Todo configurable porque todavía no se sabe qué manda el bastón real.</summary>
public sealed class OpcionesParserCaravana
{
    /// <summary>Cantidades de dígitos que cuentan como código completo (15 = ISO 11784).</summary>
    public int[] LongitudesDigitos { get; set; } = [15];

    /// <summary>Acepta grupos de dígitos unidos por guiones ("982-000123456789").</summary>
    public bool PermitirGuiones { get; set; } = true;

    /// <summary>Si es mayor que 0, también marca como "número corto" las secuencias de esa cantidad de dígitos o más
    /// que no llegan a ser un código completo (por si el bastón manda el número de caravana sin el resto). 0 = apagado.</summary>
    public int MinimoDigitosCorto { get; set; }
}

public enum TipoCandidato { Digitos, ConGuiones, NumeroCorto }

/// <summary>Algo que "parece" una caravana. No se decide nada con esto todavía: solo se muestra y se registra.</summary>
public sealed record CandidatoCaravana(string Texto, string Digitos, TipoCandidato Tipo, int Posicion)
{
    /// <summary>Primeros 3 dígitos si es un código de 15 (en ISO 11784 es el país o fabricante); si no, vacío.</summary>
    public string CodigoPais => Digitos.Length == 15 ? Digitos[..3] : "";

    /// <summary>Los 12 dígitos restantes si es un código de 15; si no, vacío.</summary>
    public string NumeroNacional => Digitos.Length == 15 ? Digitos[3..] : "";
}

/// <summary>Busca candidatos a código de caravana dentro de un texto. Función pura: se prueba con tramas inventadas.</summary>
public static class ParserCandidatosCaravana
{
    private static readonly Regex SoloDigitos = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex DigitosOGuiones = new(@"\d+(?:-\d+)*", RegexOptions.Compiled);

    public static IReadOnlyList<CandidatoCaravana> Buscar(string texto, OpcionesParserCaravana opciones)
    {
        var resultado = new List<CandidatoCaravana>();
        var regex = opciones.PermitirGuiones ? DigitosOGuiones : SoloDigitos;

        foreach (Match m in regex.Matches(texto))
        {
            var digitos = m.Value.Replace("-", "");
            var tieneGuiones = m.Value.Contains('-');

            if (opciones.LongitudesDigitos.Contains(digitos.Length))
                resultado.Add(new CandidatoCaravana(m.Value, digitos, tieneGuiones ? TipoCandidato.ConGuiones : TipoCandidato.Digitos, m.Index));
            else if (opciones.MinimoDigitosCorto > 0 && digitos.Length >= opciones.MinimoDigitosCorto && digitos.Length < opciones.LongitudesDigitos.DefaultIfEmpty(15).Min())
                resultado.Add(new CandidatoCaravana(m.Value, digitos, TipoCandidato.NumeroCorto, m.Index));
        }
        return resultado;
    }
}
