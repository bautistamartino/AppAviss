using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aviss.Infrastructure.Persistencia;

/// <summary>
/// Guarda toda fecha como texto de ancho fijo <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c> (UTC). Ese formato ordena bien como
/// texto, se lee a mano al inspeccionar la base y no tiene ambigüedad de zona horaria (RF-17).
/// Escribir una fecha que no sea UTC lanza una excepción; leer siempre devuelve <c>Kind = Utc</c>.
/// </summary>
public sealed class ConvertidorFechaUtc : ValueConverter<DateTime, string>
{
    public const string Formato = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public ConvertidorFechaUtc() : base(v => ATexto(v), v => ADateTime(v)) { }

    public static string ATexto(DateTime fecha)
    {
        if (fecha.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Las fechas se guardan siempre en UTC (Kind = Utc).", nameof(fecha));
        return fecha.ToString(Formato, CultureInfo.InvariantCulture);
    }

    public static DateTime ADateTime(string texto)
        => DateTime.ParseExact(texto, Formato, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
