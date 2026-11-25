namespace Aviss.Application.Exportacion;

/// <summary>Cómo se escribe el valor de una columna nueva: texto, o un número entero (días).</summary>
public enum TipoColumnaExportacion { Texto, Entero }

/// <summary>
/// Una columna de la exportación. Por defecto se agrega a la derecha de las originales (y si el original ya la trae, se rechaza).
/// Con <paramref name="ReemplazaSiExiste"/> (SCDAT y SCTIM) es una columna que Dairy Comp SIEMPRE exporta: si el original la trae
/// se reemplaza su valor solo en las vacas con tacto; si no la trae se crea a la derecha y se avisa.
/// </summary>
public sealed record ColumnaExportacion(string Nombre, TipoColumnaExportacion Tipo, bool ReemplazaSiExiste = false);

/// <summary>
/// LAS columnas nuevas del archivo de vuelta (spec 007, RF-6 a RF-10), con su nombre, su orden y su tipo. Es el ÚNICO lugar
/// donde viven los nombres: el exportador, el validador, las pantallas y los tests los leen de acá, así que cambiar uno es
/// cambiar una línea (pregunta abierta 1 de la spec).
/// <list type="bullet">
/// <item><c>SCDAT</c> y <c>SCTIM</c>: fijos (decisión del responsable). Dairy Comp SIEMPRE las exporta y marcan el último tacto:
/// si el original las trae, REEMPLAZAN su valor en las vacas con tacto del día (las demás filas quedan como venían); si no las
/// trae, se crean a la derecha y la exportación avisa (modificado 2026-11-24).</item>
/// <item><c>Evento1</c>: PROVISORIO. El nombre real que usa el Daily de Dairy Comp se consigue después.</item>
/// <item><c>Evento2</c> (spec 011): PROVISORIO. El nombre del segundo evento del tacto, siempre presente, justo a la derecha de
/// <c>Evento1</c>; vacío si la vaca no tiene Evento 2. Comparte SCDAT y SCTIM con el Evento 1 y no lleva observaciones.</item>
/// <item><c>DIASPRE</c>, <c>DIASABO</c> y <c>RESABO</c>: PROVISORIOS, sin confirmar.</item>
/// </list>
/// </summary>
public static class ColumnasExportacion
{
    public static readonly ColumnaExportacion Fecha = new("SCDAT", TipoColumnaExportacion.Texto, ReemplazaSiExiste: true);
    public static readonly ColumnaExportacion Hora = new("SCTIM", TipoColumnaExportacion.Texto, ReemplazaSiExiste: true);
    public static readonly ColumnaExportacion Evento = new("Evento1", TipoColumnaExportacion.Texto);
    public static readonly ColumnaExportacion Evento2 = new("Evento2", TipoColumnaExportacion.Texto);
    public static readonly ColumnaExportacion DiasPreniez = new("DIASPRE", TipoColumnaExportacion.Entero);
    public static readonly ColumnaExportacion DiasAborto = new("DIASABO", TipoColumnaExportacion.Entero);
    public static readonly ColumnaExportacion ResultadoAborto = new("RESABO", TipoColumnaExportacion.Texto);

    /// <summary>Las seis (las que se agregan o se reemplazan), en el orden en que van en el archivo (el mismo de <see cref="ValoresExportacion.EnOrden"/>).</summary>
    public static readonly IReadOnlyList<ColumnaExportacion> Todas = [Fecha, Hora, Evento, Evento2, DiasPreniez, DiasAborto, ResultadoAborto];
}
