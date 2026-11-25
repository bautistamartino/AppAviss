using System.Globalization;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using NPOI.SS.UserModel;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Agrega las columnas nuevas a la derecha sobre la hoja ya abierta del original (spec 007, RF-16) y escribe el libro. Se edita la
/// hoja en memoria, no se reconstruye celda por celda, para conservar estilos cuando NPOI pudo abrir el libro. Fecha, hora y evento
/// van como TEXTO (así salen "24/11/26" y "09:05" sin depender del formato regional); los días, como número.
/// </summary>
internal static class EscritorXls
{
    public static byte[] Escribir(ISheet hoja, LecturaExcel lectura, ValoresExportacion[] valoresPorFila, PlanColumnasExportacion plan)
    {
        int filaEnc = PerfilDairyComp.FilaDeEncabezados(hoja);
        int columnaInicial = AnchoDeLaHoja(hoja);
        var nuevas = plan.Nuevas;

        // Encabezados: texto, con el mismo estilo que el último encabezado original (negrita, relleno...).
        var filaEncabezados = hoja.GetRow(filaEnc);
        var estilo = UltimaCeldaConContenido(filaEncabezados)?.CellStyle;

        // Dónde está en la hoja cada columna que se reemplaza (SCDAT/SCTIM que el original ya trae), por su encabezado.
        var textosEncabezado = Enumerable.Range(0, columnaInicial)
            .Select(c => PerfilDairyComp.Convertir(filaEncabezados.GetCell(c)) is { } celdaEnc ? celdaEnc.Texto : "")
            .ToList();
        var columnasReemplazo = plan.Reemplazos
            .Select(r => (r.Columna, r.PosValores, Hoja: PlanColumnasExportacion.BuscarPosicion(textosEncabezado, r.NombreOriginal!)))
            .ToList();

        for (int j = 0; j < nuevas.Count; j++)
        {
            var celda = filaEncabezados.CreateCell(columnaInicial + j);
            celda.SetCellValue(nuevas[j].Columna.Nombre);
            if (estilo is not null) celda.CellStyle = estilo;
        }

        // Valores: las filas sin valores no reciben ninguna celda nueva (quedan en blanco) ni se tocan sus SCDAT/SCTIM.
        for (int k = 0; k < lectura.Filas.Count; k++)
        {
            var valores = valoresPorFila[k].EnOrden();
            var fila = hoja.GetRow(lectura.Filas[k].NumeroFila - 1);

            // Reemplazo: solo en filas con tacto. Se pisa el valor de la celda (o se crea si la fila no la tenía).
            if (!valoresPorFila[k].EsVacio)
                foreach (var (columna, pos, enHoja) in columnasReemplazo)
                {
                    if (valores[pos].Length == 0) continue;
                    EscribirValor(fila.GetCell(enHoja) ?? fila.CreateCell(enHoja), columna, valores[pos]);
                }

            for (int j = 0; j < nuevas.Count; j++)
            {
                var texto = valores[nuevas[j].PosValores];
                if (texto.Length == 0) continue;
                EscribirValor(fila.CreateCell(columnaInicial + j), nuevas[j].Columna, texto);
            }
        }

        using var salida = new MemoryStream();
        hoja.Workbook.Write(salida, true);
        return salida.ToArray();
    }

    /// <summary>Texto para casi todo; entero para los días. Si la celda ya tenía otro tipo (por ejemplo una fecha numérica), pasa a texto.</summary>
    private static void EscribirValor(ICell celda, ColumnaExportacion columna, string valor)
    {
        if (columna.Tipo == TipoColumnaExportacion.Entero && int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out int entero))
            celda.SetCellValue(entero);
        else
            celda.SetCellValue(valor);
    }

    /// <summary>La primera columna libre: una más que la última celda con contenido de CUALQUIER fila (nunca se pisa nada).</summary>
    internal static int AnchoDeLaHoja(ISheet hoja)
    {
        int ancho = 0;
        for (int i = hoja.FirstRowNum; i <= hoja.LastRowNum; i++)
        {
            var fila = hoja.GetRow(i);
            if (fila is not null) ancho = Math.Max(ancho, AnchoDeLaFila(fila));
        }
        return ancho;
    }

    internal static int AnchoDeLaFila(IRow fila)
    {
        for (int c = fila.LastCellNum - 1; c >= 0; c--)
        {
            var celda = PerfilDairyComp.Convertir(fila.GetCell(c));
            if (celda is not null && !celda.EsVacia) return c + 1;
        }
        return 0;
    }

    private static ICell? UltimaCeldaConContenido(IRow fila)
    {
        int ancho = AnchoDeLaFila(fila);
        return ancho > 0 ? fila.GetCell(ancho - 1) : null;
    }
}
