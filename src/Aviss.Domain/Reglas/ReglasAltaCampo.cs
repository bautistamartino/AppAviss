namespace Aviss.Domain.Reglas;

/// <summary>
/// Qué identificador electrónico guardar en un alta en campo. <paramref name="OmitidoPorFormato"/> es true cuando
/// el código leído difería de la caravana pero no sirve como identificador (por ejemplo, es demasiado largo):
/// el alta sigue, sin identificador, y se avisa.
/// </summary>
public readonly record struct IdElectronicoDeAlta(string? IdElectronico, bool OmitidoPorFormato);

/// <summary>Reglas puras del alta en campo (spec 003, RF-25).</summary>
public static class ReglasAltaCampo
{
    /// <summary>
    /// Si el veterinario tipea una caravana distinta del código leído, el número tipeado es la caravana y el código
    /// leído se guarda como identificador electrónico; si son iguales (o no hay código leído, por ejemplo en el alta
    /// desde "Buscar a mano"), no se guarda identificador. La comparación no distingue mayúsculas, igual que la
    /// unicidad de la caravana.
    /// </summary>
    public static IdElectronicoDeAlta IdElectronicoDeLectura(string? caravanaTipeada, string? codigoLeido)
    {
        if (string.IsNullOrWhiteSpace(codigoLeido))
            return new(null, false);

        var tipeada = (caravanaTipeada ?? "").Trim();
        if (Caravana.CalcularClave(tipeada) == Caravana.CalcularClave(codigoLeido.Trim()))
            return new(null, false);

        // Difiere: el código leído pasa a ser el identificador, si cumple las reglas del identificador (RF-4b).
        return IdElectronico.TryCrear(codigoLeido, out var id, out _)
            ? new(id.Valor, false)
            : new(null, true);
    }
}
