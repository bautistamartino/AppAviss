using System.Globalization;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Importacion;

/// <summary>Resultado de interpretar la celda ID: una caravana válida o el motivo por el que no sirve.</summary>
public readonly record struct ResultadoId(Caravana? Caravana, MotivoFila? Motivo)
{
    public bool EsValido => Caravana is not null;
}

/// <summary>
/// RF-7: cómo se obtiene el número de caravana de la celda ID según su tipo. Función pura, sin archivos.
/// </summary>
public static class ReglasIdImportacion
{
    /// <summary>Una numérica entera tiene como máximo 15 dígitos (todo lo que un double representa sin perder precisión).</summary>
    private const double LimiteNumerico = 1e15;

    /// <param name="celda">La celda ID de la fila; nulo si la fila no tiene esa celda (cuenta como vacía).</param>
    public static ResultadoId Evaluar(CeldaLeida? celda)
    {
        if (celda is null) return Falla(MotivoFila.IdVacio);

        return celda.Tipo switch
        {
            TipoCelda.Blanco => Falla(MotivoFila.IdVacio),
            TipoCelda.Texto => EvaluarTexto(celda.Texto),
            TipoCelda.Numerica => EvaluarNumero(celda.Numero),
            // Fecha, booleana, error o fórmula: no se adivina qué quiso decir el archivo.
            _ => Falla(MotivoFila.IdTipoInvalido)
        };
    }

    private static ResultadoId EvaluarTexto(string? texto)
    {
        // Se recorta con char.IsWhiteSpace (incluye NBSP) ANTES de Caravana.TryCrear, que no recorta.
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return Falla(MotivoFila.IdVacio);

        // Espacios o caracteres de control en el medio (los de los bordes ya se recortaron).
        foreach (var c in t)
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return Falla(MotivoFila.IdCaracteres);

        if (t.Length > Caravana.LargoMaximo) return Falla(MotivoFila.IdLargo);

        return Caravana.TryCrear(t, out var caravana, out _)
            ? new ResultadoId(caravana, null)
            : Falla(MotivoFila.IdCaracteres);   // no debería pasar: ya se validó todo lo que valida Caravana
    }

    private static ResultadoId EvaluarNumero(double? numero)
    {
        // Entera exacta, no negativa y de hasta 15 dígitos; cualquier otra cosa (decimal, negativa,
        // científica, NaN, infinito) es inválida. 123.0 pasa a "123".
        if (numero is not { } n
            || double.IsNaN(n) || double.IsInfinity(n)
            || n < 0 || n >= LimiteNumerico
            || n != Math.Floor(n))
            return Falla(MotivoFila.IdNumeroInvalido);

        var texto = ((long)n).ToString(CultureInfo.InvariantCulture);
        return Caravana.TryCrear(texto, out var caravana, out _)
            ? new ResultadoId(caravana, null)
            : Falla(MotivoFila.IdNumeroInvalido);
    }

    private static ResultadoId Falla(MotivoFila motivo) => new(null, motivo);
}
