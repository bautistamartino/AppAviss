using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>Mantiene la pantalla encendida mientras se trabaja (RF-3), con MAUI Essentials.</summary>
public sealed class PantallaMaui : IPantalla
{
    public void MantenerEncendida(bool encendida)
    {
        // DeviceDisplay solo se toca desde el hilo de la interfaz; la lógica puede llamar desde cualquier hilo.
        MainThread.BeginInvokeOnMainThread(() => DeviceDisplay.Current.KeepScreenOn = encendida);
    }
}
