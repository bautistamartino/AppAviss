using Aviss.Application.Comun;
using Aviss.Domain.Enums;

namespace Aviss.Application.Campo;

/// <summary>
/// Mensajes fijos en español del flujo de campo (spec 003, RF-33): nunca llevan caravanas ni nombres de tambos.
/// Cada código nuevo y cada motivo de rechazo tiene uno; un test comprueba que ninguno falte.
/// </summary>
public static class MensajesCampo
{
    private const string AvisoDeHora = "Revisá la fecha y la hora del dispositivo.";

    /// <summary>Mensaje de los códigos de error propios del flujo de campo. Para otros códigos lanza.</summary>
    public static string Para(CodigoError codigo) => codigo switch
    {
        CodigoError.NoHayMangaAbierta => "No hay una manga abierta: no hay nada para finalizar.",
        CodigoError.CantidadInvalida => "La cantidad de animales no es válida: tiene que ser un número entero, cero o más.",
        CodigoError.MarcaYaAnulada => "Esa vaca ya estaba desmarcada: no hay nada para deshacer.",
        CodigoError.MarcaNoEsLaUltima => "Solo se puede deshacer lo último que se hizo con la vaca; antes hay que deshacer lo posterior.",
        CodigoError.AnimalConMarcasVigentes => "La vaca tiene marcas de pasada vigentes: primero hay que desmarcarla.",
        CodigoError.CierreYaAnulado => "Ese cierre de manga ya se había deshecho.",
        CodigoError.CierreNoEsElUltimo => "Solo se puede deshacer el último cierre de manga del rodeo.",
        CodigoError.CierreAutomaticoNoSeDeshace => "Un cierre automático no se deshace: la manga se cerró sola por quedar abierta de un día anterior.",
        CodigoError.CierreDeOtroDia => "Solo se puede deshacer un cierre de manga de hoy.",
        CodigoError.HayOtraMangaAbierta => "Ya se empezó otra manga en este rodeo: no se puede reabrir la anterior.",
        CodigoError.FechaAnteriorAlUltimoRegistro =>
            "La hora de la tablet es anterior al último registro de este animal. " + AvisoDeHora,
        CodigoError.VacaYaEnEspera => "Esa vaca ya está en espera.",
        CodigoError.VacaNoEntraEnTacto => "Esa vaca no entra en tacto (CVET 0): no se puede poner en espera.",
        CodigoError.EsperaNoPendiente => "Esa vaca no tiene una espera pendiente.",
        CodigoError.VacaEnEspera => "Está en espera: primero resolvela o saltala.",
        CodigoError.RechazoYaAnulado => "Ese rechazo ya se había deshecho.",
        CodigoError.RechazoNoEsElUltimo => "Solo se puede deshacer lo último que se hizo con la vaca; antes hay que deshacer lo posterior.",
        _ => throw new ArgumentOutOfRangeException(nameof(codigo), "Ese código no es del flujo de campo.")
    };

    /// <summary>De la regla pura de la marca de pasada al código y mensaje que ve quien llama (RF-21).</summary>
    public static (CodigoError Codigo, string Mensaje) Para(MotivoRechazoMarca motivo) => motivo switch
    {
        MotivoRechazoMarca.AnimalAnulado => (CodigoError.AnimalAnulado, "El animal está anulado y no admite marcas de pasada."),
        MotivoRechazoMarca.RodeoDeBaja => (CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no admite marcas de pasada."),
        MotivoRechazoMarca.EstablecimientoDeBaja => (CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite marcas de pasada."),
        MotivoRechazoMarca.FechaFutura => (CodigoError.FechaFutura, "La hora de la tablet está adelantada. " + AvisoDeHora),
        MotivoRechazoMarca.FechaAnteriorAlUltimoRegistro =>
            (CodigoError.FechaAnteriorAlUltimoRegistro, Para(CodigoError.FechaAnteriorAlUltimoRegistro)),
        _ => throw new ArgumentOutOfRangeException(nameof(motivo))
    };

    /// <summary>Igual que <see cref="Para(MotivoRechazoMarca)"/>, pero con los textos del rechazo (spec 004, RF-6): son las mismas reglas.</summary>
    public static (CodigoError Codigo, string Mensaje) ParaRechazo(MotivoRechazoMarca motivo) => motivo switch
    {
        MotivoRechazoMarca.AnimalAnulado => (CodigoError.AnimalAnulado, "El animal está anulado y no admite rechazos."),
        MotivoRechazoMarca.RodeoDeBaja => (CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no admite rechazos."),
        MotivoRechazoMarca.EstablecimientoDeBaja => (CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite rechazos."),
        _ => Para(motivo)
    };

    /// <summary>De la regla pura de reapertura al código y mensaje (RF-9).</summary>
    public static (CodigoError Codigo, string Mensaje) Para(MotivoNoReabrir motivo) => motivo switch
    {
        MotivoNoReabrir.CierreYaAnulado => (CodigoError.CierreYaAnulado, Para(CodigoError.CierreYaAnulado)),
        MotivoNoReabrir.CierreNoEsElUltimo => (CodigoError.CierreNoEsElUltimo, Para(CodigoError.CierreNoEsElUltimo)),
        MotivoNoReabrir.CierreAutomatico => (CodigoError.CierreAutomaticoNoSeDeshace, Para(CodigoError.CierreAutomaticoNoSeDeshace)),
        MotivoNoReabrir.CierreDeOtroDia => (CodigoError.CierreDeOtroDia, Para(CodigoError.CierreDeOtroDia)),
        MotivoNoReabrir.HayOtraMangaAbierta => (CodigoError.HayOtraMangaAbierta, Para(CodigoError.HayOtraMangaAbierta)),
        _ => throw new ArgumentOutOfRangeException(nameof(motivo))
    };
}
