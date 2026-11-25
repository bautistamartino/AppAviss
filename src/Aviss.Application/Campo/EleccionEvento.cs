using Aviss.Domain.Enums;

namespace Aviss.Application.Campo;

/// <summary>
/// Spec 011: el Evento 2 de la ficha (con su observación propia). Nunca cambia el estado ni lleva pasos; el servicio revalida que
/// se pueda combinar con el Evento 1 (<c>ReglasSegundoEvento</c>).
/// </summary>
public sealed record SegundoEventoElegido(Guid EventoId, string? Observaciones);

/// <summary>
/// Lo que queda "en el casillero" de la ficha tras elegir un evento y completar sus pasos (spec 005, RF-27, RF-29, RF-30):
/// el evento, los días de preñez (Preñada fecha previa), los días de aborto y el resultado (Aborto). Los datos que el evento
/// no pide van en nulo. El servicio revalida todo: nunca confía en lo que ya validó la pantalla.
/// </summary>
public sealed record EleccionEvento(
    Guid EventoId, int? DiasPreniez = null, int? DiasAborto = null, ResultadoAborto? ResultadoAborto = null,
    SegundoEventoElegido? Segundo = null)
{
    /// <summary>
    /// Es el Rechazo fijo o un Aborto con resultado Rechazo: se guarda como registro de rechazo y no cambia el estado de la vaca.
    /// (Un "solo evento" del catálogo, como OK, también se guarda en esa tabla, pero eso depende del tipo del evento y lo
    /// decide quien lo conoce: <c>!EventoCampoDto.CambiaEstado</c>.)
    /// </summary>
    public bool RegistraComoRechazo => EventoId == EventosCampo.Rechazo || ResultadoAborto == Domain.Enums.ResultadoAborto.Rechazo;
}
