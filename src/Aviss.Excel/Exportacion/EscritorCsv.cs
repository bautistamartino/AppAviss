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

    public static byte[]? DesdeTexto(byte[] original, CsvAnalizado csv, LecturaExcel lectura, ValoresExportacion[] valoresPorFila, PlanColumnasExportacion plan)
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

            // Dónde está, entre los campos del encabezado, cada columna que se REEMPLAZA (SCDAT/SCTIM que el original ya trae).
            var camposEncabezado = registros[filaEncabezados].Campos;
            var columnasReemplazo = plan.Reemplazos
                .Select(r => (r.PosValores, Campo: PlanColumnasExportacion.BuscarPosicion(camposEncabezado, r.NombreOriginal!)))
                .ToList();

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
                Dictionary<int, string>? reemplazos = null;   // campo del registro -> texto nuevo (solo vacas con tacto)
                if (i == filaEncabezados)
                    nuevos = plan.Nuevas
                        .Select(n => encabezadosEntreComillas
                            ? ConvencionCsvDairyComp.Entrecomillar(n.Columna.Nombre)
                            : ConvencionCsvDairyComp.Campo(n.Columna.Nombre, sep))
                        .ToList();
                else if (registro.Campos.All(c => c.Trim().Length == 0))
                    continue; // línea vacía (o solo separadores): la lectura la saltea, se deja como está
                else
                {
                    var valores = valoresPorRegistro.TryGetValue(i, out var v) ? v : ValoresExportacion.Vacios;
                    var enOrden = valores.EnOrden();
                    nuevos = plan.Nuevas.Select(n => ConvencionCsvDairyComp.Campo(enOrden[n.PosValores], sep)).ToList();
                    if (!valores.EsVacio)
                        foreach (var (pos, campo) in columnasReemplazo)
                            if (enOrden[pos].Length > 0)
                                (reemplazos ??= [])[campo] = ConvencionCsvDairyComp.Campo(enOrden[pos], sep);
                }

                salida.Append(texto, cursor, registro.Inicio - cursor);
                AgregarConReemplazos(salida, texto, registro, sep, reemplazos);
                AgregarCampos(salida, texto, registro, sep, anchoEncabezado, nuevos, reemplazos);
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
    private static void AgregarCampos(
        StringBuilder salida, string texto, RegistroCsv registro, char sep, int ancho, List<string> nuevos, Dictionary<int, string>? reemplazos)
    {
        bool terminaEnSeparador = TerminaEnSeparador(texto, registro, sep);
        int reales = CamposReales(texto, registro, sep);

        var campos = new List<string>();
        // Los campos que la fila no tenía hasta el ancho del encabezado: vacíos, salvo el que se reemplaza (fila corta con tacto).
        for (int i = reales; i < ancho; i++) campos.Add(reemplazos is not null && reemplazos.TryGetValue(i, out var r) ? r : "");
        campos.AddRange(nuevos);

        foreach (var campo in campos)
        {
            if (terminaEnSeparador) salida.Append(campo).Append(sep);
            else salida.Append(sep).Append(campo);
        }
    }

    /// <summary>
    /// Copia el registro tal cual, salvo los campos con reemplazo (SCDAT/SCTIM de una vaca con tacto), que se sustituyen enteros.
    /// Los campos que la fila no alcanza a tener los completa <see cref="AgregarCampos"/>.
    /// </summary>
    private static void AgregarConReemplazos(StringBuilder salida, string texto, RegistroCsv registro, char sep, Dictionary<int, string>? reemplazos)
    {
        if (reemplazos is null) { salida.Append(texto, registro.Inicio, registro.Fin - registro.Inicio); return; }

        var posiciones = PosicionesDeCampos(texto, registro, sep);
        int reales = CamposReales(texto, registro, sep);
        int cursor = registro.Inicio;
        foreach (var (campo, nuevo) in reemplazos.OrderBy(r => r.Key))
        {
            if (campo >= reales) continue; // el campo no existe en esta fila: lo agrega AgregarCampos
            salida.Append(texto, cursor, posiciones[campo].Inicio - cursor).Append(nuevo);
            cursor = posiciones[campo].Fin;
        }
        salida.Append(texto, cursor, registro.Fin - cursor);
    }

    /// <summary>Dónde empieza y termina cada campo del registro en el texto (mismas reglas de comillas que <c>LectorCsv.Separar</c>).</summary>
    private static List<(int Inicio, int Fin)> PosicionesDeCampos(string texto, RegistroCsv registro, char sep)
    {
        var posiciones = new List<(int, int)>();
        int inicio = registro.Inicio, contenido = 0;
        bool entreComillas = false;
        for (int i = registro.Inicio; i < registro.Fin; i++)
        {
            char c = texto[i];
            if (entreComillas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { contenido++; i++; }
                    else entreComillas = false;
                }
                else contenido++;
            }
            else if (c == '"' && contenido == 0) entreComillas = true;
            else if (c == sep) { posiciones.Add((inicio, i)); inicio = i + 1; contenido = 0; }
            else contenido++;
        }
        posiciones.Add((inicio, registro.Fin));
        return posiciones;
    }

    private static bool TerminaEnSeparador(string texto, RegistroCsv r, char sep)
        => r.Fin > r.Inicio && texto[r.Fin - 1] == sep && r.Campos.Count >= 2 && r.Campos[^1].Length == 0;

    /// <summary>Cantidad de campos del registro sin contar el vacío que deja un separador final.</summary>
    private static int CamposReales(string texto, RegistroCsv r, char sep)
        => TerminaEnSeparador(texto, r, sep) ? r.Campos.Count - 1 : r.Campos.Count;

    // ---- .xls a CSV ----

    public static byte[]? DesdeHoja(ISheet hoja, LecturaExcel lectura, ValoresExportacion[] valoresPorFila, PlanColumnasExportacion plan)
    {
        const char sep = ConvencionCsvDairyComp.Separador;
        int filaEncabezados = PerfilDairyComp.FilaDeEncabezados(hoja);
        if (filaEncabezados < 0) return null;
        int ancho = EscritorXls.AnchoDeLaHoja(hoja); // las columnas nuevas van después de TODAS las que tenga alguna fila

        var valoresPorFilaDeHoja = new Dictionary<int, ValoresExportacion>();
        for (int k = 0; k < lectura.Filas.Count; k++)
            valoresPorFilaDeHoja[lectura.Filas[k].NumeroFila - 1] = valoresPorFila[k];

        // Posición de cada columna que se REEMPLAZA (SCDAT/SCTIM que el original ya trae), por su encabezado.
        var filaEnc = hoja.GetRow(filaEncabezados);
        var textosEncabezado = Enumerable.Range(0, ancho)
            .Select(c => PerfilDairyComp.Convertir(filaEnc.GetCell(c)) is { } celdaEnc ? celdaEnc.Texto : "")
            .ToList();
        var columnasReemplazo = plan.Reemplazos
            .Select(r => (r.PosValores, Columna: PlanColumnasExportacion.BuscarPosicion(textosEncabezado, r.NombreOriginal!)))
            .ToList();

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
                foreach (var n in plan.Nuevas) salida.Append(ConvencionCsvDairyComp.Entrecomillar(n.Columna.Nombre)).Append(sep);
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

                var valores = valoresPorFilaDeHoja.TryGetValue(i, out var v) ? v : ValoresExportacion.Vacios;
                var enOrden = valores.EnOrden();

                // Reemplazo de SCDAT/SCTIM: solo en las vacas con tacto; las demás filas salen con lo que traían.
                if (!valores.EsVacio)
                    foreach (var (pos, columna) in columnasReemplazo)
                        if (enOrden[pos].Length > 0) textos[columna] = enOrden[pos];

                foreach (var t in textos) salida.Append(ConvencionCsvDairyComp.Campo(t)).Append(sep);
                foreach (var n in plan.Nuevas) salida.Append(ConvencionCsvDairyComp.Campo(enOrden[n.PosValores])).Append(sep);
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
