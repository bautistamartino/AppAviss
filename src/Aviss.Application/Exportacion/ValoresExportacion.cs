namespace Aviss.Application.Exportacion;

/// <summary>
/// Los siete textos que una vaca lleva en las columnas nuevas (spec 007). Vacío es la cadena vacía, sin ningún otro texto
/// (RF-11): lo que no corresponde a un evento (o a una vaca que no pasó) queda en blanco. No trae caravana: el diccionario
/// que lo contiene es el que sabe de qué vaca es.
/// </summary>
public sealed record ValoresExportacion(string Fecha, string Hora, string Evento, string Evento2, string DiasPreniez, string DiasAborto, string ResultadoAborto)
{
    /// <summary>Todas las columnas nuevas en blanco: vaca que no pasó, fila excluida o fila Total.</summary>
    public static readonly ValoresExportacion Vacios = new("", "", "", "", "", "", "");

    /// <summary>True si no lleva ningún valor.</summary>
    public bool EsVacio => EnOrden().All(string.IsNullOrEmpty);

    /// <summary>Los valores en el orden de <see cref="ColumnasExportacion.Todas"/>.</summary>
    public IReadOnlyList<string> EnOrden() => [Fecha, Hora, Evento, Evento2, DiasPreniez, DiasAborto, ResultadoAborto];
}
