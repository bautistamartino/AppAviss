using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Enums;

namespace Aviss.Infrastructure.Servicios;

/// <summary>Cosas que repiten varios casos de uso.</summary>
internal static class Ayudas
{
    /// <summary>Cómo se escribe un Id dentro del historial: igual que lo guarda la base (texto en mayúsculas).</summary>
    public static string IdATexto(Guid? id) => id?.ToString().ToUpperInvariant() ?? "";

    public static string? IdATextoONulo(Guid? id) => id is null ? null : IdATexto(id);

    /// <summary>Texto opcional: vacío o solo espacios se guarda como nulo (misma regla que el dominio).</summary>
    public static string? TextoONulo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Convierte una DominioException (dato inválido que el dominio rechazó) en un Resultado de rechazo.
    /// Se usa cuando se llama a una fábrica del dominio dentro de un caso de uso.
    /// </summary>
    public static Resultado<T> DatosInvalidos<T>(DominioException ex) => Resultado.Error<T>(CodigoError.DatosInvalidos, ex.Message);

    public static Resultado DatosInvalidos(DominioException ex) => Resultado.Error(CodigoError.DatosInvalidos, ex.Message);

    /// <summary>Verifica que el usuario exista y esté activo: uno de baja no puede hacer registros nuevos (RF-24).</summary>
    public static async Task<Resultado?> ValidarUsuarioAsync(IUsuarioRepository usuarios, Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerAsync(usuarioId, ct);
        if (usuario is null)
            return Resultado.Error(CodigoError.NoEncontrado, "El usuario no existe.");
        if (!usuario.Activo)
            return Resultado.Error(CodigoError.UsuarioDeBaja, "El usuario está dado de baja y no puede hacer registros nuevos.");
        return null;
    }

    /// <summary>De la regla pura del dominio al código y mensaje que ve quien llama (RF-8b). Los mensajes no llevan datos.</summary>
    public static (CodigoError Codigo, string Mensaje) DeMotivo(MotivoRechazoTacto motivo) => motivo switch
    {
        MotivoRechazoTacto.AnimalAnulado => (CodigoError.AnimalAnulado, "El animal está anulado y no admite tactos."),
        MotivoRechazoTacto.RodeoDeBaja => (CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no admite tactos."),
        MotivoRechazoTacto.EstablecimientoDeBaja => (CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite tactos."),
        MotivoRechazoTacto.EstadoNuevoDeBaja => (CodigoError.EstadoDeBaja, "El estado elegido está dado de baja."),
        MotivoRechazoTacto.EstadoAnteriorNoCoincide => (CodigoError.EstadoAnteriorNoCoincide, "El estado del animal cambió: volvé a leerlo antes de registrar el tacto."),
        MotivoRechazoTacto.FechaFutura => (CodigoError.FechaFutura, "La hora de la tablet está adelantada. Revisá la fecha y la hora del dispositivo."),
        MotivoRechazoTacto.FechaAnteriorAlUltimoTacto => (CodigoError.FechaAnteriorAlUltimoTacto, "La hora de la tablet es anterior al último tacto de este animal. Revisá la fecha y la hora del dispositivo."),
        MotivoRechazoTacto.DiasPrenezFueraDeRango => (CodigoError.DiasPrenezFueraDeRango, "Los días de preñez deben estar entre 0 y 300."),
        _ => (CodigoError.DatosInvalidos, "El tacto no es válido.")
    };
}
