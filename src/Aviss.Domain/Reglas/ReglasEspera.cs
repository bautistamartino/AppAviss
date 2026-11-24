namespace Aviss.Domain.Reglas;

/// <summary>Reglas puras de la espera (spec 004).</summary>
public static class ReglasEspera
{
    /// <summary>
    /// RF-1: se puede poner en espera una vaca que todavía no está en espera. Desde la spec 009 (RF-15) ya no importa el CVET: una vaca
    /// de CVET 0 abierta con "Ver ficha" también puede quedar en espera, porque en el campo se la puede decidir tactar igual.
    /// </summary>
    public static bool PuedePonerseEnEspera(bool yaEnEspera) => !yaEnEspera;

    /// <summary>
    /// RF-9: la marca de pasada de una vaca en espera no se puede deshacer; la espera solo se sale resolviéndola o salteándola.
    /// </summary>
    public static bool PuedeDesmarcarse(bool enEspera) => !enEspera;
}
