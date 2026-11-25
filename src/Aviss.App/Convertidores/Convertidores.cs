using System.Globalization;
using Aviss.Application.Campo;

namespace Aviss.App.Convertidores;

// Convertidores de presentación de la pantalla de trabajo (spec 003, T4). Solo dan formato a lo que se ve (textos y
// colores): ninguna regla del negocio vive acá, las reglas están en Domain/Application y en el ViewModel.

/// <summary>Da vuelta un booleano (para mostrar algo cuando otra condición es falsa).</summary>
public sealed class InvertirBooleanoConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Un color en texto hexadecimal ("#1B5E20") a <see cref="Color"/>: el chip y el cartel del bastón (spec 008) traen el color del ViewModel.</summary>
public sealed class HexAColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string hex && Color.TryParse(hex, out var color) ? color : Colors.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Línea de fecha de un evento del historial de la ficha: "05/11/2026 · hace 12 días". Si la fecha del Excel no se pudo
/// interpretar se muestra el texto tal cual (RF-14). Los eventos anulados se marcan.
/// </summary>
public sealed class EventoFichaFechaConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EventoFichaDto e) return "";

        string texto;
        if (e.FechaLocal is { } fecha)
        {
            // Los eventos de Aviss traen hora; los del Excel son fechas sin hora (medianoche): solo se muestra la hora si existe.
            texto = fecha.TimeOfDay == TimeSpan.Zero ? fecha.ToString("dd/MM/yyyy") : fecha.ToString("dd/MM/yyyy HH:mm");
            if (e.DiasTranscurridos is { } dias) texto += " · " + Dias(dias);
        }
        else
        {
            texto = string.IsNullOrWhiteSpace(e.TextoFecha) ? "sin fecha" : e.TextoFecha + " (fecha no legible)";
        }

        return e.Anulado ? texto + " · anulado" : texto;
    }

    // "hoy", "hace 1 día", "hace 12 días"; las fechas futuras (por ejemplo la fecha probable de parto) dicen "en N días".
    private static string Dias(int dias) => dias switch
    {
        0 => "hoy",
        1 => "hace 1 día",
        > 1 => $"hace {dias} días",
        -1 => "en 1 día",
        _ => $"en {-dias} días"
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Una fecha guardada en UTC, mostrada en hora local ("17/11/2026 14:35"). Sin fecha: "sin tactos".</summary>
public sealed class FechaUtcALocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc) return "sin tactos";
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Resalta el botón del estado actual de la vaca (RF-13): ámbar con texto negro; los demás, el color normal.
/// Recibe el id del estado del botón y el id del estado de la ficha. <c>parameter</c> = "fondo" o "texto".
/// </summary>
public sealed class EstadoActualConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var esActual = values.Length >= 2 && values[0] is Guid id && values[1] is Guid actual && id == actual;
        var fondo = parameter as string != "texto";
        if (fondo)
            return esActual ? Color.FromArgb("#FFC107") : Color.FromArgb("#512BD4");
        return esActual ? Colors.Black : Colors.White;
    }

    public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Texto de cada resultado del aborto ("Vacía OK", "Inseminada", ...) tomado de las reglas del dominio. Resalta el elegido con el parámetro "fondo"/"texto".</summary>
public sealed class ResultadoAbortoConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not Aviss.Domain.Enums.ResultadoAborto r) return "";
        var elegido = values.Length > 1 && values[1] is Aviss.Domain.Enums.ResultadoAborto e && e == r;
        return parameter as string switch
        {
            "fondo" => elegido ? Color.FromArgb("#FFC107") : Color.FromArgb("#512BD4"),
            "texto" => elegido ? Colors.Black : Colors.White,
            _ => Aviss.Domain.Catalogo.ReglasResultadoAborto.Etiqueta(r)
        };
    }

    public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
