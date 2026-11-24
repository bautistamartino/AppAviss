using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace Aviss.Excel;

/// <summary>
/// Lee un listado de Dairy Comp guardado como CSV o TXT ("Save as .CSV" / ".TXT") y lo deja como una hoja en memoria,
/// para que <see cref="PerfilDairyComp"/> aplique las mismas reglas que al .xls (encabezados, fila Total, columnas
/// opcionales). Es tolerante porque el formato real todavía no está confirmado: detecta el separador (coma, punto y coma
/// o tabulación) y la codificación (UTF-8, UTF-16 con BOM o Windows-1252), y entiende campos entre comillas.
/// Nunca lanza: si el contenido no parece un listado con columna ID, devuelve nulo.
/// </summary>
internal static partial class LectorCsv
{
    private static readonly char[] Separadores = [',', ';', '\t'];

    // Entero sin ceros a la izquierda (un ID "007" es texto) o decimal con punto: se guardan como número, igual que el .xls.
    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronNumero();

    // Decimal con coma ("12,5"), solo cuando el separador del archivo no es la coma.
    [GeneratedRegex(@"^-?(0|[1-9][0-9]*),[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronNumeroConComa();

    /// <summary>
    /// Un registro (línea lógica) del CSV con sus campos ya separados y su posición en el texto decodificado, para que la
    /// exportación (spec 007) pueda agregar columnas al final de cada línea sin tocar el resto. <see cref="Fin"/> es donde termina
    /// el contenido (antes del salto de línea) y <see cref="Terminador"/> el salto que lo cierra ("\r\n", "\n", "\r" o "" en la última).
    /// </summary>
    internal readonly record struct RegistroCsv(List<string> Campos, int Inicio, int Fin, string Terminador);

    /// <summary>
    /// El CSV tal como está en los bytes: texto, codificación con su marca de orden de bytes (para volver a escribirlo igual),
    /// separador y registros. Es lo que usa la exportación para editar el original A NIVEL DE TEXTO.
    /// </summary>
    internal sealed record CsvAnalizado(string Texto, Encoding Codificacion, byte[] Preambulo, char Separador, List<RegistroCsv> Registros);

    /// <summary>Decodifica y parte en registros con las MISMAS reglas que <see cref="ComoHoja"/>. Nulo si no es un listado de texto.</summary>
    internal static CsvAnalizado? Analizar(byte[] contenido)
    {
        try
        {
            var texto = Decodificar(contenido, out var codificacion, out var preambulo);
            if (texto is null) return null;

            var posiciones = new List<RegistroCsv>();
            Separar(texto, out char separador, posiciones);
            return separador == '\0' ? null : new CsvAnalizado(texto, codificacion, preambulo, separador, posiciones);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>La hoja con el contenido, o nulo si no es un listado de texto con una columna ID.</summary>
    public static ISheet? ComoHoja(byte[] contenido)
    {
        try
        {
            var texto = Decodificar(contenido, out _, out _);
            if (texto is null) return null;

            var filas = Separar(texto, out char separador, null);
            if (separador == '\0') return null;

            // El encabezado es la primera fila con contenido y tiene que traer ID (igual que el .xls).
            var primera = filas.FirstOrDefault(f => f.Any(c => c.Trim().Length > 0));
            if (primera is null || !primera.Any(c => string.Equals(c.Trim(), "ID", StringComparison.OrdinalIgnoreCase)))
                return null;

            var libro = new HSSFWorkbook();
            var hoja = libro.CreateSheet("Listado");
            for (int i = 0; i < filas.Count; i++)
            {
                var fila = filas[i];
                if (fila.All(c => c.Length == 0)) continue; // línea vacía: no se crea (se conserva la numeración)
                var f = hoja.CreateRow(i);
                for (int c = 0; c < fila.Count; c++)
                {
                    var valor = fila[c];
                    if (valor.Length == 0) continue;
                    var celda = f.CreateCell(c);
                    if (EsNumero(valor, separador, out double numero)) celda.SetCellValue(numero);
                    else celda.SetCellValue(valor);
                }
            }
            return hoja;
        }
        catch (Exception)
        {
            return null; // cualquier problema = "no es un listado que entendamos"
        }
    }

    private static bool EsNumero(string valor, char separador, out double numero)
    {
        numero = 0;
        var texto = valor.Trim();
        if (PatronNumero().IsMatch(texto))
            return double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out numero);
        if (separador != ',' && PatronNumeroConComa().IsMatch(texto))
            return double.TryParse(texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numero);
        return false;
    }

    /// <summary>
    /// Bytes a texto. Nulo si parece binario (con caracteres NUL). Además devuelve la codificación y los bytes de la marca de orden
    /// (BOM) que traía el archivo, para que la exportación lo vuelva a escribir igual.
    /// </summary>
    private static string? Decodificar(byte[] b, out Encoding codificacion, out byte[] preambulo)
    {
        string texto;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            codificacion = new UTF8Encoding(false); preambulo = b[..3];
            texto = Encoding.UTF8.GetString(b, 3, b.Length - 3);
        }
        else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            codificacion = new UnicodeEncoding(false, false); preambulo = b[..2];
            texto = Encoding.Unicode.GetString(b, 2, b.Length - 2);
        }
        else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            codificacion = new UnicodeEncoding(true, false); preambulo = b[..2];
            texto = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        }
        else
        {
            preambulo = [];
            try { codificacion = new UTF8Encoding(false, throwOnInvalidBytes: true); texto = codificacion.GetString(b); }
            catch (DecoderFallbackException) { codificacion = Encoding.Latin1; texto = Encoding.Latin1.GetString(b); } // Windows-1252: sirve para las tildes y la Ñ
        }
        return texto.Contains('\0') ? null : texto;
    }

    /// <summary>
    /// Parte el texto en filas de campos. Elige el separador que más aparece en la primera línea con contenido (fuera de
    /// comillas); '\0' si ninguno aparece (un texto sin columnas).
    /// </summary>
    private static List<List<string>> Separar(string texto, out char separador, List<RegistroCsv>? posiciones)
    {
        separador = ElegirSeparador(texto);
        var filas = new List<List<string>>();
        if (separador == '\0') return filas;

        var fila = new List<string>();
        var campo = new StringBuilder();
        bool entreComillas = false;
        int inicioRegistro = 0; // donde empieza el registro actual en el texto (solo se usa para informar las posiciones)

        void CerrarCampo() { fila.Add(campo.ToString()); campo.Clear(); }
        void CerrarFila(int fin, string terminador, int siguienteInicio)
        {
            CerrarCampo();
            filas.Add(fila);
            posiciones?.Add(new RegistroCsv(fila, inicioRegistro, fin, terminador));
            fila = new List<string>();
            inicioRegistro = siguienteInicio;
        }

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];
            if (entreComillas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else entreComillas = false;
                }
                else campo.Append(c);
            }
            else if (c == '"' && campo.Length == 0) entreComillas = true;
            else if (c == separador) CerrarCampo();
            else if (c == '\r')
            {
                int fin = i;
                bool crlf = i + 1 < texto.Length && texto[i + 1] == '\n';
                if (crlf) i++;
                CerrarFila(fin, crlf ? "\r\n" : "\r", i + 1);
            }
            else if (c == '\n') CerrarFila(i, "\n", i + 1);
            else campo.Append(c);
        }
        if (campo.Length > 0 || fila.Count > 0) CerrarFila(texto.Length, "", texto.Length);
        return filas;
    }

    private static char ElegirSeparador(string texto)
    {
        var conteo = Separadores.ToDictionary(s => s, _ => 0);
        bool entreComillas = false, hayContenido = false;
        foreach (char c in texto)
        {
            if (c == '"') { entreComillas = !entreComillas; continue; }
            if (!entreComillas && (c == '\n' || c == '\r'))
            {
                if (hayContenido) break; // terminó la primera línea con contenido
                continue;
            }
            if (!char.IsWhiteSpace(c) || c == '\t') hayContenido = true;
            if (!entreComillas && conteo.ContainsKey(c)) conteo[c]++;
        }
        var mejor = conteo.OrderByDescending(kv => kv.Value).First();
        return mejor.Value > 0 ? mejor.Key : '\0';
    }
}
