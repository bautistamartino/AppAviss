using Aviss.Domain.Enums;

namespace Aviss.Domain.Catalogo;

/// <summary>
/// Un evento estándar que se siembra al crear la base (RF-6; spec 005 RF-1 a RF-3). <c>Codigo</c> es también su
/// <c>ClaveEstandar</c> (identidad estable, aunque se renombre). <c>Orden</c> solo importa entre los principales.
/// <c>Pasos</c> son los pasos que pide el evento después de elegirlo (nombres de <see cref="PasoEvento"/> separados por
/// coma) y <c>DestinoClave</c> la clave del estado en que deja a la vaca, si no es él mismo (Preñada fecha previa → PRENADA).
/// </summary>
public sealed record EstadoInicial(
    string Codigo, string Nombre, string CodigoDairyComp,
    TipoEvento Tipo = TipoEvento.Estado, bool EsPrincipal = false, int Orden = 0,
    string? Pasos = null, string? DestinoClave = null);

/// <summary>
/// Los diez eventos estándar: los seis estados vistos en Dairy Comp más OK, Aborto, Revisar Nuevamente y Preñada fecha
/// previa (spec 005). SUPUESTO sin validar (prueba 0.2): el código de Dairy Comp es igual al nombre que trae el Excel. Los
/// de OK, Aborto, Revisar Nuevamente y Preñada fecha previa son PROVISORIOS (el nombre en mayúsculas) hasta que lleguen
/// los reales (pendiente de validar, constitución §4); se corrigen con una migración de datos, sin tocar la identidad.
/// El código interno es estable (mayúsculas, sin acentos): por eso PRENADA y no PREÑADA. PRENADA conserva su clave aunque
/// ahora se llame "Preñada fecha correcta": cambiar la clave obligaría a reescribir la identidad sin ganar nada.
/// El sembrador los procesa en este orden: PRENADA tiene que ir antes de PRENADA_FECHA_PREVIA, que apunta a ella.
/// </summary>
public static class EstadosIniciales
{
    /// <summary>El nombre nuevo de PREÑADA (RF-26).</summary>
    public const string NombrePrenadaFechaCorrecta = "Preñada fecha correcta";

    public static readonly IReadOnlyList<EstadoInicial> Todos = new[]
    {
        // Principales, en el orden en que se ven (RF-3).
        new EstadoInicial("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", TipoEvento.SoloEvento, true, 1),
        new EstadoInicial("OK", "OK", "OK", TipoEvento.SoloEvento, true, 2),
        new EstadoInicial("VACIA", "VACIA", "VACIA", TipoEvento.Estado, true, 3),
        new EstadoInicial("PRENADA", NombrePrenadaFechaCorrecta, "PREÑADA", TipoEvento.Estado, true, 4),
        new EstadoInicial("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", TipoEvento.Estado, true, 5,
            Pasos: "DiasPreniez", DestinoClave: "PRENADA"),
        new EstadoInicial("ABORTO", "Aborto", "ABORTO", TipoEvento.Estado, true, 6, Pasos: "DiasAborto,ResultadoAborto"),
        new EstadoInicial("NOINSEM", "NOINSEM", "NOINSEM", TipoEvento.Estado, true, 7),
        // De "Otro evento".
        new EstadoInicial("INSEMIN", "INSEMIN", "INSEMIN"),
        new EstadoInicial("SECA", "SECA", "SECA"),
        new EstadoInicial("FRESCA", "FRESCA", "FRESCA"),
    };
}
