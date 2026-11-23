using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>
/// Preferencias de la tablet con el almacén de MAUI (SharedPreferences en Android). Guardan, por establecimiento, si se lleva
/// el control por manga (spec 003, RF-43). Desde la spec 006 ya no se recuerda ningún veterinario: la clave que una versión
/// anterior dejó guardada se borra con <see cref="EliminarClavesObsoletas"/>.
/// </summary>
public sealed class PreferenciasMaui : IPreferenciasMangas, ILimpiezaPreferencias
{
    // Clave del veterinario recordado de versiones anteriores: solo se conserva el nombre para poder borrarla.
    private const string ClaveVeterinarioObsoleta = "veterinario_recordado";

    // Una clave por establecimiento: cada tambo trabaja a su manera. Nulo si nunca se eligió.
    private static string ClaveControlPorManga(Guid establecimientoId) => $"control_por_manga_{establecimientoId}";

    public bool? ObtenerControlPorManga(Guid establecimientoId)
    {
        var clave = ClaveControlPorManga(establecimientoId);
        return Preferences.Default.ContainsKey(clave) ? Preferences.Default.Get(clave, IPreferenciasMangas.ControlPorMangaPorDefecto) : null;
    }

    public void GuardarControlPorManga(Guid establecimientoId, bool valor)
        => Preferences.Default.Set(ClaveControlPorManga(establecimientoId), valor);

    /// <summary>Quita el veterinario recordado de una versión anterior (RF-21). Nunca toca las claves de mangas.</summary>
    public void EliminarClavesObsoletas() => Preferences.Default.Remove(ClaveVeterinarioObsoleta);
}
