using Aviss.Domain.Catalogo;
using Aviss.Domain.Enums;

namespace Aviss.Application.Campo;

/// <summary>
/// Arma el texto de un evento para el historial, los movimientos y el aviso de repetido (spec 005, RF-25, RF-30, RF-31):
/// "Aborto, 45 días, Vacía OK", "Preñada fecha previa, 130 días". Un Aborto viejo (sin días ni resultado) queda "Aborto".
/// </summary>
public static class DescripcionEvento
{
    /// <param name="nombreEvento2">Spec 011: el segundo evento del registro; se une al final con " + " ("OK + PROSTAG"). Nulo o vacío = un solo evento, igual que siempre.</param>
    public static string Armar(string nombreEvento, int? diasPreniez = null, int? diasAborto = null, ResultadoAborto? resultado = null,
        string? nombreEvento2 = null)
    {
        var partes = new List<string> { nombreEvento };
        if (diasPreniez is int p) partes.Add(Dias(p));
        if (diasAborto is int a) partes.Add(Dias(a));
        if (resultado is ResultadoAborto r) partes.Add(ReglasResultadoAborto.Etiqueta(r));
        var texto = string.Join(", ", partes);
        return string.IsNullOrWhiteSpace(nombreEvento2) ? texto : $"{texto}{Separador}{nombreEvento2}";
    }

    /// <summary>Lo que une los dos eventos de un registro en toda la app (historial, movimientos, avisos, "Último guardado").</summary>
    public const string Separador = " + ";

    private static string Dias(int n) => n == 1 ? "1 día" : $"{n} días";
}
