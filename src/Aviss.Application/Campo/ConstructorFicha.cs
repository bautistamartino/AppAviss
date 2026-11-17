
using System.Text.Json;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Campo;

/// <summary>Un tacto de la vaca tal como lo necesita el historial de la ficha.</summary>
public sealed record TactoParaFicha(DateTime FechaHoraUtc, string? EstadoNombre, bool Anulado);

/// <summary>Lo que arma <see cref="ConstructorFicha"/>: los campos del Excel, el dato de inseminaciones y el historial.</summary>
public sealed record FichaArmada(
    IReadOnlyList<CampoFichaDto> Campos, string Inseminaciones, string UltimoToro, IReadOnlyList<EventoFichaDto> Historial);

/// <summary>
/// Arma lo que la ficha muestra a partir de las columnas originales del Excel (JSON) y los tactos de Aviss (spec 003,
/// RF-13 y RF-14). Es una función pura: no toca la base y NUNCA falla por datos malos (JSON roto o ausente, fechas
/// que no se entienden): lo que no se puede leer queda vacío o se muestra como texto.
/// </summary>
public static class ConstructorFicha
{
    // Columnas que se muestran tal cual, en este orden (RF-13). VINS y UTORO van aparte y FCELO solo en el historial.
    private static readonly string[] ColumnasMostradas = ["LACT", "DEL", "FPART", "FAPAR", "COM"];

    // Fechas del Excel que entran al historial, con el nombre con que se muestran (RF-14).
    private static readonly (string Columna, string Titulo)[] FechasDelExcel =
    [
        ("FPART", "Último parto"),
        ("FCELO", "Celo"),
        ("FAPAR", "Fecha probable de parto")
    ];

    public static FichaArmada Armar(
        string? datosOriginalesJson, IEnumerable<TactoParaFicha> tactos, DateTime ahoraUtc, TimeZoneInfo zona)
    {
        var originales = LeerOriginales(datosOriginalesJson);
        string Valor(string columna) => originales.TryGetValue(columna, out var v) ? v : "";

        var campos = ColumnasMostradas.Select(c => new CampoFichaDto(c, Valor(c))).ToList();

        // "Hoy" y los días transcurridos se cuentan en el día local (la misma regla que el avance del día).
        var hoyLocal = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, zona).Date;

        var datados = new List<EventoFichaDto>();
        var ilegibles = new List<EventoFichaDto>();

        foreach (var (columna, titulo) in FechasDelExcel)
        {
            var texto = Valor(columna);
            if (texto.Length == 0) continue;
            if (FechaExcel.TryParsear(texto, out var fecha))
            {
                var local = fecha.ToDateTime(TimeOnly.MinValue);
                datados.Add(new EventoFichaDto(OrigenEvento.Excel, titulo, local, null, (hoyLocal - local.Date).Days, false));
            }
            else
            {
                // No se entiende: va como texto, sin fecha para ordenar, y la ficha sigue funcionando.
                ilegibles.Add(new EventoFichaDto(OrigenEvento.Excel, titulo, null, texto, null, false));
            }
        }

        foreach (var t in tactos)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(t.FechaHoraUtc, zona);
            var titulo = string.IsNullOrWhiteSpace(t.EstadoNombre) ? "Tacto" : $"Tacto: {t.EstadoNombre}";
            datados.Add(new EventoFichaDto(OrigenEvento.Aviss, titulo, local, null, (hoyLocal - local.Date).Days, t.Anulado));
        }

        // Cronológico, del más viejo al más nuevo. OrderBy es estable: con la misma fecha queda primero el del Excel.
        var historial = datados.OrderBy(e => e.FechaLocal).Concat(ilegibles).ToList();

        return new FichaArmada(campos, Valor("VINS"), Valor("UTORO"), historial);
    }

    /// <summary>
    /// Lee el objeto JSON { "columna": "valor" } a un diccionario sin distinguir mayúsculas. Si no es un objeto o está
    /// roto, devuelve uno vacío. Los valores que no son texto (un número) se pasan a texto.
    /// </summary>
    private static Dictionary<string, string> LeerOriginales(string? json)
    {
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return resultado;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return resultado;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var texto = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    _ => null
                };
                if (texto is not null) resultado[p.Name] = texto.Trim();
            }
        }
        catch (JsonException)
        {
            // Columnas originales mal formadas: se ignoran (RF-13).
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        return resultado;
    }
}
