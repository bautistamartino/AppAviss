using System.Globalization;

namespace Aviss.Domain.Reglas;

/// <summary>
/// Cómo se escriben la fecha y la hora del registro en las columnas nuevas del archivo de vuelta (spec 007, RF-6 y RF-7):
/// la fecha como <c>dd/MM/yy</c> y la hora como <c>HH:mm</c> (15:30 es <c>15:30</c>, 09:05 es <c>09:05</c>), igual que el archivo de Dairy Comp (cambiado 2026-11-25), las dos en la
/// hora LOCAL de la zona dada (la de la tablet). Las fechas se guardan en UTC y se convierten recién al exportar. Siempre con
/// cultura invariante: el formato no debe depender del idioma del dispositivo.
/// </summary>
public static class FormatoColumnasExportacion
{
    public static string Fecha(DateTime utc, TimeZoneInfo zona)
        => ALocal(utc, zona).ToString("dd/MM/yy", CultureInfo.InvariantCulture);

    public static string Hora(DateTime utc, TimeZoneInfo zona)
        => ALocal(utc, zona).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DateTime ALocal(DateTime utc, TimeZoneInfo zona)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha debe estar en UTC.", nameof(utc));
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zona);
    }
}
