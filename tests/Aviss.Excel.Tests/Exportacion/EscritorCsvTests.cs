using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// El CSV de vuelta (spec 007, RF-15): CSV a CSV a nivel de texto (cada línea original queda igual, byte por byte, hasta el punto de
/// agregar) y .xls a CSV con la convención de Dairy Comp. Los datos son las muestras anonimizadas o textos inventados.
/// </summary>
public class EscritorCsvTests
{
    private static ResultadoGeneracionArchivo Csv(byte[] original, IReadOnlyDictionary<string, ValoresExportacion>? valores = null)
        => GenerarOk(original, FormatoExportacion.Csv, valores ?? new Dictionary<string, ValoresExportacion>());

    // ---- CSV a CSV con la muestra de 282 ----

    [Fact]
    public void Csv_a_csv_cada_linea_original_queda_igual_byte_por_byte_hasta_el_punto_de_agregar()
    {
        var original = Csv282();
        var generado = Csv(original, Valores282()).Contenido!;

        var lineasOriginal = SepararLineas(original);
        var lineasGenerado = SepararLineas(generado);

        Assert.Equal(lineasOriginal.Count, lineasGenerado.Count);
        for (int i = 0; i < lineasOriginal.Count; i++)
            Assert.True(lineasGenerado[i].AsSpan().StartsWith(lineasOriginal[i]), $"La línea {i + 1} no conserva el original");
    }

    [Fact]
    public void Csv_a_csv_conserva_el_punto_y_coma_final_sin_dejar_una_columna_vacia_en_el_medio()
    {
        var generado = Latin1(Csv(Csv282(), Valores282()).Contenido!);
        var lineas = generado.Split('\n');

        // Encabezado: los originales y los nuevos entre comillas, y el ";" final de siempre (12 originales + 7 nuevos).
        Assert.EndsWith("\"LECHE\";\"SCDAT\";\"SCTIM\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";", lineas[0]);
        Assert.Equal(19, lineas[0].Count(c => c == ';'));

        // La primera vaca (ID 20001, con "Preñada fecha previa"): termina en sus siete valores y su ";" final.
        Assert.EndsWith(";     0 ;24/11/2026;0905;Preñada fecha previa;;215;;;", lineas[1]);
        // Una vaca sin evento: siete campos vacíos, cada uno con su separador.
        Assert.EndsWith(";     0 ;;;;;;;;", lineas[4]);
        // Todas las líneas con datos tienen la misma cantidad de separadores que el encabezado.
        Assert.All(lineas.Take(lineas.Length - 1), l => Assert.Equal(19, l.Count(c => c == (char)59)));
    }

    [Fact]
    public void Csv_a_csv_conserva_windows_1252_y_escribe_la_enie_nueva_como_un_solo_byte()
    {
        var original = Csv282();
        var generado = Csv(original, Valores282()).Contenido!;

        Assert.Contains((byte)0xD1, generado);              // la Ñ de PREÑADA del original sigue siendo 0xD1
        Assert.DoesNotContain((byte)0xC3, generado);         // y no pasó a UTF-8
        Assert.Equal(original.Count(b => b == 0xD1), generado.Count(b => b == 0xD1)); // ninguna Ñ original se perdió ni se duplicó
        Assert.Contains((byte)0xF1, generado);                // la ñ de "Preñada fecha previa" es un solo byte
        Assert.Equal(0, generado.Count(b => b == 0xEF)); // sin marca UTF-8
    }

    [Fact]
    public void Csv_a_csv_la_fila_Total_recibe_seis_campos_vacios()
    {
        var generado = Latin1(Csv(Csv282(), Valores282()).Contenido!);
        var total = generado.Split('\n').Single(l => l.StartsWith("Total:"));

        Assert.Equal("Total:;282;;;;;;;;;;;" + ";;;;;;;", total);
    }

    // ---- Codificaciones y formas del CSV (datos inventados) ----

    private const string Base = "\"ID\";\"RPRO\";\"NOTA\"";

    [Fact]
    public void Utf8_con_marca_se_conserva_con_su_marca()
    {
        var original = Concat(Encoding.UTF8.GetPreamble(), Encoding.UTF8.GetBytes(Base + ";\n1001;SECA;Ñandú;\n"));

        var generado = Csv(original, new Dictionary<string, ValoresExportacion> { [Clave("1001")] = Previa }).Contenido!;

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, generado[..3]);
        var texto = new UTF8Encoding(false).GetString(generado, 3, generado.Length - 3);
        Assert.Equal(Base + ";\"SCDAT\";\"SCTIM\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";\n1001;SECA;Ñandú;24/11/2026;0905;Preñada fecha previa;;215;;;\n", texto);
    }

    [Fact]
    public void Utf8_sin_marca_queda_sin_marca()
    {
        var original = Encoding.UTF8.GetBytes(Base + ";\n1001;SECA;Ñandú;\n");

        var generado = Csv(original, new Dictionary<string, ValoresExportacion> { [Clave("1001")] = Ok }).Contenido!;

        Assert.NotEqual((byte)0xEF, generado[0]);
        Assert.EndsWith("Ñandú;24/11/2026;0010;OK;;;;;\n", Encoding.UTF8.GetString(generado));
    }

    [Fact]
    public void Utf16_con_marca_se_conserva()
    {
        var original = Concat(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes(Base + ";\r\n1001;SECA;Ñandú;\r\n"));

        var generado = Csv(original, new Dictionary<string, ValoresExportacion> { [Clave("1001")] = Ok }).Contenido!;

        Assert.Equal(new byte[] { 0xFF, 0xFE }, generado[..2]);
        var texto = Encoding.Unicode.GetString(generado, 2, generado.Length - 2);
        Assert.Contains("\"RESABO\";\r\n1001;SECA;Ñandú;24/11/2026;0010;OK;;;;;\r\n", texto);
    }

    [Fact]
    public void Un_campo_con_salto_de_linea_adentro_cuenta_como_una_sola_fila_y_se_conserva()
    {
        var original = Encoding.UTF8.GetBytes(Base + ";\n1001;SECA;\"linea uno\nlinea dos\";\n1002;VACIA;sin nota;\n");
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("1001")] = Ok, [Clave("1002")] = Previa };

        var generado = Csv(original, valores).Contenido!;

        var texto = Encoding.UTF8.GetString(generado);
        Assert.Contains("1001;SECA;\"linea uno\nlinea dos\";24/11/2026;0010;OK;;;;;\n", texto);
        Assert.Contains("1002;VACIA;sin nota;24/11/2026;0905;Preñada fecha previa;;215;;;\n", texto);
    }

    [Fact]
    public void Decimales_con_coma_relleno_y_saltos_mixtos_se_conservan()
    {
        // Una línea con CRLF y otra con LF: cada una conserva el suyo. "12,5" y " 20001 " no se tocan.
        var original = Encoding.Latin1.GetBytes("\"ID\";\"RPRO\";\"LECHE\";\r\n 20001 ;SECA;12,5;\r\n20002;VACIA;  3,25 ;\n");

        var generado = Latin1(Csv(original, new Dictionary<string, ValoresExportacion> { [Clave("20001")] = Ok }).Contenido!);

        Assert.Equal(
            "\"ID\";\"RPRO\";\"LECHE\";\"SCDAT\";\"SCTIM\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";\r\n" +
            " 20001 ;SECA;12,5;24/11/2026;0010;OK;;;;;\r\n" +
            "20002;VACIA;  3,25 ;;;;;;;;\n",
            generado);
    }

    [Fact]
    public void Una_linea_sin_separador_final_recibe_los_campos_nuevos_precedidos_de_separador()
    {
        var original = Encoding.UTF8.GetBytes("ID,RPRO,LACT\n1001,SECA,2\n1002,VACIA,3");   // coma, sin ";" final y sin salto al final

        var generado = Encoding.UTF8.GetString(Csv(original, new Dictionary<string, ValoresExportacion> { [Clave("1002")] = Aborto }).Contenido!);

        Assert.Equal("ID,RPRO,LACT,SCDAT,SCTIM,Evento1,Evento2,DIASPRE,DIASABO,RESABO\n1001,SECA,2,,,,,,,\n1002,VACIA,3,24/11/2026,1530,Aborto,,,120,Vacía OK", generado);
    }

    [Fact]
    public void Un_valor_con_el_separador_o_comillas_va_entre_comillas()
    {
        var original = Encoding.UTF8.GetBytes("ID;RPRO\n1001;SECA\n");
        var valores = new Dictionary<string, ValoresExportacion>
        {
            [Clave("1001")] = new("24/11/2026", "0905", "Evento; \"raro\"", "", "", "", ""),
        };

        var generado = Encoding.UTF8.GetString(Csv(original, valores).Contenido!);

        Assert.Contains("1001;SECA;24/11/2026;0905;\"Evento; \"\"raro\"\"\";;;", generado);
        Assert.True(new ExportadorDairyComp().Validar(original, Encoding.UTF8.GetBytes(generado), FormatoExportacion.Csv, valores).EsValido);
    }

    [Fact]
    public void Un_texto_nuevo_que_la_codificacion_original_no_admite_no_se_escribe_con_signos_de_pregunta()
    {
        // El original es Windows-1252/Latin-1 (la Ñ suelta es un byte inválido en UTF-8) y el evento trae un carácter que no existe ahí.
        var original = Concat(Encoding.Latin1.GetBytes("ID;RPRO\n1001;"), [0xD1], Encoding.Latin1.GetBytes("\n"));
        var valores = new Dictionary<string, ValoresExportacion>
        {
            [Clave("1001")] = new("24/11/2026", "0905", "Evento € nuevo", "", "", "", ""),
        };

        var r = new ExportadorDairyComp().Generar(original, FormatoExportacion.Csv, valores);

        Assert.Equal(FalloGeneracion.NoSePudoGenerar, r.Fallo);
    }

    // ---- .xls a CSV con la convención ----

    [Fact]
    public void Xls_a_csv_usa_la_convencion_de_dairy_comp()
    {
        var generado = Csv(Xls226(), Valores226());
        var texto = Latin1(generado.Contenido!);
        var lineas = texto.Split("\r\n");

        // Encabezados entre comillas con su ";" final, saltos CRLF, y las columnas nuevas al final.
        Assert.StartsWith("\"ID\";\"CVET\";", lineas[0]);
        Assert.EndsWith("\"FAPAR\";\"SCDAT\";\"SCTIM\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";", lineas[0]);
        Assert.DoesNotContain((byte)0xEF, generado.Contenido!); // sin marca UTF-8

        // Una vaca con evento (ID 10000, número) lleva sus valores; los números salen sin ceros de más.
        var vaca = lineas.Single(l => l.StartsWith("10000;"));
        Assert.EndsWith(";24/11/2026;0905;Preñada fecha previa;;215;;;", vaca);

        // La fila final "Total:226" sale como lo hace Dairy Comp: "Total:;226", con las columnas nuevas vacías.
        Assert.Contains(lineas, l => l.StartsWith("Total:;226;"));
        Assert.Equal(lineas.Last(l => l.Length > 0).Count(c => c == ';'), lineas[0].Count(c => c == ';'));
    }

    [Fact]
    public void Xls_a_csv_se_vuelve_a_leer_con_los_mismos_valores_del_original()
    {
        var original = Xls226();
        var generado = Csv(original, Valores226()).Contenido!;

        var a = LeerOk(original);
        var b = LeerOk(generado);

        Assert.Equal(a.Filas.Count, b.Filas.Count);
        Assert.Equal(226, b.TotalDeclarado);
        for (int k = 0; k < a.Filas.Count; k++)
            foreach (var e in a.Encabezados)
                Assert.Equal(Texto(a.Filas[k], e).Trim(), Texto(b.Filas[k], e).Trim());
    }

    private static string Texto(FilaLeida fila, string columna)
        => fila.Celdas.TryGetValue(columna, out var c) && !c.EsVacia ? c.Texto : "";

    // ---- Ayudas ----

    /// <summary>Las líneas como bytes, sin el salto (para comparar byte por byte).</summary>
    private static List<byte[]> SepararLineas(byte[] bytes)
    {
        var lineas = new List<byte[]>();
        int inicio = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            lineas.Add(bytes[inicio..i]);
            inicio = i + 1;
        }
        if (inicio < bytes.Length) lineas.Add(bytes[inicio..]);
        return lineas;
    }

    private static byte[] Concat(params byte[][] partes) => partes.SelectMany(p => p).ToArray();
}
