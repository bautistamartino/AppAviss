using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using NPOI.SS.UserModel;
using static Aviss.Excel.LectorCsv;

namespace Aviss.Excel.Exportacion;

/// <summary>
/// Escribe el archivo de vuelta en CSV (spec 007, RF-15). Hay dos caminos:
/// <list type="bullet">
/// <item><b>CSV a CSV</b> (<see cref="DesdeTexto"/>): se trabaja A NIVEL DE TEXTO. Cada línea original queda igual, byte por byte,
/// hasta el punto donde se agregan los campos nuevos: se conservan la codificación y su marca, las comillas, los espacios de relleno,
/// los decimales con coma, los saltos de línea y el separador final. Por eso no se pasa por una tabla de celdas (cambiaría
/// <c>12,5</c> o <c>" 20001 "</c>).</item>
/// <item><b>.xls a CSV</b> (<see cref="DesdeHoja"/>): se dibuja la hoja con <see cref="ConvencionCsvDairyComp"/>.</item>
/// </list>
/// Devuelve nulo si no puede escribirlo sin alterar nada (por ejemplo, un carácter que la codificación original no admite).
/// </summary>
internal static class EscritorCsv
{
    // ---- CSV a CSV ----

    public static byte[]? DesdeTexto(byte[] original, CsvAnalizado csv, LecturaExcel lectura, ValoresExportacion[] valoresPorFila)
    {
        var codificacion = ConvencionCsvDairyComp.CodificacionEstricta(csv.Codificacion);
        var texto = csv.Texto;
        var sep = csv.Separador;
        var registros = csv.Registros;

        try
        {
            // Seguro de fidelidad: si volver a codificar el texto SIN tocar nada no da los mismos bytes (por ejemplo, un UTF-8 con
            // bytes inválidos que se leyeron como "?"), no se puede garantizar que las líneas originales queden iguales: no se genera.
            if (!Concatenar(csv.Preambulo, codificacion.GetBytes(texto)).AsSpan().SequenceEqual(original)) return null;

            int filaEncabezados = registros.FindIndex(r => r.Campos.Any(c => c.Trim().Length > 0));
            if (filaEncabezados < 0) return null;

            // Ancho del encabezado en campos reales: si la línea termina en el separador, el último campo vacío no es una columna.
            int anchoEncabezado = CamposReales(texto, registros[filaEncabezados], sep);
            bool encabezadosEntreComillas = texto[registros[filaEncabezados].Inicio] == '"';

            // Valores por número de registro (el mismo que el número de fila menos 1 de la lectura).
            var valoresPorRegistro = new Dictionary<int, ValoresExportacion>();
            for (int k = 0; k < lectura.Filas.Count; k++)
                valoresPorRegistro[lectura.Filas[k].NumeroFila - 1] = valoresPorFila[k];

            var salida = new StringBuilder(texto.Length + registros.Count * 40);
            int cursor = 0;
            for (int i = filaEncabezados; i < registros.Count; i++)
            {
                var registro = registros[i];
                List<string> nuevos;
                if (i == filaEncabezados)
                    nuevos = ColumnasExportacion.Todas
                        .Select(c => encabezadosEntreComillas
                            ? ConvencionCsvDairyComp.Entrecomillar(c.Nombre)
                            : ConvencionCsvDairyComp.Campo(c.Nombre, sep))
                        .ToList();
                else if (registro.Campos.All(c => c.Trim().Length == 0))
                    continue; // línea vacía (o solo separadores): la lectura la saltea, se deja como está
                else
                    nuevos = (valoresPorRegistro.TryGetValue(i, out var v) ? v : ValoresExportacion.Vacios)
                        .EnOrden().Select(x => ConvencionCsvDairyComp.Campo(x, sep)).ToList();

                salida.Append(texto, cursor, registro.Fin - cursor);
                AgregarCampos(salida, texto, registro, sep, anchoEncabezado, nuevos);
                cursor = registro.Fin;
            }
            salida.Append(texto, cursor, texto.Length - cursor);

            return Concatenar(csv.Preambulo, codificacion.GetBytes(salida.ToString()));
        }
        catch (EncoderFallbackException)
        {
            return null; // un texto nuevo con un carácter que esta codificación no tiene
        }
    }

    /// <summary>
    /// Agrega al final del registro los campos vacíos que falten para llegar al ancho del encabezado y después los nuevos. Si la línea
    /// ya termina en el separador (como las de Dairy Comp: <c>...;     0 ;</c>) cada campo va SEGUIDO de su separador, así no queda
    /// una columna vacía en el medio; si no termina en separador, cada campo va PRECEDIDO de uno.
    /// </summary>
    private static void AgregarCampos(StringBuilder salida, string texto, RegistroCsv registro, char sep, int ancho, List<string> nuevos)
    {
        bool terminaEnSeparador = TerminaEnSeparador(texto, registro, sep);
        int reales = CamposReales(texto, registro, sep);

        var campos = new List<string>();
        for (int i = reales; i < ancho; i++) campos.Add("");
        campos.AddRange(nuevos);

        foreach (var campo in campos)
        {
            if (terminaEnSeparador) salida.Append(campo).Append(sep);
            else salida.Append(sep).Append(campo);
        }
    }

    private static bool TerminaEnSeparador(string texto, RegistroCsv r, char sep)
        => r.Fin > r.Inicio && texto[r.Fin - 1] == sep && r.Campos.Count >= 2 && r.Campos[^1].Length == 0;

    /// <summary>Cantidad de campos del registro sin contar el vacío que deja un separador final.</summary>
    private static int CamposReales(string texto, RegistroCsv r, char sep)
        => TerminaEnSeparador(texto, r, sep) ? r.Campos.Count - 1 : r.Campos.Count;

    // ---- .xls a CSV ----

    public static byte[]? DesdeHoja(ISheet hoja, LecturaExcel lectura, ValoresExportacion[] valoresPorFila)
    {
        const char sep = ConvencionCsvDairyComp.Separador;
        int filaEncabezados = PerfilDairyComp.FilaDeEncabezados(hoja);
        if (filaEncabezados < 0) return null;
        int ancho = EscritorXls.AnchoDeLaHoja(hoja); // las columnas nuevas van después de TODAS las que tenga alguna fila

        var valoresPorFilaDeHoja = new Dictionary<int, ValoresExportacion>();
        for (int k = 0; k < lectura.Filas.Count; k++)
            valoresPorFilaDeHoja[lectura.Filas[k].NumeroFila - 1] = valoresPorFila[k];

        var salida = new StringBuilder();
        for (int i = 0; i <= hoja.LastRowNum; i++)
        {
            var fila = hoja.GetRow(i);
            bool conContenido = i >= filaEncabezados && fila is not null && PerfilDairyComp.TieneContenido(fila);
            if (!conContenido) { salida.Append(ConvencionCsvDairyComp.Salto); continue; } // línea vacía: se conserva la numeración

            var textos = new string[ancho];
            for (int c = 0; c < ancho; c++)
                textos[c] = PerfilDairyComp.Convertir(fila!.GetCell(c)) is { } celda ? celda.Texto : "";

            if (i == filaEncabezados)
            {
                foreach (var t in textos) salida.Append(t.Length == 0 ? "" : ConvencionCsvDairyComp.Entrecomillar(t)).Append(sep);
                foreach (var c in ColumnasExportacion.Todas) salida.Append(ConvencionCsvDairyComp.Entrecomillar(c.Nombre)).Append(sep);
            }
            else
            {
                // "Total:226" en una sola celda se escribe como lo hace Dairy Comp en un CSV: "Total:;226".
                var total = PerfilDairyComp.PatronTotal.Match(textos[0]);
                if (total.Success && ancho > 1 && textos.Skip(1).All(t => t.Trim().Length == 0))
                {
                    textos[0] = "Total:";
                    textos[1] = total.Groups[1].Value;
                }

                foreach (var t in textos) salida.Append(ConvencionCsvDairyComp.Campo(t)).Append(sep);
                var valores = valoresPorFilaDeHoja.TryGetValue(i, out var v) ? v : ValoresExportacion.Vacios;
                foreach (var x in valores.EnOrden()) salida.Append(ConvencionCsvDairyComp.Campo(x)).Append(sep);
            }
            salida.Append(ConvencionCsvDairyComp.Salto);
        }

        try { return ConvencionCsvDairyComp.CodificacionEstricta(Encoding.Latin1).GetBytes(salida.ToString()); }
        catch (EncoderFallbackException) { return null; }
    }

    private static byte[] Concatenar(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }
}
