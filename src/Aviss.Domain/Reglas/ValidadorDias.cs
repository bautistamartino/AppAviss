namespace Aviss.Domain.Reglas;

/// <summary>Lo que devuelve <see cref="ValidadorDias.Validar"/>: el valor o el mensaje para mostrar (nunca los dos).</summary>
public readonly record struct ResultadoDias(int? Valor, string? Mensaje)
{
    public bool EsValido => Valor is not null;
}

/// <summary>
/// Valida los días escritos en un campo de texto (spec 005, RF-23 y RF-28). Pura: la usan el ViewModel y los servicios,
/// porque el servicio nunca confía en lo que ya validó la pantalla.
/// </summary>
public static class ValidadorDias
{
    /// <summary>
    /// Acepta solo dígitos (con espacios de los costados a lo sumo): rechaza vacío, negativos, decimales, notación
    /// científica, espacios internos y texto. Después comprueba el rango.
    /// </summary>
    public static ResultadoDias Validar(string? texto, RangoDias rango)
    {
        var t = texto?.Trim() ?? "";
        if (t.Length == 0)
            return new(null, $"{rango.Descripcion} son obligatorios: escribí un número entero entre {rango.Minimo} y {rango.Maximo}.");

        // Solo dígitos ASCII (char.IsDigit aceptaría también dígitos de otros alfabetos).
        foreach (var c in t)
            if (c is < '0' or > '9')
                return new(null, $"{rango.Descripcion}: escribí un número entero entre {rango.Minimo} y {rango.Maximo}, sin signo, comas ni puntos.");

        // Un texto de solo dígitos pero larguísimo no entra en un int: es "fuera de rango".
        if (t.Length > 9 || !int.TryParse(t, out var dias) || !rango.Contiene(dias))
            return new(null, $"{rango.Descripcion} tienen que estar entre {rango.Minimo} y {rango.Maximo}.");

        return new(dias, null);
    }

    /// <summary>Valida un número ya parseado (lo usa el servicio con lo que llega en el comando).</summary>
    public static bool EnRango(int dias, RangoDias rango) => rango.Contiene(dias);
}
