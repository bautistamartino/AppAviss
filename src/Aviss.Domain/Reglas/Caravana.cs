using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Número de caravana (el ID de Dairy Comp). Reglas de RF-4: obligatorio, hasta 20 caracteres,
/// sin espacios al principio ni al final, y se guarda tal cual (los ceros a la izquierda se conservan).
/// </summary>
public sealed record Caravana
{
    public const int LargoMaximo = 20;

    /// <summary>El texto tal como se guarda.</summary>
    public string Valor { get; }

    /// <summary>
    /// Clave para detectar repetidos sin distinguir mayúsculas. Es la MISMA regla que la columna generada
    /// de SQLite (<c>NumeroCaravanaClave</c>); un test de Infrastructure comprueba que dan igual.
    /// </summary>
    public string Clave { get; }

    private Caravana(string valor)
    {
        Valor = valor;
        Clave = CalcularClave(valor);
    }

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out Caravana? caravana, out string? error)
    {
        caravana = null;
        error = Validacion(texto);
        if (error is not null) return false;
        caravana = new Caravana(texto!);
        return true;
    }

    public static Caravana Crear(string? texto)
    {
        if (!TryCrear(texto, out var caravana, out var error))
            throw new DominioException(error!);
        return caravana;
    }

    private static string? Validacion(string? texto)
    {
        if (string.IsNullOrEmpty(texto) || string.IsNullOrWhiteSpace(texto))
            return "El número de caravana es obligatorio.";
        if (texto.Length > LargoMaximo)
            return $"El número de caravana no puede superar los {LargoMaximo} caracteres.";
        if (char.IsWhiteSpace(texto[0]) || char.IsWhiteSpace(texto[^1]))
            return "El número de caravana no puede empezar ni terminar con espacios.";
        return null;
    }

    /// <summary>
    /// Pasa a mayúsculas solo las letras ASCII y ñ á é í ó ú ü. NO usa ToUpperInvariant a propósito:
    /// SQLite hace lo mismo con <c>upper()</c> + <c>replace()</c> y las dos reglas tienen que coincidir
    /// exactamente (otros caracteres, como la ç, quedan como vienen).
    /// </summary>
    public static string CalcularClave(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => (char)(c - 32),
                'ñ' => 'Ñ',
                'á' => 'Á',
                'é' => 'É',
                'í' => 'Í',
                'ó' => 'Ó',
                'ú' => 'Ú',
                'ü' => 'Ü',
                _ => c
            });
        }
        return sb.ToString();
    }

    public override string ToString() => Valor;
}
