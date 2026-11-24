
using System.Text.Json;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Campo;

/// <summary>Un tacto de la vaca tal como lo necesita el historial de la ficha.</summary>
public sealed record TactoParaFicha(DateTime FechaHoraUtc, string? EstadoNombre, bool Anulado);

/// <summary>Un rechazo de la vaca tal como lo necesita la ficha (spec 004, RF-11).</summary>
/// <param name="EventoId">Spec 005: el solo evento del catálogo (OK, Aborto...); nulo = el Rechazo fijo.</param>
/// <param name="NombreEvento">El nombre de ese evento (aunque esté desactivado); nulo para el Rechazo fijo.</param>
public sealed record RechazoParaFicha(
    Guid Id, DateTime FechaHoraUtc, long OrdenInsercion, string? Observaciones, bool Anulado, Guid? EventoId = null, string? NombreEvento = null);

/// <summary>Lo que arma <see cref="ConstructorFicha"/>: los campos del Excel, el dato de inseminaciones, el historial, el CVET y el rechazo vigente.</summary>
public sealed record FichaArmada(
    IReadOnlyList<CampoFichaDto> Campos, string Inseminaciones, string UltimoToro, IReadOnlyList<EventoFichaDto> Historial,
    string Cvet = "",
    IReadOnlyList<CampoFichaDto>? Extras = null,
    RechazoVigenteDto? RechazoVigente = null);

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
        string? datosOriginalesJson, IEnumerable<TactoParaFicha> tactos, DateTime ahoraUtc, TimeZoneInfo zona,
        IEnumerable<RechazoParaFicha>? rechazos = null)
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

        // Los rechazos entran al historial con su motivo; los anulados quedan marcados (RF-11).
        var listaRechazos = (rechazos ?? []).ToList();
        foreach (var r in listaRechazos)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(r.FechaHoraUtc, zona);
            var nombre = r.NombreEvento ?? EventosCampo.NombreRechazo;
            var titulo = string.IsNullOrWhiteSpace(r.Observaciones) ? nombre : $"{nombre}: {r.Observaciones}";
            datados.Add(new EventoFichaDto(OrigenEvento.Aviss, titulo, local, null, (hoyLocal - local.Date).Days, r.Anulado));
        }

        // El rechazo vigente es el no anulado más reciente (fecha y, con empate, el guardado después).
        // Solo el Rechazo fijo (sin evento) es la "marca de rechazo": un OK o un Aborto no pintan la franja de rechazo.
        var vigente = listaRechazos.Where(r => !r.Anulado && r.EventoId is null)
            .OrderByDescending(r => r.FechaHoraUtc).ThenByDescending(r => r.OrdenInsercion).FirstOrDefault();
        var rechazoVigente = vigente is null ? null : new RechazoVigenteDto(vigente.Id, vigente.Observaciones, vigente.FechaHoraUtc);

        // Cronológico, del más viejo al más nuevo. OrderBy es estable: con la misma fecha queda primero el del Excel.
        var historial = datados.OrderBy(e => e.FechaLocal).Concat(ilegibles).ToList();

        return new FichaArmada(campos, Valor("VINS"), Valor("UTORO"), historial, Valor("CVET"), ColumnasExtra(originales), rechazoVigente);
    }

    // Columnas que la ficha ya muestra por su cuenta (o que no son un dato para mirar): no van entre las "extras". FCELO y RPRO sí van como
    // extras (spec 009, RF-15): son datos del Excel que el veterinario quiere ver, también en la ficha de una vaca con CVET 0.
    private static readonly HashSet<string> ColumnasPropias = new(StringComparer.OrdinalIgnoreCase)
    {
        "ID", "CVET", "LACT", "DEL", "FPART", "FAPAR", "COM", "VINS", "UTORO",
        // EID (spec 010): la ficha lo muestra una sola vez, con el valor guardado en el animal (no la celda original).
        "EID"
    };

    /// <summary>
    /// Las demás columnas que traiga el Excel (por ejemplo DCC, DDUC o LECHE), en el orden del archivo y solo las que tienen
    /// valor, para mostrarlas en la pestaña de datos sin tener que tocar el código cuando Dairy Comp exporta más columnas.
    /// </summary>
    private static List<CampoFichaDto> ColumnasExtra(Dictionary<string, string> originales)
        => originales
            .Where(c => !ColumnasPropias.Contains(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => new CampoFichaDto(c.Key, c.Value))
            .ToList();

    /// <summary>
    /// El CVET original de la vaca (por qué está en la lista de tacto), o vacío si el Excel no lo traía o no se puede leer.
    /// Nunca falla. Con <see cref="ReglasCvet"/> se decide si la vaca entra en tacto.
    /// </summary>
    public static string LeerCvet(string? datosOriginalesJson)
        => LeerOriginales(datosOriginalesJson).TryGetValue("CVET", out var cvet) ? cvet : "";

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
