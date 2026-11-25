using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// Regla del 2026-11-24 (spec 007, RF-4 y RF-14 modificados): SCDAT y SCTIM SIEMPRE vienen en el Excel de Dairy Comp y marcan el
/// último tacto. Si el original las trae, la exportación REEMPLAZA su valor solo en las vacas con tacto del día; las demás filas
/// quedan como venían. Si faltan, se crean a la derecha y se avisa. Todo con muestras inventadas (nada de tambos reales).
/// </summary>
public class ReemplazoScdatSctimTests
{
    private static readonly string[] Sufijo = ["Evento1", "Evento2", "DIASPRE", "DIASABO", "RESABO"];

    // 1001 sin tacto, 1002 con tacto ("Preñada fecha previa"), 1003 es CVET 0 pero se tactó a propósito, 1004 sin tacto.
    private static Dictionary<string, ValoresExportacion> Valores() => new()
    {
        [Clave("1002")] = Previa,   // 24/11/2026, 0905
        [Clave("1003")] = Ok,       // 24/11/2026, 0010
    };

    private static object?[][] Filas(bool conFechaHora) =>
    [
        Fila("1001", 1, "INSEMIN", "10/11/2026", "0800", 25, conFechaHora),
        Fila("1002", 1, "VACIA", "11/11/2026", "0930", 30, conFechaHora),
        Fila("1003", 0, "SECA", "12/11/2026", "1010", 0, conFechaHora),
        Fila("1004", 1, "PREÑADA", "13/11/2026", "1111", 28, conFechaHora),
    ];

    // Con fecha y hora: ID, CVET, RPRO, SCDAT, SCTIM, LECHE. Sin ellas: ID, CVET, RPRO, LECHE.
    private static object?[] Fila(string id, int cvet, string rpro, string fecha, string hora, int leche, bool conFechaHora)
        => conFechaHora ? [id, cvet, rpro, fecha, hora, leche] : [id, cvet, rpro, leche];

    private static byte[] OriginalXls(string[] encabezados, bool conFechaHora = true) => GeneradorMuestras.Construir(encabezados, Filas(conFechaHora));

    /// <summary>El mismo original como CSV de Dairy Comp: separador ;, campos con relleno y ; al final.</summary>
    private static byte[] OriginalCsv(string[] encabezados, bool conFechaHora = true)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', encabezados.Select(e => $"\"{e}\""))).Append(";\r\n");
        foreach (var f in Filas(conFechaHora))
            sb.Append(string.Join(';', f.Select((v, i) => i == 0 ? $" {v} " : $"{v}".PadRight(4)))).Append(";\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static byte[] Original(string origen, string[] encabezados, bool conFechaHora = true)
        => origen == "xls" ? OriginalXls(encabezados, conFechaHora) : OriginalCsv(encabezados, conFechaHora);

    private static string[] CabecerasCompletas(string scdat = "SCDAT", string sctim = "SCTIM") => ["ID", "CVET", "RPRO", scdat, sctim, "LECHE"];

    /// <summary>El texto de la celda (sin los espacios de los bordes) de la fila k; "" si no hay celda.</summary>
    private static string T(LecturaExcel lectura, int k, string columna)
        => lectura.Filas[k].Celdas.TryGetValue(columna, out var c) && !c.EsVacia ? c.Texto.Trim() : "";

    public static IEnumerable<object[]> Cruces()
    {
        foreach (var origen in new[] { "xls", "csv" })
            foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
                yield return [origen, formato];
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Si_el_original_trae_SCDAT_y_SCTIM_se_reemplazan_solo_en_las_vacas_con_tacto(string origen, FormatoExportacion formato)
    {
        var original = Original(origen, CabecerasCompletas());
        var copia = (byte[])original.Clone();

        var generado = GenerarOk(original, formato, Valores());

        Assert.Equal(copia, original);                      // el original no se toca
        Assert.Empty(generado.ColumnasCreadas!);             // no faltaba ninguna: sin aviso
        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);

        // Sin columnas duplicadas: las originales y a la derecha solo las que de verdad son nuevas.
        Assert.Equal(a.Encabezados.Concat(Sufijo), b.Encabezados);

        // Vacas con tacto: valor del día. La de CVET 0 tactada a propósito, también.
        Assert.Equal("24/11/2026", T(b, 1, "SCDAT"));
        Assert.Equal("0905", T(b, 1, "SCTIM"));
        Assert.Equal("Preñada fecha previa", T(b, 1, "Evento1"));
        Assert.Equal("24/11/2026", T(b, 2, "SCDAT"));
        Assert.Equal("0010", T(b, 2, "SCTIM"));

        // Vacas sin tacto: conservan lo que traían, en todas las columnas.
        foreach (int k in new[] { 0, 3 })
            foreach (var e in a.Encabezados)
                Assert.Equal(T(a, k, e), T(b, k, e));
        // Y en las vacas con tacto, las columnas que no son SCDAT/SCTIM siguen igual.
        for (int k = 0; k < 4; k++)
            foreach (var e in new[] { "ID", "CVET", "RPRO", "LECHE" })
                Assert.Equal(T(a, k, e), T(b, k, e));
        Assert.Equal("10/11/2026", T(b, 0, "SCDAT"));
        Assert.Equal("1111", T(b, 3, "SCTIM"));

        Assert.Equal(2, generado.FilasConValores);
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, Valores()).EsValido);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Los_encabezados_con_otras_mayusculas_tambien_se_reemplazan(string origen, FormatoExportacion formato)
    {
        var original = Original(origen, CabecerasCompletas("scdat", "SctIm"));

        var generado = GenerarOk(original, formato, Valores());

        var b = LeerOk(generado.Contenido!);
        Assert.Equal(["ID", "CVET", "RPRO", "scdat", "SctIm", "LECHE", .. Sufijo], b.Encabezados);   // el encabezado original se respeta
        Assert.Equal("24/11/2026", T(b, 1, "scdat"));
        Assert.Equal("0905", T(b, 1, "SctIm"));
        Assert.Equal("10/11/2026", T(b, 0, "scdat"));
        Assert.Empty(generado.ColumnasCreadas!);
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, Valores()).EsValido);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Si_el_original_no_las_trae_se_crean_a_la_derecha_y_se_avisa(string origen, FormatoExportacion formato)
    {
        var original = Original(origen, ["ID", "CVET", "RPRO", "LECHE"], conFechaHora: false);

        var generado = GenerarOk(original, formato, Valores());

        Assert.Equal(["SCDAT", "SCTIM"], generado.ColumnasCreadas);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal(["ID", "CVET", "RPRO", "LECHE", "SCDAT", "SCTIM", .. Sufijo], b.Encabezados);
        Assert.Equal("24/11/2026", T(b, 1, "SCDAT"));
        Assert.Equal("0905", T(b, 1, "SCTIM"));
        Assert.Equal("", T(b, 0, "SCDAT"));     // la vaca sin tacto queda en blanco
        Assert.Equal("", T(b, 3, "SCTIM"));
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, Valores()).EsValido);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Si_trae_solo_SCDAT_se_crea_SCTIM_y_se_avisa_solo_de_esa(string origen, FormatoExportacion formato)
    {
        var original = origen == "xls"
            ? GeneradorMuestras.Construir(["ID", "CVET", "RPRO", "SCDAT", "LECHE"],
                [["1001", 1, "INSEMIN", "10/11/2026", 25], ["1002", 1, "VACIA", "11/11/2026", 30]])
            : Encoding.Latin1.GetBytes("\"ID\";\"CVET\";\"RPRO\";\"SCDAT\";\"LECHE\";\r\n 1001 ;1;INSEMIN;10/11/2026;25;\r\n 1002 ;1;VACIA;11/11/2026;30;\r\n");
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("1002")] = Previa };

        var generado = GenerarOk(original, formato, valores);

        Assert.Equal(["SCTIM"], generado.ColumnasCreadas);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal(["ID", "CVET", "RPRO", "SCDAT", "LECHE", "SCTIM", .. Sufijo], b.Encabezados);
        Assert.Equal("10/11/2026", T(b, 0, "SCDAT"));
        Assert.Equal("24/11/2026", T(b, 1, "SCDAT"));
        Assert.Equal("0905", T(b, 1, "SCTIM"));
        Assert.Equal("", T(b, 0, "SCTIM"));
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Si_trae_solo_SCTIM_se_crea_SCDAT_y_se_avisa_solo_de_esa(string origen, FormatoExportacion formato)
    {
        var original = origen == "xls"
            ? GeneradorMuestras.Construir(["ID", "SCTIM", "CVET", "RPRO"],
                [["1001", "0800", 1, "INSEMIN"], ["1002", "0930", 1, "VACIA"]])
            : Encoding.Latin1.GetBytes("\"ID\";\"SCTIM\";\"CVET\";\"RPRO\";\r\n 1001 ;0800;1;INSEMIN;\r\n 1002 ;0930;1;VACIA;\r\n");
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("1002")] = Aborto };

        var generado = GenerarOk(original, formato, valores);

        Assert.Equal(["SCDAT"], generado.ColumnasCreadas);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal(["ID", "SCTIM", "CVET", "RPRO", "SCDAT", "Evento1", "Evento2", "DIASPRE", "DIASABO", "RESABO"], b.Encabezados);
        Assert.Equal("0800", T(b, 0, "SCTIM"));
        Assert.Equal("1530", T(b, 1, "SCTIM"));
        Assert.Equal("24/11/2026", T(b, 1, "SCDAT"));
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
    }

    [Theory]
    [InlineData("Evento1")]
    [InlineData("Evento2")]
    [InlineData("EVENTO2")]
    [InlineData("diaspre")]
    [InlineData("DIASABO")]
    [InlineData("resabo")]
    public void Las_otras_columnas_nuevas_ya_existentes_siguen_siendo_un_rechazo(string columna)
    {
        var original = GeneradorMuestras.Construir(["ID", "RPRO", "SCDAT", columna], [["1001", "INSEMIN", "x", "y"]]);
        var esperada = ColumnasExportacion.Todas.First(c => string.Equals(c.Nombre, columna, StringComparison.OrdinalIgnoreCase)).Nombre;

        foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
        {
            var r = new ExportadorDairyComp().Generar(original, formato, new Dictionary<string, ValoresExportacion>());
            Assert.Equal(FalloGeneracion.ColumnaDuplicada, r.Fallo);
            Assert.Equal(esperada, r.ColumnaDuplicada);
        }
    }

    // ---- Texto exacto del CSV a CSV y .xls con tipos ----

    [Fact]
    public void Un_CSV_conserva_byte_a_byte_las_lineas_de_las_vacas_sin_tacto()
    {
        var original = Encoding.Latin1.GetBytes(
            "\"ID\";\"CVET\";\"RPRO\";\"SCDAT\";\"SCTIM\";\"LECHE\";\r\n" +
            " 1001 ;1    ;INSEMIN ;10/11/2026;0800;12,5 ;\r\n" +
            " 1002 ;1    ;VACIA   ;11/11/2026;0930;30 ;\r\n");

        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores());

        Assert.Equal(
            "\"ID\";\"CVET\";\"RPRO\";\"SCDAT\";\"SCTIM\";\"LECHE\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";\r\n" +
            " 1001 ;1    ;INSEMIN ;10/11/2026;0800;12,5 ;;;;;;\r\n" +
            " 1002 ;1    ;VACIA   ;24/11/2026;0905;30 ;Preñada fecha previa;;215;;;\r\n",
            Encoding.UTF8.GetString(generado.Contenido!));   // original solo ASCII: se lee como UTF-8, igual que la app
    }

    [Fact]
    public void Un_CSV_sin_separador_final_con_una_fila_corta_completa_los_campos_que_faltan()
    {
        // La fila 1002 termina antes de SCDAT: el valor nuevo cae en su lugar y los campos del medio quedan vacíos.
        var original = Encoding.Latin1.GetBytes("ID;CVET;RPRO;SCDAT;SCTIM;LECHE\n1001;1;INSEMIN;10/11/2026;0800;25\n1002;1;VACIA\n");

        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores());

        Assert.Equal(
            "ID;CVET;RPRO;SCDAT;SCTIM;LECHE;Evento1;Evento2;DIASPRE;DIASABO;RESABO\n" +
            "1001;1;INSEMIN;10/11/2026;0800;25;;;;;\n" +
            "1002;1;VACIA;24/11/2026;0905;;Preñada fecha previa;;215;;\n",
            Encoding.UTF8.GetString(generado.Contenido!));
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, FormatoExportacion.Csv, Valores()).EsValido);
    }

    [Fact]
    public void Un_xls_reemplaza_con_texto_y_deja_intactas_las_celdas_de_las_vacas_sin_tacto()
    {
        var original = OriginalXls(CabecerasCompletas());
        var generado = GenerarOk(original, FormatoExportacion.Xls, Valores()).Contenido!;
        var hoja = new HSSFWorkbook(new MemoryStream(generado)).GetSheetAt(0);

        Assert.Equal(CellType.String, hoja.GetRow(2).GetCell(3).CellType);          // 1002: reemplazada
        Assert.Equal("24/11/2026", hoja.GetRow(2).GetCell(3).StringCellValue);
        Assert.Equal("0905", hoja.GetRow(2).GetCell(4).StringCellValue);
        Assert.Equal("10/11/2026", hoja.GetRow(1).GetCell(3).StringCellValue);       // 1001: sin cambios
        Assert.Equal("0800", hoja.GetRow(1).GetCell(4).StringCellValue);
        Assert.Equal(CellType.Numeric, hoja.GetRow(1).GetCell(5).CellType);          // LECHE sigue numérica
        Assert.Equal("Evento1", hoja.GetRow(0).GetCell(6).StringCellValue);          // las nuevas empiezan después de LECHE
        Assert.Equal(11, hoja.GetRow(0).LastCellNum);                                // 6 originales + 5 nuevas, sin duplicar
    }

    // ---- Validador ----

    [Fact]
    public void El_validador_detecta_un_cambio_en_SCDAT_de_una_vaca_sin_tacto()
    {
        var original = OriginalCsv(CabecerasCompletas());
        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores()).Contenido!;
        var tocado = Encoding.Latin1.GetBytes(Latin1(generado).Replace("10/11/2026", "10/11/2025"));

        var r = new ExportadorDairyComp().Validar(original, tocado, FormatoExportacion.Csv, Valores());

        Assert.Equal(DiferenciaExportacion.ValorOriginalCambiado, r.Diferencia);
    }

    [Fact]
    public void El_validador_detecta_un_valor_viejo_en_una_vaca_con_tacto_y_un_cambio_en_otras_columnas()
    {
        var original = OriginalCsv(CabecerasCompletas());
        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores()).Contenido!;
        var texto = Latin1(generado);

        // Una vaca con tacto que quedó con el valor viejo (no se reemplazó).
        var sinReemplazar = Encoding.Latin1.GetBytes(texto.Replace("24/11/2026;0905", "11/11/2026;0930"));
        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto,
            new ExportadorDairyComp().Validar(original, sinReemplazar, FormatoExportacion.Csv, Valores()).Diferencia);

        // Otra columna original alterada.
        var otraColumna = Encoding.Latin1.GetBytes(texto.Replace("INSEMIN", "INSEMIX"));
        Assert.Equal(DiferenciaExportacion.ValorOriginalCambiado,
            new ExportadorDairyComp().Validar(original, otraColumna, FormatoExportacion.Csv, Valores()).Diferencia);
    }

    [Fact]
    public void El_validador_exige_las_columnas_creadas_cuando_faltaban_y_rechaza_una_duplicada()
    {
        var original = OriginalCsv(["ID", "CVET", "RPRO", "LECHE"], conFechaHora: false);
        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores()).Contenido!;

        var sinSctim = Encoding.Latin1.GetBytes(Latin1(generado).Replace("\"SCTIM\";", ""));
        Assert.Equal(DiferenciaExportacion.ColumnasDistintas,
            new ExportadorDairyComp().Validar(original, sinSctim, FormatoExportacion.Csv, Valores()).Diferencia);

        // Si el generado repitiera SCDAT, la lectura ya lo rechaza (encabezado duplicado): no es válido.
        var duplicada = Encoding.Latin1.GetBytes(Latin1(generado).Replace("\"SCDAT\";", "\"SCDAT\";\"SCDAT\";"));
        Assert.False(new ExportadorDairyComp().Validar(original, duplicada, FormatoExportacion.Csv, Valores()).EsValido);
    }

    [Fact]
    public void La_muestra_con_EID_con_SCDAT_y_SCTIM_al_final_se_exporta_reemplazando_sin_fallar()
    {
        // La muestra imita el archivo real que pasa el usuario (EID al principio, SCDAT y SCTIM al final).
        var original = Muestra("dairy-comp-eid-282-vacas.csv");
        var a = LeerOk(original);
        var id = a.Filas[0].Celdas[a.NombreColumnaId].Texto.Trim();
        var valores = new Dictionary<string, ValoresExportacion> { [Clave(id)] = Previa };

        foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
        {
            var generado = GenerarOk(original, formato, valores);
            var b = LeerOk(generado.Contenido!);
            Assert.Equal(a.Encabezados.Concat(Sufijo), b.Encabezados);
            Assert.Equal("24/11/2026", T(b, 0, "SCDAT"));
            Assert.Equal("0905", T(b, 0, "SCTIM"));
            Assert.Equal(T(a, 1, "SCDAT"), T(b, 1, "SCDAT"));
            Assert.Empty(generado.ColumnasCreadas!);
            Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
        }
    }
}
