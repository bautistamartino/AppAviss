using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>
/// Preferencias de la tablet con el almacén de MAUI (SharedPreferences en Android). Guardan el último veterinario
/// activo fuera de la base, igual que el identificador del dispositivo: no cambia si se restaura o recrea la base (RF-1).
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
}
