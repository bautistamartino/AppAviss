namespace Aviss.Domain.Reglas;

/// <summary>Un rango de días permitido, con los dos extremos incluidos.</summary>
public readonly record struct RangoDias(int Minimo, int Maximo, string Descripcion)
{
    public bool Contiene(int dias) => dias >= Minimo && dias <= Maximo;
}

/// <summary>
/// Única fuente de los rangos de días de los eventos con pasos (spec 005, RF-23 y RF-28). Son PROVISORIOS (pregunta P9):
/// si cambian, se cambian acá y en los CHECK de la migración "EventosConPasos" (una migración chica de CHECK).
/// </summary>
public static class RangosDias
{
    /// <summary>Días de preñez de "Preñada fecha previa": 1 a 400.</summary>
    public static readonly RangoDias Preniez = new(1, 400, "Los días de preñez");

    /// <summary>Días de aborto: 0 a 400 (0 es válido: abortó hoy).</summary>
    public static readonly RangoDias Aborto = new(0, 400, "Los días de aborto");
}
