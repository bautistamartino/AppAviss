using System.Text;
using Aviss.Application.Importacion;

namespace Aviss.Excel.Tests;

/// <summary>
/// Lectura de un listado de Dairy Comp guardado como CSV o TXT. Todos los datos son inventados: el formato real del CSV
/// de Dairy Comp todavía no se vio, así que el lector es tolerante (separador, codificación, comillas).
/// </summary>
public class PerfilDairyCompCsvTests
{
    private static readonly PerfilDairyComp Perfil = new();

    private static byte[] Utf8(string texto, bool conBom = false)
        => (conBom ? Encoding.UTF8.GetPreamble() : [])
            .Concat(Encoding.UTF8.GetBytes(texto)).ToArray();

    private static LecturaExcel LeerOk(byte[] bytes)
    {
        var r = Perfil.Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto, $"Se esperaba lectura correcta, pero se rechazó: {r.Rechazo} {r.Mensaje}");
        return r.Lectura!;
    }

    private const string Coma =
        "ID,LACT,DEL,FPART,FCELO,UTORO,VINS,DDUC,RPRO,DCC,FAPAR,LECHE\n" +
        "1000,2,241,24/ 3/26 ,16/10/26 ,FULHAN    ,4,35,INSEMIN ,0,,0\n" +
        "1003,2,7,13/11/26 ,,,0,0,FRESCA  ,0,,0\n";

    [Theory]
    [InlineData("Total:;2;;;\n", 2)]            // "Total:" en la columna ID y el número en la celda de al lado
    [InlineData("Total: ;2;;;\n", 2)]
    [InlineData("TOTAL:;0002;;;\n", 2)]
    [InlineData("Total:2;;;;\n", 2)]            // la forma de siempre: todo en una celda
    public void La_fila_Total_con_el_numero_en_otra_celda_no_cuenta_como_vaca(string total, int esperado)
    {
        var lectura = LeerOk(Utf8("ID;RPRO;LACT;\n1000;INSEMIN;2;\n1003;FRESCA;2;\n" + total));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal(esperado, lectura.TotalDeclarado);
    }

    [Fact]
    public void Una_fila_que_empieza_con_Total_pero_sin_numero_sigue_siendo_una_fila()
    {
        var lectura = LeerOk(Utf8("ID;RPRO;LACT;\n1000;INSEMIN;2;\nTotal:;abc;;\n"));

        Assert.Equal(2, lectura.Filas.Count);   // no es una fila de totales: el importador la va a excluir como ID inválido
        Assert.Null(lectura.TotalDeclarado);
    }

    [Fact]
    public void Csv_con_comas_se_lee_como_el_xls()
    {
        var lectura = LeerOk(Utf8(Coma));

        Assert.Equal(["ID", "LACT", "DEL", "FPART", "FCELO", "UTORO", "VINS", "DDUC", "RPRO", "DCC", "FAPAR", "LECHE"],
            lectura.Encabezados);
        Assert.Equal("ID", lectura.NombreColumnaId);
        Assert.Equal("RPRO", lectura.NombreColumnaEstado);
        Assert.Equal(["CVET", "COM"], lectura.ColumnasOpcionalesFaltantes);
        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal(2, lectura.Filas[0].NumeroFila);   // la fila 1 es la de encabezados, igual que en el .xls
        Assert.Equal(3, lectura.Filas[1].NumeroFila);

        var vaca = lectura.Filas[0].Celdas;
        Assert.Equal(TipoCelda.Numerica, vaca["ID"].Tipo);
        Assert.Equal(1000, vaca["ID"].Numero);
        Assert.Equal(241, vaca["DEL"].Numero);
        Assert.Equal(TipoCelda.Texto, vaca["FPART"].Tipo);   // las fechas llegan como texto, igual que en el .xls
        Assert.Equal("24/ 3/26 ", vaca["FPART"].Texto);
        Assert.Equal("INSEMIN ", vaca["RPRO"].Texto);
    }

    [Fact]
    public void Las_celdas_vacias_quedan_vacias()
    {
        var lectura = LeerOk(Utf8(Coma));

        var fresca = lectura.Filas[1].Celdas;
        Assert.True(!fresca.TryGetValue("FCELO", out var celo) || celo.EsVacia);
        Assert.True(!fresca.TryGetValue("UTORO", out var toro) || toro.EsVacia);
    }

    [Fact]
    public void Csv_con_punto_y_coma_y_decimales_con_coma()
    {
        var csv = "ID;RPRO;LECHE\r\n7;PREÑADA;12,5\r\n9;SECA;0\r\n";

        var lectura = LeerOk(Utf8(csv));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal(12.5, lectura.Filas[0].Celdas["LECHE"].Numero);
        Assert.Equal("PREÑADA", lectura.Filas[0].Celdas["RPRO"].Texto);
    }

    [Fact]
    public void Txt_separado_por_tabulaciones()
    {
        var txt = "ID\tRPRO\tDEL\n5\tVACIA\t100\n6\tSECA\t0\n";

        var lectura = LeerOk(Utf8(txt));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal("VACIA", lectura.Filas[0].Celdas["RPRO"].Texto);
        Assert.Equal(100, lectura.Filas[0].Celdas["DEL"].Numero);
    }

    [Fact]
    public void Utf8_con_marca_BOM_no_rompe_el_encabezado_ID()
    {
        var lectura = LeerOk(Utf8("ID,RPRO\n1,SECA\n", conBom: true));

        Assert.Equal("ID", lectura.NombreColumnaId);
        Assert.Single(lectura.Filas);
    }

    [Fact]
    public void Windows_1252_con_tildes_se_lee_bien()
    {
        // Excel y Dairy Comp en Windows suelen guardar en Windows-1252: la Ñ es el byte 0xD1, que no es UTF-8 válido.
        var bytes = Encoding.Latin1.GetBytes("ID,RPRO\n1,PREÑADA\n");

        var lectura = LeerOk(bytes);

        Assert.Equal("PREÑADA", lectura.Filas[0].Celdas["RPRO"].Texto);
    }

    [Fact]
    public void Utf16_con_BOM_se_lee()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("ID\tRPRO\n1\tSECA\n")).ToArray();

        var lectura = LeerOk(bytes);

        Assert.Single(lectura.Filas);
        Assert.Equal("SECA", lectura.Filas[0].Celdas["RPRO"].Texto);
    }

    [Fact]
    public void La_fila_Total_se_separa_y_no_cuenta()
    {
        var lectura = LeerOk(Utf8("ID,RPRO\n1,SECA\n2,VACIA\nTotal:2,\n"));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal(2, lectura.TotalDeclarado);
    }

    [Fact]
    public void Sin_fila_Total_el_total_declarado_es_nulo()
    {
        var lectura = LeerOk(Utf8(Coma));

        Assert.Null(lectura.TotalDeclarado);
    }

    [Fact]
    public void Campo_entre_comillas_puede_llevar_el_separador_y_comillas_dobles()
    {
        var csv = "ID,RPRO,COM\n1,SECA,\"a, b \"\"c\"\"\"\n";

        var lectura = LeerOk(Utf8(csv));

        Assert.Equal("a, b \"c\"", lectura.Filas[0].Celdas["COM"].Texto);
    }

    [Fact]
    public void Las_lineas_vacias_se_saltean_pero_conservan_la_numeracion()
    {
        var lectura = LeerOk(Utf8("ID,RPRO\n1,SECA\n\n\n4,VACIA\n"));

        Assert.Equal([2, 5], lectura.Filas.Select(f => f.NumeroFila));
    }

    [Fact]
    public void El_ID_con_ceros_a_la_izquierda_queda_como_texto()
    {
        var lectura = LeerOk(Utf8("ID,RPRO\n007,SECA\n"));

        Assert.Equal(TipoCelda.Texto, lectura.Filas[0].Celdas["ID"].Tipo);
        Assert.Equal("007", lectura.Filas[0].Celdas["ID"].Texto);
    }

    [Fact]
    public void Los_bytes_del_archivo_no_cambian_al_leerlo()
    {
        var bytes = Utf8(Coma);
        var copia = (byte[])bytes.Clone();

        LeerOk(bytes);

        Assert.Equal(copia, bytes);
    }

    // ---- Rechazos ----

    [Fact]
    public void Csv_con_ID_pero_sin_RPRO_rechaza_e_indica_la_columna()
    {
        var r = Perfil.Leer(Utf8("ID,LACT\n1,2\n"), LimitesImportacion.MaxFilas);

        Assert.False(r.EsCorrecto);
        Assert.Equal(MotivoRechazoArchivo.FaltaColumna, r.Rechazo);
        Assert.Contains("RPRO", r.Mensaje);
    }

    [Fact]
    public void Csv_con_encabezado_duplicado_se_rechaza()
    {
        var r = Perfil.Leer(Utf8("ID,RPRO,ID\n1,SECA,2\n"), LimitesImportacion.MaxFilas);

        Assert.Equal(MotivoRechazoArchivo.EncabezadoDuplicado, r.Rechazo);
    }

    [Fact]
    public void Texto_sin_columna_ID_no_es_un_listado_de_Dairy_Comp()
    {
        var r = Perfil.Leer(Utf8("hola,que,tal\n1,2,3\n"), LimitesImportacion.MaxFilas);

        Assert.Equal(MotivoRechazoArchivo.FormatoNoSoportado, r.Rechazo);
    }

    [Fact]
    public void Binario_cualquiera_no_se_toma_por_csv()
    {
        var binario = new byte[] { 0x01, 0x00, 0x49, 0x44, 0x00, 0x2C, 0x52, 0x50, 0x52, 0x4F, 0x00 };

        var r = Perfil.Leer(binario, LimitesImportacion.MaxFilas);

        Assert.Equal(MotivoRechazoArchivo.FormatoNoSoportado, r.Rechazo);
    }

    [Fact]
    public void Un_csv_que_pasa_el_tope_de_filas_se_rechaza()
    {
        var r = Perfil.Leer(Utf8("ID,RPRO\n1,SECA\n2,SECA\n3,SECA\n"), maxFilas: 2);

        Assert.Equal(MotivoRechazoArchivo.ExcedeFilas, r.Rechazo);
    }
}
