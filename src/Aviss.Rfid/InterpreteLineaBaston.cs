namespace Aviss.Rfid;

/// <summary>
/// Decide si una línea que mandó el bastón es un EID (RF-18, RF-20). Función pura, sin estado: el bastón manda
/// <c>982 000458467499</c> (15 dígitos, con un espacio después del país); se entrega sin espacios. Todo lo demás es basura
/// y devuelve <c>null</c> (no hay mensajes ni avisos: se ignora en silencio).
/// </summary>
public static class InterpreteLineaBaston
{
    private const int LargoEid = 15;

    /// <returns>Los 15 dígitos sin espacios, o <c>null</c> si la línea es basura.</returns>
    public static string? Interpretar(byte[] linea)
    {
        Span<char> digitos = stackalloc char[LargoEid];
        var cantidad = 0;
        foreach (var b in linea)
        {
            if (b == (byte)' ') continue;                  // los espacios no cuentan
            if (b is < (byte)'0' or > (byte)'9') return null;   // letras, NUL, bytes altos, guiones: basura
            if (cantidad == LargoEid) return null;          // más de 15 dígitos
            digitos[cantidad++] = (char)b;
        }
        return cantidad == LargoEid ? new string(digitos) : null;
    }
}
