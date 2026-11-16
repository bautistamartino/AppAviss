namespace Aviss.Domain.Reglas;

/// <summary>Validaciones chicas compartidas por las entidades.</summary>
internal static class Validar
{
    /// <summary>Todas las fechas del sistema son UTC (RF-17): una fecha local o sin tipo se rechaza.</summary>
    public static DateTime Utc(DateTime fecha, string nombre)
    {
        if (fecha.Kind != DateTimeKind.Utc)
            throw new DominioException($"La fecha '{nombre}' debe estar en UTC.");
        return fecha;
    }

    public static string TextoObligatorio(string? texto, string nombre, int? maximo = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            throw new DominioException($"{nombre} es obligatorio.");
        var limpio = texto.Trim();
        if (maximo is int max && limpio.Length > max)
            throw new DominioException($"{nombre} no puede superar los {max} caracteres.");
        return limpio;
    }

    /// <summary>Texto opcional: vacío o solo espacios se guarda como nulo.</summary>
    public static string? TextoOpcional(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    public static Guid IdObligatorio(Guid id, string nombre)
    {
        if (id == Guid.Empty) throw new DominioException($"{nombre} es obligatorio.");
        return id;
    }
}
