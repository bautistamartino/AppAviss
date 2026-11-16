using System.Diagnostics.CodeAnalysis;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Identificador electrónico (RFID) de un animal, opcional. RF-4b: se guarda sin espacios y hasta 32 caracteres.
/// Decisión: los espacios se QUITAN (todos, también los del medio) en vez de rechazar el valor,
/// porque algunos lectores muestran el número agrupado ("982 000123456789").
/// </summary>
public sealed record IdElectronico
{
    public const int LargoMaximo = 32;

    public string Valor { get; }

    private IdElectronico(string valor) => Valor = valor;

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out IdElectronico? id, out string? error)
    {
        id = null;
        var sinEspacios = texto is null ? "" : new string(texto.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (sinEspacios.Length == 0)
        {
            error = "El identificador electrónico no puede estar vacío.";
            return false;
        }
        if (sinEspacios.Length > LargoMaximo)
        {
            error = $"El identificador electrónico no puede superar los {LargoMaximo} caracteres.";
            return false;
        }
        error = null;
        id = new IdElectronico(sinEspacios);
        return true;
    }

    public static IdElectronico Crear(string? texto)
    {
        if (!TryCrear(texto, out var id, out var error))
            throw new DominioException(error!);
        return id;
    }

    /// <summary>Para campos opcionales: vacío o nulo da nulo; si trae algo, lo valida y lo limpia.</summary>
    public static string? NormalizarOpcional(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : Crear(texto).Valor;

    public override string ToString() => Valor;
}
