using Aviss.Domain.Reglas;

namespace Aviss.Application.Exportacion;

/// <summary>El día de la jornada (nulo si no hay registros) y los registros que caen en ese día local.</summary>
public sealed record JornadaDeTactoResultado(DateOnly? Dia, IReadOnlyList<RegistroExportable> Registros);

/// <summary>
/// La jornada de tacto de un rodeo (spec 007, RF-12 y RF-22): el DÍA LOCAL (zona de la tablet) del registro vigente más reciente del
/// rodeo. Solo los registros de ese día se exportan; los de días anteriores no. Se toma del último registro y no del día en que se
/// exporta, para que exportar pasada la medianoche no deje el archivo vacío. Reutiliza <see cref="DiaLocal"/>, que ya maneja los días de
/// 23 y 25 horas del cambio de horario.
/// </summary>
public static class JornadaDeTacto
{
    public static JornadaDeTactoResultado Filtrar(IReadOnlyCollection<RegistroExportable> registros, TimeZoneInfo zona)
    {
        if (registros.Count == 0) return new JornadaDeTactoResultado(null, []);

        var masReciente = registros.Max(r => r.FechaHoraUtc);
        var rango = DiaLocal.RangoUtcDeHoy(masReciente, zona);
        var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(masReciente, zona));
        return new JornadaDeTactoResultado(dia, registros.Where(r => rango.Contiene(r.FechaHoraUtc)).ToList());
    }
}
