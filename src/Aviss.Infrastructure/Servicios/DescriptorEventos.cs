using Aviss.Application.Campo;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Arma la descripción de un registro de campo con el catálogo completo (activo o no): el historial de la ficha, "Últimos movimientos"
/// y el aviso de repetido dicen lo mismo (spec 005, RF-25, RF-30, RF-31). El resultado del aborto no se guarda: se deriva acá.
/// </summary>
internal static class DescriptorEventos
{
    /// <param name="eventoId">El evento guardado en el registro (nulo en un tacto viejo o normal).</param>
    /// <param name="estadoFinalId">El estado en que quedó la vaca (solo los tactos).</param>
    /// <param name="esRechazo">El registro está en la tabla de rechazos (un Aborto con resultado Rechazo).</param>
    /// <param name="evento2Id">Spec 011: el segundo evento del registro; se agrega al texto con " + ".</param>
    /// <returns>Nulo si no hay evento (el Rechazo fijo, una marca) o si el Id no está en el catálogo.</returns>
    public static string? Describir(
        IReadOnlyDictionary<Guid, EstadoReproductivo> catalogo, Guid? eventoId, Guid? estadoFinalId,
        int? diasPreniez, int? diasAborto, bool esRechazo, Guid? evento2Id = null)
        => DescribirConResultado(catalogo, eventoId, estadoFinalId, diasPreniez, diasAborto, esRechazo, evento2Id).Texto;

    public static (string? Texto, ResultadoAborto? Resultado) DescribirConResultado(
        IReadOnlyDictionary<Guid, EstadoReproductivo> catalogo, Guid? eventoId, Guid? estadoFinalId,
        int? diasPreniez, int? diasAborto, bool esRechazo, Guid? evento2Id = null)
    {
        var id = eventoId ?? estadoFinalId;
        if (id is not Guid g || !catalogo.TryGetValue(g, out var evento)) return (null, null);

        ResultadoAborto? resultado = null;
        if (diasAborto is not null)
        {
            if (esRechazo) resultado = ResultadoAborto.Rechazo;
            else if (estadoFinalId is Guid f && catalogo.TryGetValue(f, out var final))
                resultado = ReglasResultadoAborto.DesdeClaveEstado(final.ClaveEstandar);
        }
        // Spec 011: el segundo evento se nombra con el catálogo completo (activo o no), como el primero.
        var nombre2 = evento2Id is Guid g2 && catalogo.TryGetValue(g2, out var segundo) ? segundo.Nombre : null;
        return (DescripcionEvento.Armar(evento.Nombre, diasPreniez, diasAborto, resultado, nombre2), resultado);
    }
}
