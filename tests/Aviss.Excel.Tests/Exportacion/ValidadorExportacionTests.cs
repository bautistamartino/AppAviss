using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using NPOI.HSSF.UserModel;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// La validación (spec 007, RF-18): reabre el archivo generado y lo compara con el original y con los valores esperados. Cada test
/// toma un archivo bueno, le hace UN daño y comprueba que se detecta (y que el mensaje es solo una categoría, sin valores).
/// </summary>
public class ValidadorExportacionTests
{
    private static readonly ExportadorDairyComp Exportador = new();

    private static (byte[] Original, byte[] Generado) CsvBueno()
    {
        var original = Csv282();
        return (original, GenerarOk(original, FormatoExportacion.Csv, Valores282()).Contenido!);
    }

    private static byte[] Modificar(byte[] bytes, Func<string, string> cambio) => Encoding.Latin1.GetBytes(cambio(Latin1(bytes)));

    private static ResultadoValidacionArchivo Validar(byte[] original, byte[] generado)
        => Exportador.Validar(original, generado, FormatoExportacion.Csv, Valores282());

    [Fact]
    public void Un_archivo_bueno_es_valido()
    {
        var (original, generado) = CsvBueno();
        Assert.True(Validar(original, generado).EsValido);
    }

    [Fact]
    public void Detecta_una_fila_de_menos()
    {
        var (original, generado) = CsvBueno();
        var sinUnaFila = Modificar(generado, t => string.Join('\n', t.Split('\n').Where((_, i) => i != 5)));

        Assert.Equal(DiferenciaExportacion.FilasDistintas, Validar(original, sinUnaFila).Diferencia);
    }

    [Fact]
    public void Detecta_un_valor_original_cambiado()
    {
        var (original, generado) = CsvBueno();
        var cambiado = Modificar(generado, t => t.Replace("TOROCUATRO", "TOROCUATRX"));

        Assert.Equal(DiferenciaExportacion.ValorOriginalCambiado, Validar(original, cambiado).Diferencia);
    }

    [Fact]
    public void Detecta_una_columna_faltante()
    {
        var (original, generado) = CsvBueno();
        var sinColumna = Modificar(generado, t => t.Replace("\"RESABO\";", ""));

        Assert.Equal(DiferenciaExportacion.ColumnasDistintas, Validar(original, sinColumna).Diferencia);
    }

    [Fact]
    public void Detecta_una_columna_nueva_fuera_de_orden()
    {
        var (original, generado) = CsvBueno();
        var cambiado = Modificar(generado, t => t.Replace("\"SCDAT\";\"SCTIM\"", "\"SCTIM\";\"SCDAT\""));

        Assert.Equal(DiferenciaExportacion.ColumnasDistintas, Validar(original, cambiado).Diferencia);
    }

    [Fact]
    public void Detecta_un_valor_nuevo_equivocado()
    {
        var (original, generado) = CsvBueno();
        var equivocado = Modificar(generado, t => t.Replace(";0905;", ";0906;"));

        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto, Validar(original, equivocado).Diferencia);
    }

    [Fact]
    public void Detecta_un_valor_nuevo_en_una_vaca_que_no_deberia_tenerlo()
    {
        var (original, generado) = CsvBueno();
        // La vaca de la línea 5 no tiene evento: se le pone uno a mano.
        var conDeMas = Modificar(generado, t =>
        {
            var lineas = t.Split('\n');
            lineas[4] = lineas[4][..^6] + "24/11/2026;;;;;;";
            return string.Join('\n', lineas);
        });

        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto, Validar(original, conDeMas).Diferencia);
    }

    [Fact]
    public void Detecta_que_a_una_vaca_con_evento_le_falta_su_valor()
    {
        var (original, generado) = CsvBueno();
        var sinValor = Modificar(generado, t => t.Replace("24/11/2026;0010;OK;;;;", ";;;;;;"));

        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto, Validar(original, sinValor).Diferencia);
    }

    [Fact]
    public void Un_archivo_que_no_se_puede_abrir_es_ilegible()
    {
        var (original, _) = CsvBueno();

        Assert.Equal(DiferenciaExportacion.ArchivoIlegible, Validar(original, [1, 2, 3, 4]).Diferencia);
        Assert.Equal(DiferenciaExportacion.ArchivoIlegible, Validar(original, []).Diferencia);
    }

    [Fact]
    public void En_xls_detecta_un_valor_original_cambiado_y_un_valor_nuevo_equivocado()
    {
        var original = Xls226();
        var bueno = GenerarOk(original, FormatoExportacion.Xls, Valores226()).Contenido!;
        Assert.True(Exportador.Validar(original, bueno, FormatoExportacion.Xls, Valores226()).EsValido);

        // Se cambia el RPRO de una vaca (celda original, columna J = índice 9) y, en otro archivo, la hora de una vaca (columna M = 12).
        var rproCambiado = EditarCelda(bueno, fila: 3, columna: 9, "CAMBIADO");
        var horaCambiada = EditarCelda(bueno, fila: 1, columna: 12, "0906");

        Assert.Equal(DiferenciaExportacion.ValorOriginalCambiado, Exportador.Validar(original, rproCambiado, FormatoExportacion.Xls, Valores226()).Diferencia);
        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto, Exportador.Validar(original, horaCambiada, FormatoExportacion.Xls, Valores226()).Diferencia);
    }

    [Fact]
    public void Si_los_valores_esperados_no_son_los_del_archivo_la_validacion_falla()
    {
        // El archivo se generó con un diccionario y se valida contra otro (una vaca con otro evento).
        var original = Xls226();
        var generado = GenerarOk(original, FormatoExportacion.Csv, Valores226()).Contenido!;
        var otros = Valores226();
        otros[Clave("10000")] = Ok;

        Assert.Equal(DiferenciaExportacion.ValorNuevoIncorrecto, Exportador.Validar(original, generado, FormatoExportacion.Csv, otros).Diferencia);
    }

    [Fact]
    public void El_resultado_solo_trae_una_categoria_nunca_un_valor()
    {
        var (original, generado) = CsvBueno();
        var cambiado = Modificar(generado, t => t.Replace("TOROCUATRO", "TOROCUATRX"));

        var resultado = Validar(original, cambiado);

        // El tipo del resultado es un enum: no hay campo de texto donde pudiera filtrarse una caravana o un nombre.
        Assert.Equal(typeof(DiferenciaExportacion?), resultado.GetType().GetProperty(nameof(resultado.Diferencia))!.PropertyType);
        Assert.DoesNotContain("TOROCUATR", resultado.ToString());
    }

    private static byte[] EditarCelda(byte[] xls, int fila, int columna, string texto)
    {
        var libro = new HSSFWorkbook(new MemoryStream(xls));
        libro.GetSheetAt(0).GetRow(fila).GetCell(columna).SetCellValue(texto);
        using var ms = new MemoryStream();
        libro.Write(ms, true);
        return ms.ToArray();
    }
}
