namespace Aviss.Domain.Reglas;

/// <summary>Un tramo de tiempo en UTC: incluye <see cref="Desde"/> y excluye <see cref="Hasta"/>.</summary>
public readonly record struct RangoUtc(DateTime Desde, DateTime Hasta)
{
    public bool Contiene(DateTime utc) => utc >= Desde && utc < Hasta;
}

/// <summary>
/// "Hoy" es el día local según la zona horaria del dispositivo, calculado en cada consulta y sin guardar nada
/// (spec 003, RF-10 y RF-30). Las fechas se guardan en UTC, así que acá se convierte el día local a un rango UTC
/// <c>[desde, hasta)</c> que después se usa para filtrar. Maneja los días de 23 y 25 horas del cambio de horario y
/// las zonas donde la medianoche no existe (el reloj salta de 23:59 a 01:00) o se repite.
/// </summary>
public static class DiaLocal
{
    /// <summary>El rango UTC del día local en el que cae <paramref name="ahoraUtc"/>.</summary>
    public static RangoUtc RangoUtcDeHoy(DateTime ahoraUtc, TimeZoneInfo zona)
    {
        if (ahoraUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La fecha debe estar en UTC.", nameof(ahoraUtc));
        var hoyLocal = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, zona).Date;
        return new RangoUtc(InicioDelDiaUtc(hoyLocal, zona), InicioDelDiaUtc(hoyLocal.AddDays(1), zona));
    }

    /// <summary>True si <paramref name="fechaUtc"/> es de un día local anterior al de <paramref name="ahoraUtc"/>.</summary>
    public static bool EsDeDiaAnterior(DateTime fechaUtc, DateTime ahoraUtc, TimeZoneInfo zona)
        => fechaUtc < RangoUtcDeHoy(ahoraUtc, zona).Desde;

    /// <summary>True si las dos fechas caen en el mismo día local.</summary>
    public static bool EsDelMismoDia(DateTime fechaUtc, DateTime ahoraUtc, TimeZoneInfo zona)
        => RangoUtcDeHoy(ahoraUtc, zona).Contiene(fechaUtc);

    /// <summary>
    /// El instante UTC en que empieza el día local <paramref name="dia"/> (a las 00:00 locales, con la fecha sin hora).
    /// Si esa medianoche no existe o es ambigua, no se puede convertir "a ciegas":
    /// </summary>
    private static DateTime InicioDelDiaUtc(DateTime dia, TimeZoneInfo zona)
    {
        var medianoche = DateTime.SpecifyKind(dia, DateTimeKind.Unspecified);

        // Medianoche inexistente (el reloj adelanta una hora justo a las 00:00): el día empieza en el instante del
        // salto, que es la medianoche vista con el desfase que regía justo antes.
        if (zona.IsInvalidTime(medianoche))
        {
            var desfaseAntes = zona.GetUtcOffset(medianoche.AddHours(-1));
            return DateTime.SpecifyKind(medianoche - desfaseAntes, DateTimeKind.Utc);
        }

        // Medianoche repetida (el reloj atrasa de 01:00 a 00:00): ocurre dos veces y el día empieza en la primera,
        // la de mayor desfase (la hora de verano). ConvertTimeToUtc tomaría la segunda y el día perdería una hora.
        if (zona.IsAmbiguousTime(medianoche))
        {
            var mayorDesfase = zona.GetAmbiguousTimeOffsets(medianoche).Max();
            return DateTime.SpecifyKind(medianoche - mayorDesfase, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(medianoche, zona);
    }
}
