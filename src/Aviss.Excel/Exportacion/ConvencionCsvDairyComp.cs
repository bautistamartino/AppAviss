using System.Text;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Cómo se escribe un CSV cuando el original es un .xls (spec 007, preguntas 3 y 9; valores PROVISORIOS en un solo lugar):
/// separador <c>;</c>, Windows-1252 (acá Latin-1, que coincide en la Ñ y las tildes y es lo que ya usa la lectura), encabezados
/// entre comillas, un <c>;</c> al final de cada línea y saltos CRLF; decimales con punto. Un original que YA es CSV no usa esto:
/// se respeta byte a byte.
/// </summary>
internal static class ConvencionCsvDairyComp
{
    public const char Separador = ';';
    public const string Salto = "\r\n";

    /// <summary>Latin-1 que lanza una excepción (en vez de escribir '?') si el texto trae un carácter que no admite.</summary>
    public static Encoding CodificacionEstricta(Encoding base_)
    {
        var copia = (Encoding)base_.Clone();
        copia.EncoderFallback = EncoderFallback.ExceptionFallback;
        return copia;
    }

    /// <summary>Un campo de datos: tal cual, y entre comillas (duplicando las internas) solo si trae el separador, comillas o un salto.</summary>
    public static string Campo(string valor, char separador = Separador)
        => valor.IndexOfAny([separador, '"', '\r', '\n']) >= 0 ? Entrecomillar(valor) : valor;

    public static string Entrecomillar(string valor) => "\"" + valor.Replace("\"", "\"\"") + "\"";
}
