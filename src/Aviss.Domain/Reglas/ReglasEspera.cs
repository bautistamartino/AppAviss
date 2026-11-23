namespace Aviss.Domain.Reglas;

/// <summary>Reglas puras de la espera (spec 004).</summary>
public static class ReglasEspera
{
    /// <summary>
    /// RF-1: se puede poner en espera una vaca que entra en tacto (CVET distinto de 0; ver <see cref="ReglasCvet"/>) y
    /// que todavía no está en espera.
    /// </summary>
    public static bool PuedePonerseEnEspera(bool entraEnTacto, bool yaEnEspera) => entraEnTacto && !yaEnEspera;

    /// <summary>
    /// RF-9: la marca de pasada de una vaca en espera no se puede deshacer; la espera solo se sale resolviéndola o salteándola.
    /// </summary>
    public static bool PuedeDesmarcarse(bool enEspera) => !enEspera;
}
