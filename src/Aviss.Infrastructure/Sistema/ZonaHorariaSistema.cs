using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Sistema;

/// <summary>
/// La zona horaria real del dispositivo. .NET guarda <c>TimeZoneInfo.Local</c> en una caché: si el usuario cambia la zona
/// con la app abierta, esa caché sigue con la anterior. Por eso se limpia antes de leer: un cambio de zona se nota en el
/// próximo cálculo del día de hoy sin reiniciar la app (RF-30). Se consulta pocas veces por operación; es barato.
/// </summary>
public sealed class ZonaHorariaSistema : IZonaHoraria
{
    public TimeZoneInfo Zona
    {
        get
        {
            TimeZoneInfo.ClearCachedData();
            return TimeZoneInfo.Local;
        }
    }
}
