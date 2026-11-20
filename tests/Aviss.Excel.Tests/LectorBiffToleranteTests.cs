using Aviss.Application.Importacion;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;

namespace Aviss.Excel.Tests;

/// <summary>
/// El Excel de un tambo real (exportado por Dairy Comp) fallaba en NPOI con "Unexpected record type (SelectionRecord)" y la
/// app lo rechazaba como "dañado". Estos tests arman ese mismo problema con datos inventados (un registro de selección fuera
/// de lugar) y comprueban que el perfil lo lee igual, con el mismo resultado que un archivo sano.
/// </summary>
public class LectorBiffToleranteTests
{
    private static readonly PerfilDairyComp Perfil = new();

    // ---- El caso real: un SelectionRecord en medio de las celdas ----

    [Fact]
    public void El_archivo_con_un_registro_de_seleccion_fuera_de_lugar_hace_fallar_a_NPOI_pero_el_perfil_lo_lee()
    {
        var sano = GeneradorMuestras.Muestra226Vacas();
        var raro = ConSeleccionFueraDeLugar(sano);

        // Precondición del test: de verdad es el caso que NPOI no sabe abrir.
        Assert.ThrowsAny<Exception>(() => new HSSFWorkbook(new MemoryStream(raro)));

        var lectura = LeerOk(raro);
        var esperada = LeerOk(sano);

        Assert.Equal(226, lectura.Filas.Count);
        Assert.Equal(226, lectura.TotalDeclarado);
        Assert.Equal(esperada.Encabezados, lectura.Encabezados);
        Assert.Equal(esperada.Filas.Count, lectura.Filas.Count);
        for (var i = 0; i < esperada.Filas.Count; i++)
        {
            Assert.Equal(esperada.Filas[i].NumeroFila, lectura.Filas[i].NumeroFila);
            Assert.Equal(
                esperada.Filas[i].Celdas.OrderBy(c => c.Key).Select(c => (c.Key, c.Value.Tipo, c.Value.Texto)),
                lectura.Filas[i].Celdas.OrderBy(c => c.Key).Select(c => (c.Key, c.Value.Tipo, c.Value.Texto)));
        }
    }

    [Fact]
    public void Un_archivo_sano_reconstruido_registro_por_registro_da_las_mismas_celdas_que_NPOI()
    {
        var bytes = GeneradorMuestras.Muestra226Vacas();
        var normal = new HSSFWorkbook(new MemoryStream(bytes)).GetSheetAt(0);

        var reconstruido = LectorBiffTolerante.Reconstruir(new POIFSFileSystem(new MemoryStream(bytes))).GetSheetAt(0);

        Assert.Equal(normal.LastRowNum, reconstruido.LastRowNum);
        for (var f = 0; f <= normal.LastRowNum; f++)
        {
            var a = normal.GetRow(f);
            var b = reconstruido.GetRow(f);
            Assert.NotNull(b);
            for (var c = 0; c < a.LastCellNum; c++)
            {
                var celdaA = a.GetCell(c);
                var celdaB = b.GetCell(c);
                if (celdaA is null || celdaA.CellType == CellType.Blank) { Assert.True(celdaB is null || celdaB.CellType == CellType.Blank); continue; }
                Assert.NotNull(celdaB);
                Assert.Equal(celdaA.CellType, celdaB.CellType);
                if (celdaA.CellType == CellType.Numeric) Assert.Equal(celdaA.NumericCellValue, celdaB.NumericCellValue);
                else Assert.Equal(celdaA.StringCellValue, celdaB.StringCellValue);
            }
        }
    }

    [Fact]
    public void Reconstruye_textos_numeros_fechas_booleanos_y_errores()
    {
        // Un libro chico armado con NPOI, para ver cada tipo de celda.
        var origen = new HSSFWorkbook();
        var hoja = origen.CreateSheet("Hoja1");
        var estiloFecha = origen.CreateCellStyle();
        estiloFecha.DataFormat = origen.CreateDataFormat().GetFormat("dd/mm/yyyy");
        var fila = hoja.CreateRow(0);
        fila.CreateCell(0).SetCellValue("Texto ñandú");
        fila.CreateCell(1).SetCellValue(1234.5);
        var celdaFecha = fila.CreateCell(2);
        celdaFecha.SetCellValue(new DateTime(2026, 11, 13));
        celdaFecha.CellStyle = estiloFecha;
        fila.CreateCell(3).SetCellValue(true);
        fila.CreateCell(4).SetCellErrorValue((byte)FormulaError.DIV0.Code);
        using var ms = new MemoryStream();
        origen.Write(ms, true);

        var libro = LectorBiffTolerante.Reconstruir(new POIFSFileSystem(new MemoryStream(ms.ToArray())));

        var f = libro.GetSheetAt(0).GetRow(0);
        Assert.Equal("Texto ñandú", f.GetCell(0).StringCellValue);
        Assert.Equal(1234.5, f.GetCell(1).NumericCellValue);
        Assert.True(DateUtil.IsCellDateFormatted(f.GetCell(2)));
        Assert.Equal(new DateTime(2026, 11, 13), f.GetCell(2).DateCellValue);
        Assert.True(f.GetCell(3).BooleanCellValue);
        Assert.Equal(CellType.Error, f.GetCell(4).CellType);
    }

    [Fact]
    public void Un_archivo_dañado_sigue_siendo_rechazado_como_corrupto()
    {
        var r = Perfil.Leer(GeneradorMuestras.Corrupto(), LimitesImportacion.MaxFilas);

        Assert.False(r.EsCorrecto);
        Assert.Equal(MotivoRechazoArchivo.Corrupto, r.Rechazo);
    }

    // ---- Apoyo ----

    private static LecturaExcel LeerOk(byte[] bytes)
    {
        var r = Perfil.Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto, $"Se esperaba lectura correcta, pero se rechazó: {r.Rechazo} {r.Mensaje}");
        return r.Lectura!;
    }

    // Inserta un registro SELECTION (0x001D) justo después de la primera celda de la hoja: un orden que Excel tolera
    // y NPOI no. Reescribe el contenedor OLE2 con el flujo "Workbook" modificado (todo en memoria).
    private static byte[] ConSeleccionFueraDeLugar(byte[] xls)
    {
        var flujo = LeerFlujoWorkbook(xls);

        const ushort Bof = 0x0809, LabelSst = 0x00FD, Number = 0x0203;
        byte[] seleccion = [0x1D, 0x00, 0x0F, 0x00, 0x03, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0];

        var posicion = 0;
        var hojasVistas = 0;
        var insertarEn = -1;
        while (posicion + 4 <= flujo.Length)
        {
            var tipo = BitConverter.ToUInt16(flujo, posicion);
            var largo = BitConverter.ToUInt16(flujo, posicion + 2);
            var siguiente = posicion + 4 + largo;
            if (tipo == Bof && BitConverter.ToUInt16(flujo, posicion + 6) == 0x0010) hojasVistas++;
            if (hojasVistas >= 1 && tipo is LabelSst or Number) { insertarEn = siguiente; break; }
            posicion = siguiente;
        }
        Assert.True(insertarEn > 0, "No se encontró dónde insertar el registro de selección.");

        var nuevo = new byte[flujo.Length + seleccion.Length];
        Array.Copy(flujo, 0, nuevo, 0, insertarEn);
        Array.Copy(seleccion, 0, nuevo, insertarEn, seleccion.Length);
        Array.Copy(flujo, insertarEn, nuevo, insertarEn + seleccion.Length, flujo.Length - insertarEn);

        var sistema = new POIFSFileSystem();
        sistema.CreateDocument(new MemoryStream(nuevo), "Workbook");
        using var salida = new MemoryStream();
        sistema.WriteFileSystem(salida);
        return salida.ToArray();
    }

    private static byte[] LeerFlujoWorkbook(byte[] xls)
    {
        var sistema = new POIFSFileSystem(new MemoryStream(xls));
        using var entrada = sistema.CreateDocumentInputStream("Workbook");
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int leidos;
        while ((leidos = entrada.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, leidos);
        return ms.ToArray();
    }
}
