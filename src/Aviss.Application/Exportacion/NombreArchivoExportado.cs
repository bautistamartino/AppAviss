using Aviss.Domain.Enums;

namespace Aviss.Application.Exportacion;

/// <summary>
/// El nombre del archivo de vuelta (spec 007, RF-19, pregunta 12): <c>&lt;original sin extensión&gt;_aviss_AAAAMMDD_HHmm.&lt;csv|xls&gt;</c>, con la
/// fecha y hora LOCALES. Función pura: no mira el disco, así que "no pisar un archivo existente" lo resuelve quien guarda.
/// </summary>
public static class NombreArchivoExportado
{
    private const int LargoMaximoBase = 80;
    private const string BaseSiFalta = "rodeo";

    public static string Crear(string? nombreOriginal, DateTime ahoraUtc, TimeZoneInfo zona, FormatoExportacion formato)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, zona);
        var extension = formato == FormatoExportacion.Csv ? "csv" : "xls";
        return $"{BaseLimpia(nombreOriginal)}_aviss_{local:yyyyMMdd}_{local:HHmm}.{extension}";
    }

    /// <summary>El nombre sin su última extensión, sin caracteres que un archivo no admite y con un largo razonable.</summary>
    private static string BaseLimpia(string? nombreOriginal)
    {
        var texto = (nombreOriginal ?? "").Trim();
        // Solo el nombre, sin ninguna ruta (una barra o una barra invertida separan carpetas).
        var corte = texto.LastIndexOfAny(['/', '\\']);
        if (corte >= 0) texto = texto[(corte + 1)..];

        var punto = texto.LastIndexOf('.');
        if (punto > 0) texto = texto[..punto];

        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(texto.Where(c => !invalidos.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (limpio.Length > LargoMaximoBase) limpio = limpio[..LargoMaximoBase].TrimEnd();
        return limpio.Length == 0 ? BaseSiFalta : limpio;
    }
}
