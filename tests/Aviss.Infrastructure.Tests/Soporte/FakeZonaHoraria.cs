using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Zona horaria de prueba. Por defecto UTC: así "hoy" es el día UTC del <see cref="FakeReloj"/> y los tests de
/// fechas son simples. Un test que necesita otra zona la cambia con <see cref="Fijar"/>.
/// </summary>
public sealed class FakeZonaHoraria : IZonaHoraria
{
    public TimeZoneInfo Zona { get; private set; } = TimeZoneInfo.Utc;

    public void Fijar(TimeZoneInfo zona) => Zona = zona;

    /// <summary>Una zona de desfase fijo respecto de UTC (sin horario de verano).</summary>
    public static TimeZoneInfo ConDesfase(double horas)
        => TimeZoneInfo.CreateCustomTimeZone("fake" + horas, TimeSpan.FromHours(horas), "fake", "fake");
}
