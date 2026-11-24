namespace Aviss.Application.Exportacion;

/// <summary>Cómo se escribe el valor de una columna nueva: texto, o un número entero (días).</summary>
public enum TipoColumnaExportacion { Texto, Entero }

/// <summary>Una columna que la exportación agrega a la derecha de las originales.</summary>
public sealed record ColumnaExportacion(string Nombre, TipoColumnaExportacion Tipo);

/// <summary>
/// LAS columnas nuevas del archivo de vuelta (spec 007, RF-6 a RF-10), con su nombre, su orden y su tipo. Es el ÚNICO lugar
/// donde viven los nombres: el exportador, el validador, las pantallas y los tests los leen de acá, así que cambiar uno es
/// cambiar una línea (pregunta abierta 1 de la spec).
/// <list type="bullet">
/// <item><c>SCDAT</c> y <c>SCTIM</c>: fijos (decisión del responsable).</item>
/// <item><c>Evento1</c>: PROVISORIO. El nombre real que usa el Daily de Dairy Comp se consigue después.</item>
/// <item><c>DIASPRE</c>, <c>DIASABO</c> y <c>RESABO</c>: PROVISORIOS, sin confirmar.</item>
/// </list>
/// </summary>
public static class ColumnasExportacion
{
    public static readonly ColumnaExportacion Fecha = new("SCDAT", TipoColumnaExportacion.Texto);
    public static readonly ColumnaExportacion Hora = new("SCTIM", TipoColumnaExportacion.Texto);
    public static readonly ColumnaExportacion Evento = new("Evento1", TipoColumnaExportacion.Texto);
    public static readonly ColumnaExportacion DiasPreniez = new("DIASPRE", TipoColumnaExportacion.Entero);
    public static readonly ColumnaExportacion DiasAborto = new("DIASABO", TipoColumnaExportacion.Entero);
    public static readonly ColumnaExportacion ResultadoAborto = new("RESABO", TipoColumnaExportacion.Texto);

    /// <summary>Las seis, en el orden en que van en el archivo (el mismo de <see cref="ValoresExportacion.EnOrden"/>).</summary>
    public static readonly IReadOnlyList<ColumnaExportacion> Todas = [Fecha, Hora, Evento, DiasPreniez, DiasAborto, ResultadoAborto];
}
