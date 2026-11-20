using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>
/// Preferencias de la tablet con el almacén de MAUI (SharedPreferences en Android). Guardan el último veterinario
/// activo fuera de la base, igual que el identificador del dispositivo: no cambia si se restaura o recrea la base (RF-1).
/// También guardan, por veterinario, si lleva el control por manga.
/// </summary>
public sealed class PreferenciasMaui : IPreferenciasDispositivo
{
    private const string ClaveVeterinario = "veterinario_recordado";

    public Guid? ObtenerVeterinarioRecordado()
    {
        var texto = Preferences.Default.Get(ClaveVeterinario, "");
        return Guid.TryParse(texto, out var id) ? id : null;
    }

    public void GuardarVeterinarioRecordado(Guid veterinarioId)
        => Preferences.Default.Set(ClaveVeterinario, veterinarioId.ToString());

    // Una clave por veterinario: cada uno tiene su forma de trabajar. Nulo si nunca eligió.
    private static string ClaveControlPorManga(Guid veterinarioId) => $"control_por_manga_{veterinarioId}";

    public bool? ObtenerControlPorManga(Guid veterinarioId)
    {
        var clave = ClaveControlPorManga(veterinarioId);
        return Preferences.Default.ContainsKey(clave) ? Preferences.Default.Get(clave, IPreferenciasDispositivo.ControlPorMangaPorDefecto) : null;
    }

    public void GuardarControlPorManga(Guid veterinarioId, bool valor)
        => Preferences.Default.Set(ClaveControlPorManga(veterinarioId), valor);
}
