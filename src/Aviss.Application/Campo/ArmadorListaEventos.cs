using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Campo;

/// <summary>
/// Arma la lista de eventos de la ficha en dos niveles (spec 005, RF-6, RF-7, RF-11, RF-13). Es pura: recibe el catálogo y devuelve
/// los principales activos en su <c>Orden</c> y los de "Otro evento" (el RECHAZO fijo primero y después los activos no principales por
/// nombre, sin FRESCA, INSEMIN ni SECA: ver <see cref="ClavesOcultasEnOtroEvento"/>). Los desactivados no aparecen; renombrar un principal no lo mueve (el orden es el de la columna, no el del nombre) y
/// desactivarlo no lo reemplaza con otro.
/// </summary>
public static class ArmadorListaEventos
{
    /// <summary>El "Rechazo" fijo de la aplicación (spec 004): no es una fila del catálogo y siempre abre "Otro evento".</summary>
    public static readonly EventoCampoDto EventoRechazo = new(EventosCampo.Rechazo, EventosCampo.NombreRechazo, CambiaEstado: false, EsPrincipal: false);

    /// <summary>
    /// Claves estándar que NO se muestran en "Otro evento" (decisión del 2026-11-24) pero siguen ACTIVAS en el catálogo: el Aborto las
    /// ofrece como resultados, la importación (RPRO) las entiende y el historial muestra su nombre. Se excluye por clave (la
    /// identidad estable), así que renombrarlas no las hace reaparecer, y no hace falta ningún dato nuevo ni migración.
    /// </summary>
    public static readonly IReadOnlySet<string> ClavesOcultasEnOtroEvento = new HashSet<string> { "FRESCA", "INSEMIN", "SECA" };

    public static ListaEventosDto Armar(IEnumerable<EstadoReproductivo> catalogo)
    {
        var activos = catalogo.Where(e => e.Activo).ToList();

        var principales = activos.Where(e => e.EsPrincipal)
            .OrderBy(e => e.Orden).ThenBy(e => e.CreadoEnUtc)
            .Select(Dto)
            .ToList();

        var otros = new List<EventoCampoDto> { EventoRechazo };
        otros.AddRange(activos.Where(e => !e.EsPrincipal && !(e.ClaveEstandar is { } clave && ClavesOcultasEnOtroEvento.Contains(clave)))
            .OrderBy(e => OrdenAlfabetico.Clave(e.Nombre), StringComparer.Ordinal).ThenBy(e => e.CreadoEnUtc)
            .Select(Dto));

        // Los resultados del aborto se calculan sobre todo el catálogo (también si el estado es de "Otro evento").
        var resultados = new List<ResultadoAborto>();
        foreach (var r in ReglasResultadoAborto.Todos)
        {
            var clave = ReglasResultadoAborto.ClaveEstado(r);
            if (clave is null || activos.Any(e => e.ClaveEstandar == clave)) resultados.Add(r);
        }

        return new ListaEventosDto(principales, otros, resultados);
    }

    private static EventoCampoDto Dto(EstadoReproductivo e)
        => new(e.Id, e.Nombre, e.Tipo == TipoEvento.Estado, e.EsPrincipal, e.ListarPasos(), e.EstadoDestinoId, e.ClaveEstandar);
}
