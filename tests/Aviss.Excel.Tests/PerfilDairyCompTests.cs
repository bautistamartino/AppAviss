using Aviss.Application.Importacion;

namespace Aviss.Excel.Tests;

/// <summary>Tests del perfil de Dairy Comp: lee archivos armados por <see cref="GeneradorMuestras"/> (datos inventados).</summary>
public class PerfilDairyCompTests
{
    private static readonly PerfilDairyComp Perfil = new();
    private static readonly string[] Enc = ["ID", "RPRO"];

    private static LecturaExcel LeerOk(byte[] bytes, int maxFilas = LimitesImportacion.MaxFilas)
    {
        var r = Perfil.Leer(bytes, maxFilas);
        Assert.True(r.EsCorrecto, $"Se esperaba lectura correcta, pero se rechazó: {r.Rechazo} {r.Mensaje}");
        return r.Lectura!;
    }

    private static void AssertRechazo(byte[] bytes, MotivoRechazoArchivo esperado)
    {
        var r = Perfil.Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.False(r.EsCorrecto);
        Assert.Equal(esperado, r.Rechazo);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensaje));
    }

    [Fact]
    public void Tiene_nombre_Dairy_Comp() => Assert.Equal("Dairy Comp", Perfil.Nombre);

    // ---- La muestra de 226 vacas ----

    [Fact]
    public void Muestra_de_226_vacas_no_cuenta_la_fila_Total()
    {
        var lectura = LeerOk(GeneradorMuestras.Muestra226Vacas());

        Assert.Equal(226, lectura.Filas.Count);
        Assert.Equal(226, lectura.TotalDeclarado);
        Assert.Equal(GeneradorMuestras.EncabezadosDairyComp, lectura.Encabezados);
        Assert.Equal("ID", lectura.NombreColumnaId);
        Assert.Equal("RPRO", lectura.NombreColumnaEstado);
        Assert.Empty(lectura.ColumnasOpcionalesFaltantes);
        Assert.Equal(2, lectura.Filas[0].NumeroFila); // la fila 1 es la de encabezados
        Assert.Equal(227, lectura.Filas[^1].NumeroFila);
    }

    [Fact]
    public void Los_bytes_del_archivo_no_cambian_al_leerlo()
    {
        var bytes = GeneradorMuestras.Muestra226Vacas();
        var copia = (byte[])bytes.Clone();

        LeerOk(bytes);

        Assert.Equal(copia, bytes);
    }

    // ---- Rechazos del archivo entero: nunca lanzan ----

    [Fact]
    public void Archivo_vacio_se_rechaza() => AssertRechazo([], MotivoRechazoArchivo.Vacio);

    [Fact]
    public void Contenido_nulo_se_rechaza_como_vacio()
        => Assert.Equal(MotivoRechazoArchivo.Vacio, Perfil.Leer(null!, 10).Rechazo);

    [Fact]
    public void Archivo_corrupto_se_rechaza() => AssertRechazo(GeneradorMuestras.Corrupto(), MotivoRechazoArchivo.Corrupto);

    [Fact]
    public void Xlsx_renombrado_a_xls_se_rechaza_por_formato()
        => AssertRechazo(GeneradorMuestras.XlsxRenombrado(), MotivoRechazoArchivo.FormatoNoSoportado);

    [Fact]
    public void La_muestra_de_xlsx_es_de_verdad_un_xlsx_que_NPOI_abre()
    {
        // Verificación de que el caso "xlsx renombrado" prueba lo que dice: el archivo es un libro .xlsx válido.
        using var libro = new NPOI.XSSF.UserModel.XSSFWorkbook(new MemoryStream(GeneradorMuestras.XlsxRenombrado()));

        Assert.Equal(1, libro.NumberOfSheets);
        Assert.Equal("ID", libro.GetSheetAt(0).GetRow(0).GetCell(0).StringCellValue);
    }

    [Fact]
    public void Texto_plano_se_rechaza_por_formato()
        => AssertRechazo(System.Text.Encoding.UTF8.GetBytes("hola\n"), MotivoRechazoArchivo.FormatoNoSoportado);

    [Fact]
    public void Xls_con_contrasena_se_rechaza_como_protegido()
        => AssertRechazo(GeneradorMuestras.ConContrasena(), MotivoRechazoArchivo.Protegido);

    [Fact]
    public void Xlsx_con_contrasena_se_rechaza_como_protegido()
        => AssertRechazo(GeneradorMuestras.XlsxConContrasena(), MotivoRechazoArchivo.Protegido);

    [Fact]
    public void Libro_sin_hojas_se_rechaza() => AssertRechazo(GeneradorMuestras.SinHojas(), MotivoRechazoArchivo.SinHojas);

    [Fact]
    public void Archivo_cortado_a_cualquier_largo_nunca_lanza()
    {
        var bytes = GeneradorMuestras.Muestra226Vacas();
        for (int largo = 0; largo < bytes.Length; largo += 997)
        {
            var corte = bytes.AsSpan(0, largo).ToArray();
            var r = Perfil.Leer(corte, LimitesImportacion.MaxFilas); // no debe lanzar
            Assert.True(r.EsCorrecto || r.Rechazo is not null);
        }
    }

    // ---- Encabezados (RF-3, RF-4) ----

    [Fact]
    public void Encabezados_con_otra_capitalizacion_espacios_y_orden_se_reconocen()
    {
        var bytes = GeneradorMuestras.Construir(["  rpro ", "lact", " Id"], [["VACIA", 2.0, "77"]]);

        var lectura = LeerOk(bytes);

        Assert.Equal("Id", lectura.NombreColumnaId);       // se conserva la capitalización original (sin espacios de borde)
        Assert.Equal("rpro", lectura.NombreColumnaEstado);
        var fila = Assert.Single(lectura.Filas);
        Assert.Equal("77", fila.Celdas["Id"].Texto);
        Assert.Equal("VACIA", fila.Celdas["rpro"].Texto);
    }

    [Fact]
    public void Falta_ID_rechaza_e_indica_la_columna()
    {
        var r = Perfil.Leer(GeneradorMuestras.SinColumna("ID"), 100);

        Assert.Equal(MotivoRechazoArchivo.FaltaColumna, r.Rechazo);
        Assert.Contains("ID", r.Mensaje);
    }

    [Fact]
    public void Falta_RPRO_rechaza_e_indica_la_columna()
    {
        var r = Perfil.Leer(GeneradorMuestras.SinColumna("RPRO"), 100);

        Assert.Equal(MotivoRechazoArchivo.FaltaColumna, r.Rechazo);
        Assert.Contains("RPRO", r.Mensaje);
    }

    [Fact]
    public void Encabezado_duplicado_rechaza_aunque_cambie_la_capitalizacion()
    {
        var r = Perfil.Leer(GeneradorMuestras.EncabezadoDuplicado(), 100);

        Assert.Equal(MotivoRechazoArchivo.EncabezadoDuplicado, r.Rechazo);
        Assert.Contains("lact", r.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Columnas_opcionales_faltantes_se_listan()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "VACIA"]]));

        Assert.Equal(["CVET", "FPART", "LACT", "DEL", "FCELO", "VINS", "UTORO", "COM", "FAPAR"], lectura.ColumnasOpcionalesFaltantes);
    }

    [Fact]
    public void Columnas_desconocidas_se_conservan()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(["ID", "RPRO", "MiColumna"], [["1", "VACIA", "dato"]]));

        Assert.Contains("MiColumna", lectura.Encabezados);
        Assert.Equal("dato", lectura.Filas[0].Celdas["MiColumna"].Texto);
    }

    [Fact]
    public void Encabezado_vacio_se_ignora_y_su_columna_no_se_guarda()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(["ID", "", "RPRO"], [["1", "dato suelto", "VACIA"]]));

        Assert.Equal(["ID", "RPRO"], lectura.Encabezados);
        Assert.DoesNotContain("dato suelto", lectura.Filas[0].Celdas.Values.Select(c => c.Texto));
    }

    [Fact]
    public void Los_encabezados_son_la_primera_fila_con_contenido()
    {
        // Dos filas sin crear antes: los encabezados están en la fila 3 de Excel y el primer dato en la 4.
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "VACIA"]], filasVaciasAntes: 2));

        Assert.Equal(4, lectura.Filas[0].NumeroFila);
    }

    [Fact]
    public void Solo_se_lee_la_primera_hoja()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "VACIA"]], hojasExtra: 2));

        Assert.Single(lectura.Filas);
    }

    [Fact]
    public void Solo_encabezados_da_cero_filas_de_datos()
    {
        var lectura = LeerOk(GeneradorMuestras.SoloEncabezados());

        Assert.Empty(lectura.Filas); // el rechazo "SinFilasDeDatos" lo decide el validador
        Assert.Null(lectura.TotalDeclarado);
    }

    // ---- Fila Total:N (RF-5) y filas vacías (RF-6) ----

    [Theory]
    [InlineData("Total:3")]
    [InlineData("TOTAL : 3")]
    [InlineData("total:3")]
    [InlineData("  Total:3  ")]
    public void Total_se_reconoce_con_sus_variantes_y_no_cuenta_como_dato(string texto)
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "A"], ["2", "B"], ["3", "C"], [texto]]));

        Assert.Equal(3, lectura.Filas.Count);
        Assert.Equal(3, lectura.TotalDeclarado);
    }

    [Fact]
    public void Total_en_el_medio_se_ignora_y_las_filas_posteriores_se_siguen_leyendo()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "A"], ["Total:2"], ["2", "B"]]));

        Assert.Equal(["1", "2"], lectura.Filas.Select(f => f.Celdas["ID"].Texto));
        Assert.Equal(2, lectura.TotalDeclarado);
    }

    [Fact]
    public void Con_varios_Total_se_toma_el_ultimo()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "A"], ["Total:9"], ["2", "B"], ["Total:2"]]));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Equal(2, lectura.TotalDeclarado);
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("Total:")]
    [InlineData("Total:abc")]
    [InlineData("Totales:5")]
    public void Lo_que_no_tiene_forma_de_Total_es_una_fila_de_datos(string texto)
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [["1", "A"], [texto]]));

        Assert.Equal(2, lectura.Filas.Count);
        Assert.Null(lectura.TotalDeclarado);
    }

    [Fact]
    public void Filas_totalmente_vacias_o_con_solo_espacios_se_saltean()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc,
        [
            ["1", "A"],
            [],                 // fila creada sin celdas
            ["  ", " "],        // solo espacios
            [null, null],
            ["2", "B"],
        ]));

        Assert.Equal([2, 6], lectura.Filas.Select(f => f.NumeroFila)); // los números de fila de Excel se conservan
    }

    [Fact]
    public void Fila_con_datos_pero_sin_ID_llega_al_validador()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, [[null, "VACIA"]]));

        var fila = Assert.Single(lectura.Filas);
        Assert.False(fila.Celdas.TryGetValue("ID", out var id) && !id.EsVacia);
        Assert.Equal("VACIA", fila.Celdas["RPRO"].Texto);
    }

    // ---- Tipos de celda (RF-7, RF-12) ----

    [Fact]
    public void Cada_celda_se_clasifica_por_su_tipo()
    {
        var fecha = new DateTime(2026, 3, 5);
        var lectura = LeerOk(GeneradorMuestras.Construir(
            ["ID", "RPRO", "T", "N", "D", "B", "E", "F"],
            [["1", "VACIA", "hola", 123.0, fecha, true, new ErrorExcel(), new FormulaExcel("1+1")]]));

        var c = lectura.Filas[0].Celdas;
        Assert.Equal(TipoCelda.Texto, c["T"].Tipo);
        Assert.Equal("hola", c["T"].Texto);

        Assert.Equal(TipoCelda.Numerica, c["N"].Tipo);
        Assert.Equal("123", c["N"].Texto); // sin ".0"
        Assert.Equal(123.0, c["N"].Numero);

        Assert.Equal(TipoCelda.Fecha, c["D"].Tipo);
        Assert.Equal("05/03/2026", c["D"].Texto);
        Assert.Null(c["D"].Numero);

        Assert.Equal(TipoCelda.Booleana, c["B"].Tipo);
        Assert.Equal(TipoCelda.Error, c["E"].Tipo);
        Assert.Equal(TipoCelda.Formula, c["F"].Tipo);
    }

    [Fact]
    public void Numerica_con_decimales_conserva_el_numero_y_usa_formato_invariante()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(["ID", "RPRO", "N"], [[123.5, "VACIA", 0.25]]));

        var c = lectura.Filas[0].Celdas;
        Assert.Equal("123.5", c["ID"].Texto);
        Assert.Equal(123.5, c["ID"].Numero);
        Assert.Equal("0.25", c["N"].Texto);
    }

    [Fact]
    public void Texto_con_espacios_se_entrega_tal_cual()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(["ID", "RPRO"], [[" 12 ", "VACIA"]]));

        Assert.Equal(" 12 ", lectura.Filas[0].Celdas["ID"].Texto); // recortar es decisión de Application
    }

    // ---- Límites (RF-2, RF-26) ----

    [Fact]
    public void Veinte_mil_filas_se_aceptan()
    {
        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, Filas(20_000)), LimitesImportacion.MaxFilas);

        Assert.Equal(20_000, lectura.Filas.Count);
    }

    [Fact]
    public void Veinte_mil_una_filas_se_rechazan()
        => AssertRechazo(GeneradorMuestras.Construir(Enc, Filas(20_001)), MotivoRechazoArchivo.ExcedeFilas);

    [Fact]
    public void La_fila_Total_y_las_vacias_no_cuentan_para_el_tope()
    {
        var filas = Filas(3).Append(["Total:3"]).Append([]).ToList();

        var lectura = LeerOk(GeneradorMuestras.Construir(Enc, filas), maxFilas: 3);

        Assert.Equal(3, lectura.Filas.Count);
    }

    private static IEnumerable<object?[]> Filas(int n) => Enumerable.Range(1, n).Select(i => new object?[] { i.ToString(), "VACIA" });
}
