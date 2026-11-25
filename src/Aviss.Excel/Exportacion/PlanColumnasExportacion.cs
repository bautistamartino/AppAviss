using Aviss.Application.Exportacion;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Una de las columnas de <see cref="ColumnasExportacion.Todas"/> con lo que se hace con ella en ESTE original.
/// <paramref name="PosValores"/> es su lugar en <see cref="ValoresExportacion.EnOrden"/>; <paramref name="NombreOriginal"/> es el
/// encabezado que ya traía el original (con sus mayúsculas), o nulo si la columna hay que crearla a la derecha.
/// </summary>
internal sealed record ColumnaDelPlan(ColumnaExportacion Columna, int PosValores, string? NombreOriginal)
{
    /// <summary>True si el original ya la trae y se reemplaza el valor de las vacas con tacto.</summary>
    public bool Reemplaza => NombreOriginal is not null;
}

/// <summary>
/// Decide, mirando los encabezados del original, qué pasa con cada columna de la exportación (regla del 2026-11-24):
/// <list type="bullet">
/// <item>SCDAT/SCTIM que el original TRAE: se <b>reemplazan</b> (sin distinguir mayúsculas) solo en las vacas con tacto.</item>
/// <item>SCDAT/SCTIM que NO trae: se <b>crean</b> a la derecha y quedan en <see cref="Creadas"/> para avisar.</item>
/// <item>Evento1, DIASPRE, DIASABO y RESABO: siempre nuevas a la derecha; si el original ya trae una, hay <see cref="Choque"/>.</item>
/// </list>
/// Lo comparten el exportador, los dos escritores y el validador, para que coincidan siempre.
/// </summary>
internal sealed class PlanColumnasExportacion
{
    /// <summary>Las que el original ya trae y se reemplazan, en el orden de <see cref="ColumnasExportacion.Todas"/>.</summary>
    public IReadOnlyList<ColumnaDelPlan> Reemplazos { get; }

    /// <summary>Las que se agregan a la derecha, en el orden de <see cref="ColumnasExportacion.Todas"/>.</summary>
    public IReadOnlyList<ColumnaDelPlan> Nuevas { get; }

    /// <summary>La primera columna NO reemplazable que el original ya trae (RF-14): si hay una, no se genera nada.</summary>
    public ColumnaExportacion? Choque { get; }

    /// <summary>Nombres de las SCDAT/SCTIM que faltaban en el original y se crean (para el aviso).</summary>
    public IReadOnlyList<string> Creadas => Nuevas.Where(n => n.Columna.ReemplazaSiExiste).Select(n => n.Columna.Nombre).ToList();

    private PlanColumnasExportacion(List<ColumnaDelPlan> reemplazos, List<ColumnaDelPlan> nuevas, ColumnaExportacion? choque)
    {
        Reemplazos = reemplazos;
        Nuevas = nuevas;
        Choque = choque;
    }

    public static PlanColumnasExportacion Crear(IEnumerable<string> encabezadosOriginales)
    {
        var encabezados = encabezadosOriginales.ToList();
        var reemplazos = new List<ColumnaDelPlan>();
        var nuevas = new List<ColumnaDelPlan>();
        ColumnaExportacion? choque = null;

        for (int j = 0; j < ColumnasExportacion.Todas.Count; j++)
        {
            var columna = ColumnasExportacion.Todas[j];
            var existente = encabezados.FirstOrDefault(e => string.Equals(e, columna.Nombre, StringComparison.OrdinalIgnoreCase));
            if (existente is null) nuevas.Add(new ColumnaDelPlan(columna, j, null));
            else if (columna.ReemplazaSiExiste) reemplazos.Add(new ColumnaDelPlan(columna, j, existente));
            else choque ??= columna;
        }
        return new PlanColumnasExportacion(reemplazos, nuevas, choque);
    }

    /// <summary>Los encabezados que debe tener el archivo generado: los del original y a la derecha las nuevas.</summary>
    public List<string> EncabezadosEsperados(IEnumerable<string> encabezadosOriginales)
        => encabezadosOriginales.Concat(Nuevas.Select(n => n.Columna.Nombre)).ToList();

    /// <summary>Posición (desde 0) del campo cuyo texto es <paramref name="nombre"/> (sin distinguir mayúsculas ni bordes), o -1.</summary>
    public static int BuscarPosicion(IReadOnlyList<string> textosDeEncabezado, string nombre)
    {
        for (int i = 0; i < textosDeEncabezado.Count; i++)
            if (string.Equals(textosDeEncabezado[i].Trim(), nombre, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
