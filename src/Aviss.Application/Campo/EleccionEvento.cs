using Aviss.Domain.Enums;

namespace Aviss.Application.Campo;

/// <summary>
/// Lo que queda "en el casillero" de la ficha tras elegir un evento y completar sus pasos (spec 005, RF-27, RF-29, RF-30):
/// el evento, los días de preñez (Preñada fecha previa), los días de aborto y el resultado (Aborto). Los datos que el evento
/// no pide van en nulo. El servicio revalida todo: nunca confía en lo que ya validó la pantalla.
/// </summary>
public sealed record EleccionEvento(
    Guid EventoId, int? DiasPreniez = null, int? DiasAborto = null, ResultadoAborto? ResultadoAborto = null)
{
    /// <summary>
    /// Es el Rechazo fijo o un Aborto con resultado Rechazo: se guarda como registro de rechazo y no cambia el estado de la vaca.
    /// (Un "solo evento" del catálogo, como OK, también se guarda en esa tabla, pero eso depende del tipo del evento y lo
    /// decide quien lo conoce: <c>!EventoCampoDto.CambiaEstado</c>.)
    /// </summary>
    public bool RegistraComoRechazo => EventoId == EventosCampo.Rechazo || ResultadoAborto == Domain.Enums.ResultadoAborto.Rechazo;
}
