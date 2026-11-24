using System.Globalization;

namespace Aviss.Application.Importacion;

/// <summary>Qué es el contenido de la celda EID del archivo (spec 010).</summary>
public enum EstadoEid
{
    /// <summary>Celda vacía, solo espacios o un guion: la vaca todavía no tiene EID (caso normal, no es un error).</summary>
    SinEid,
    /// <summary>Exactamente 15 dígitos (una vez quitados los espacios).</summary>
    Valido,
    /// <summary>Cualquier otra cosa: letras, otra longitud, decimales, fechas, fórmulas...</summary>
    Invalido
}

/// <summary>El EID ya limpio (sin espacios) solo cuando es <see cref="EstadoEid.Valido"/>; en los otros dos casos es nulo.</summary>
public readonly record struct ResultadoEid(string? Valor, EstadoEid Estado);

/// <summary>
/// Spec 010 RF-2 a RF-4: cómo se interpreta la celda EID según su tipo. Función pura, sin archivos, hermana de
/// <see cref="ReglasIdImportacion"/>. Es una regla de LECTURA del archivo: la regla de guardado (hasta 32 caracteres
/// sin espacios) sigue siendo la de Domain.IdElectronico.
/// </summary>
public static class ReglasEidImportacion
{
    /// <summary>Largo de un EID válido (formato ISO de las caravanas electrónicas, por ejemplo 982 000458467123).</summary>
    public const int LargoEid = 15;

    // Un entero de 15 dígitos sin ceros a la izquierda va de 1e14 a 1e15 - 1.
    private const double MinimoNumerico = 1e14;
    private const double LimiteNumerico = 1e15;

    /// <param name="celda">La celda EID de la fila; nula si la fila no tiene esa celda (cuenta como "sin EID").</param>
    public static ResultadoEid Evaluar(CeldaLeida? celda)
    {
        if (celda is null || celda.Tipo == TipoCelda.Blanco) return SinEid;

        return celda.Tipo switch
        {
            TipoCelda.Texto => EvaluarTexto(celda.Texto),
            TipoCelda.Numerica => EvaluarNumero(celda.Numero),
            // Fecha, booleana, error o fórmula: no se adivina qué quiso decir el archivo.
            _ => Invalido
        };
    }

    private static ResultadoEid EvaluarTexto(string? texto)
    {
        // Se quita TODO el espacio en blanco (incluido NBSP y los de adentro): algunos lectores muestran el EID agrupado.
        var limpio = string.Concat((texto ?? "").Where(c => !char.IsWhiteSpace(c)));
        if (limpio.Length == 0 || limpio == "-") return SinEid;

        // Solo dígitos ASCII (char.IsDigit aceptaría también los de otros alfabetos).
        return limpio.Length == LargoEid && limpio.All(c => c is >= '0' and <= '9')
            ? new ResultadoEid(limpio, EstadoEid.Valido)
            : Invalido;
    }

    private static ResultadoEid EvaluarNumero(double? numero)
    {
        // Entero exacto de 15 dígitos. Un EID que empezara con cero perdería ese cero en una celda numérica y quedaría de
        // 14 dígitos: es inválido (con advertencia), nunca un rechazo del archivo.
        if (numero is not { } n
            || double.IsNaN(n) || double.IsInfinity(n)
            || n < MinimoNumerico || n >= LimiteNumerico
            || n != Math.Floor(n))
            return Invalido;

        return new ResultadoEid(((long)n).ToString(CultureInfo.InvariantCulture), EstadoEid.Valido);
    }

    private static ResultadoEid SinEid => new(null, EstadoEid.SinEid);
    private static ResultadoEid Invalido => new(null, EstadoEid.Invalido);
}
