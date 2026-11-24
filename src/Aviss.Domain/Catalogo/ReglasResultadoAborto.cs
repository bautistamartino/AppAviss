using Aviss.Domain.Enums;

namespace Aviss.Domain.Catalogo;

/// <summary>
/// El resultado de un aborto (spec 005, RF-28): a qué estado deja a la vaca y cómo se llama en pantalla. "Vacía OK" es solo
/// la etiqueta que se muestra para VACIA dentro de un aborto. Rechazo no tiene estado: no cambia el de la vaca (RF-30).
/// </summary>
public static class ReglasResultadoAborto
{
    /// <summary>Los cinco, en el orden en que se ofrecen.</summary>
    public static readonly IReadOnlyList<ResultadoAborto> Todos =
        [ResultadoAborto.VaciaOk, ResultadoAborto.Inseminada, ResultadoAborto.Seca, ResultadoAborto.Fresca, ResultadoAborto.Rechazo];

    /// <summary>Clave estándar del estado que deja el resultado; nula para Rechazo (no cambia el estado).</summary>
    public static string? ClaveEstado(ResultadoAborto r) => r switch
    {
        ResultadoAborto.VaciaOk => "VACIA",
        ResultadoAborto.Inseminada => "INSEMIN",
        ResultadoAborto.Seca => "SECA",
        ResultadoAborto.Fresca => "FRESCA",
        _ => null
    };

    public static string Etiqueta(ResultadoAborto r) => r switch
    {
        ResultadoAborto.VaciaOk => "Vacía OK",
        ResultadoAborto.Inseminada => "Inseminada",
        ResultadoAborto.Seca => "Seca",
        ResultadoAborto.Fresca => "Fresca",
        _ => "Rechazo"
    };

    /// <summary>El resultado que corresponde a un estado final (clave estándar), o nulo si no es uno de los cuatro.</summary>
    public static ResultadoAborto? DesdeClaveEstado(string? clave) => clave switch
    {
        "VACIA" => ResultadoAborto.VaciaOk,
        "INSEMIN" => ResultadoAborto.Inseminada,
        "SECA" => ResultadoAborto.Seca,
        "FRESCA" => ResultadoAborto.Fresca,
        _ => null
    };
}
