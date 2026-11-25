using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// El .xls de vuelta (spec 007, RF-16): las columnas nuevas se agregan sobre la hoja abierta del original; fecha, hora y evento van
/// como texto y los días como número. Se lee con NPOI directo (no con el perfil) para ver el tipo real de cada celda.
/// </summary>
public class EscritorXlsTests
{
    private static ISheet Abrir(byte[] bytes) => new HSSFWorkbook(new MemoryStream(bytes)).GetSheetAt(0);

    [Fact]
    public void Fecha_hora_y_evento_son_texto_y_los_dias_son_numeros()
    {
        var original = Xls226();
        var generado = GenerarOk(original, FormatoExportacion.Xls, Valores226()).Contenido!;
        var hoja = Abrir(generado);

        // Las columnas nuevas empiezan después de la última original (FAPAR es la 11.a: índices 0 a 10).
        var encabezados = hoja.GetRow(0);
        Assert.Equal(["SCDAT", "SCTIM", "Evento1", "Evento2", "DIASPRE", "DIASABO", "RESABO"],
            Enumerable.Range(11, 7).Select(c => encabezados.GetCell(c).StringCellValue));

        // Fila de la vaca 10000 (la primera de datos): "Preñada fecha previa" con 215 días.
        var fila = hoja.GetRow(1);
        Assert.Equal(CellType.String, fila.GetCell(11).CellType);
        Assert.Equal("24/11/2026", fila.GetCell(11).StringCellValue);
        Assert.Equal(CellType.String, fila.GetCell(12).CellType);
        Assert.Equal("0905", fila.GetCell(12).StringCellValue);       // el cero de adelante se conserva
        Assert.Equal("Preñada fecha previa", fila.GetCell(13).StringCellValue);
        Assert.Equal(CellType.Numeric, fila.GetCell(15).CellType);
        Assert.Equal(215, fila.GetCell(15).NumericCellValue);
        Assert.Null(fila.GetCell(14));                                // sin Evento 2: ni siquiera hay celda
        Assert.Null(fila.GetCell(16));                                // sin días de aborto: tampoco
        Assert.Null(fila.GetCell(17));

        // La vaca con Aborto lleva días de aborto (número) y el resultado (texto).
        var aborto = hoja.GetRow(2);
        Assert.Equal(120, aborto.GetCell(16).NumericCellValue);
        Assert.Equal("Vacía OK", aborto.GetCell(17).StringCellValue);
    }

    [Fact]
    public void El_libro_sigue_teniendo_una_sola_hoja_y_las_mismas_filas()
    {
        var original = Xls226();
        var generado = GenerarOk(original, FormatoExportacion.Xls, Valores226()).Contenido!;

        var libroOriginal = new HSSFWorkbook(new MemoryStream(original));
        var libro = new HSSFWorkbook(new MemoryStream(generado));

        Assert.Equal(1, libro.NumberOfSheets);
        Assert.Equal(libroOriginal.GetSheetAt(0).LastRowNum, libro.GetSheetAt(0).LastRowNum);
    }

    [Fact]
    public void Las_celdas_originales_no_cambian_de_tipo_ni_de_valor()
    {
        var original = Xls226();
        var antes = Abrir(original);
        var despues = Abrir(GenerarOk(original, FormatoExportacion.Xls, Valores226()).Contenido!);

        for (int f = 0; f <= antes.LastRowNum; f++)
            for (int c = 0; c < 11; c++)
            {
                var a = antes.GetRow(f)?.GetCell(c);
                var b = despues.GetRow(f)?.GetCell(c);
                if (a is null) { Assert.True(b is null || b.CellType == CellType.Blank); continue; }
                Assert.Equal(a.CellType, b!.CellType);
                if (a.CellType == CellType.Numeric) Assert.Equal(a.NumericCellValue, b.NumericCellValue);
                else if (a.CellType == CellType.String) Assert.Equal(a.StringCellValue, b.StringCellValue);
            }
    }

    [Fact]
    public void Un_libro_que_npoi_no_abre_pasa_por_el_lector_tolerante_y_se_exporta_igual()
    {
        var raro = LectorBiffToleranteTests.ConSeleccionFueraDeLugar(Xls226());
        // Precondición: de verdad es un libro que NPOI no sabe abrir.
        Assert.ThrowsAny<Exception>(() => new HSSFWorkbook(new MemoryStream(raro)));

        var generado = GenerarOk(raro, FormatoExportacion.Xls, Valores226());

        var a = LeerOk(raro);
        var b = LeerOk(generado.Contenido!);
        Assert.Equal(a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)), b.Encabezados);
        Assert.Equal(a.Filas.Count, b.Filas.Count);
        Assert.Equal("Preñada fecha previa", b.Filas[0].Celdas["Evento1"].Texto);
        Assert.True(new ExportadorDairyComp().Validar(raro, generado.Contenido!, FormatoExportacion.Xls, Valores226()).EsValido);

        // Y también a CSV.
        var csv = GenerarOk(raro, FormatoExportacion.Csv, Valores226());
        Assert.True(new ExportadorDairyComp().Validar(raro, csv.Contenido!, FormatoExportacion.Csv, Valores226()).EsValido);
    }

    [Fact]
    public void Un_original_csv_pasa_a_xls_con_los_valores_como_los_lee_la_importacion()
    {
        var original = Csv282();
        var generado = GenerarOk(original, FormatoExportacion.Xls, Valores282()).Contenido!;
        var hoja = Abrir(generado);

        var a = LeerOk(original);
        var b = LeerOk(generado);

        Assert.Equal(a.Filas.Count, b.Filas.Count);
        Assert.Equal(a.TotalDeclarado, b.TotalDeclarado);
        // El ID de la primera vaca (" 20001 " en el CSV) se lee como número en los dos.
        Assert.Equal(a.Filas[0].Celdas["ID"], b.Filas[0].Celdas["ID"]);
        Assert.Equal("0905", hoja.GetRow(1).GetCell(13).StringCellValue);
    }
}
