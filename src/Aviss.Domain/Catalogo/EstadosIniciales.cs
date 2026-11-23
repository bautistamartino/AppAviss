using Aviss.Domain.Enums;

namespace Aviss.Domain.Catalogo;

/// <summary>
/// Un evento estándar que se siembra al crear la base (RF-6; spec 005 RF-1 a RF-3). <c>Codigo</c> es también su
/// <c>ClaveEstandar</c> (identidad estable, aunque se renombre). <c>Orden</c> solo importa entre los principales.
/// </summary>
public sealed record EstadoInicial(
    string Codigo, string Nombre, string CodigoDairyComp,
    TipoEvento Tipo = TipoEvento.Estado, bool EsPrincipal = false, int Orden = 0);

/// <summary>
/// Los nueve eventos estándar: los seis estados vistos en Dairy Comp más OK, Aborto y Revisar Nuevamente (spec 005).
/// SUPUESTO sin validar (prueba 0.2): el código de Dairy Comp es igual al nombre que trae el Excel. Los de OK, Aborto y
/// Revisar Nuevamente son PROVISORIOS (el nombre en mayúsculas) hasta que lleguen los reales (pendiente de validar,
/// constitución §4); se corrigen con una migración de datos, sin tocar la identidad. Si resultan distintos, se corrigen
/// en el catálogo sin tocar el programa.
/// El código interno es estable (mayúsculas, sin acentos): por eso PRENADA y no PREÑADA.
/// </summary>
public static class EstadosIniciales
{
    public static readonly IReadOnlyList<EstadoInicial> Todos = new[]
    {
        // Principales, en el orden en que se ven (RF-3).
        new EstadoInicial("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", TipoEvento.SoloEvento, true, 1),
        new EstadoInicial("OK", "OK", "OK", TipoEvento.SoloEvento, true, 2),
        new EstadoInicial("VACIA", "VACIA", "VACIA", TipoEvento.Estado, true, 3),
        new EstadoInicial("PRENADA", "PREÑADA", "PREÑADA", TipoEvento.Estado, true, 4),
        new EstadoInicial("ABORTO", "Aborto", "ABORTO", TipoEvento.SoloEvento, true, 5),
        new EstadoInicial("NOINSEM", "NOINSEM", "NOINSEM", TipoEvento.Estado, true, 6),
        // De "Otro evento".
        new EstadoInicial("INSEMIN", "INSEMIN", "INSEMIN"),
        new EstadoInicial("SECA", "SECA", "SECA"),
        new EstadoInicial("FRESCA", "FRESCA", "FRESCA"),
    };
}
