using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Spec 011: los datos de un evento del catálogo que importan para combinarlo con otro en el mismo tacto. Se arma desde la
/// fila del catálogo (<see cref="De"/>) y así las reglas no dependen de la base ni de cómo se llama el evento.
/// </summary>
/// <param name="Id">Identidad del evento (para detectar "el mismo evento dos veces").</param>
/// <param name="Activo">Un evento de baja no se puede elegir.</param>
/// <param name="Tipo">Estado (cambia el estado de la vaca) o SoloEvento (OK, Revisar Nuevamente).</param>
/// <param name="ClaveEstandar">Nula en los eventos creados a mano.</param>
/// <param name="TienePasos">El evento pide pasos después de elegirlo (Aborto, Preñada fecha previa).</param>
/// <param name="TieneDestino">El evento deja a la vaca en otro estado (Preñada fecha previa).</param>
public sealed record DatosEvento(
    Guid Id, bool Activo, TipoEvento Tipo, string? ClaveEstandar, bool TienePasos, bool TieneDestino)
{
    public static DatosEvento De(EstadoReproductivo e)
        => new(e.Id, e.Activo, e.Tipo, e.ClaveEstandar, !string.IsNullOrWhiteSpace(e.Pasos), e.EstadoDestinoId is not null);
}

/// <summary>Por qué no se puede guardar un Evento 2. El servicio los traduce a mensajes en español.</summary>
public enum MotivoSegundoEvento
{
    SegundoIgualAlPrimero,
    PrimeroNoAdmiteSegundo,
    SegundoNoCombinable,
    SegundoDeBaja,
    SegundoInexistente,
    ObservacionLarga
}

/// <summary>
/// Spec 011 (dos eventos por tacto): qué eventos pueden ir juntos. Reglas puras: las usan el servicio (que decide), la lista
/// de eventos y la pantalla (que solo muestran). El Evento 2 nunca cambia el estado ni lleva pasos: por eso solo se combinan
/// eventos sin pasos y sin destino, y como segundo solo los que no son un estándar de estado.
/// </summary>
public static class ReglasSegundoEvento
{
    /// <summary>Tope de cada observación (la del Evento 1 y la del Evento 2). Se controla en pantalla y servicio, no en la base.</summary>
    public const int MaximoObservaciones = 300;

    /// <summary>
    /// ¿Este evento puede ir como Evento 1 con un segundo? Activo, sin pasos y sin destino. Incluye OK, Revisar Nuevamente,
    /// Preñada fecha correcta, VACIA, NOINSEM, INSEMIN, SECA, FRESCA y los creados a mano. Excluye Aborto y Preñada fecha previa.
    /// El RECHAZO fijo no es una fila del catálogo: quien llama lo trata como "no admite" (no tiene <see cref="DatosEvento"/>).
    /// </summary>
    public static bool AdmiteSegundo(DatosEvento e) => e.Activo && !e.TienePasos && !e.TieneDestino;

    /// <summary>
    /// ¿Puede ir como Evento 2? Admite segundo y además es de tipo solo evento (OK, Revisar Nuevamente) o creado a mano
    /// (sin clave estándar). Los estándar de estado (VACIA, INSEMIN, SECA, FRESCA...) quedan afuera.
    /// </summary>
    public static bool PuedeSerSegundo(DatosEvento e)
        => AdmiteSegundo(e) && (e.Tipo == TipoEvento.SoloEvento || e.ClaveEstandar is null);

    /// <summary>
    /// Valida la combinación al guardar y devuelve el motivo del rechazo, o nada si es válida.
    /// <paramref name="primero"/> nulo = el RECHAZO fijo (no admite segundo); <paramref name="segundo"/> nulo = el Evento 2
    /// elegido no existe en el catálogo.
    /// </summary>
    public static MotivoSegundoEvento? Validar(DatosEvento? primero, DatosEvento? segundo, string? observaciones1, string? observaciones2)
    {
        if (primero is null || !AdmiteSegundo(primero)) return MotivoSegundoEvento.PrimeroNoAdmiteSegundo;
        if (segundo is null) return MotivoSegundoEvento.SegundoInexistente;
        if (segundo.Id == primero.Id) return MotivoSegundoEvento.SegundoIgualAlPrimero;
        if (!segundo.Activo) return MotivoSegundoEvento.SegundoDeBaja;
        if (!PuedeSerSegundo(segundo)) return MotivoSegundoEvento.SegundoNoCombinable;
        if (Largo(observaciones1) || Largo(observaciones2)) return MotivoSegundoEvento.ObservacionLarga;
        return null;
    }

    private static bool Largo(string? texto) => texto is not null && texto.Trim().Length > MaximoObservaciones;
}
