using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// Spec 011 (RF-19 a RF-22): la columna <c>Evento2</c> siempre está, justo a la derecha de <c>Evento1</c>, en los cuatro cruces de
/// formato (original .xls o CSV, salida .xls o CSV). Sale vacía donde la vaca no tiene Evento 2 y el validador de reapertura la tomó
/// sin cambios. Datos: las muestras anonimizadas o textos inventados.
/// </summary>
public class ExportacionDosEventosTests
{
    public static IEnumerable<object[]> Cruces()
    {
        foreach (var origen in new[] { "xls226", "csv282" })
            foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
                yield return [origen, formato];
    }

    private static (byte[] Original, string IdConDosEventos, string IdSinEvento2) Datos(string origen)
        => origen == "xls226" ? (Xls226(), "10000", "10026") : (Csv282(), "20001", "20015");

    private static string Texto(FilaLeida fila, string columna)
        => fila.Celdas.TryGetValue(columna, out var c) && !c.EsVacia ? c.Texto.Trim() : "";

    [Fact]
    public void Evento2_va_justo_a_la_derecha_de_Evento1_en_el_plan()
    {
        var nombres = ColumnasExportacion.Todas.Select(c => c.Nombre).ToList();
        Assert.Equal(nombres.IndexOf("Evento1") + 1, nombres.IndexOf("Evento2"));
        Assert.Equal(["SCDAT", "SCTIM", "Evento1", "Evento2", "DIASPRE", "DIASABO", "RESABO"], nombres);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Una_vaca_con_dos_eventos_lleva_los_dos_nombres_el_mismo_SCDAT_y_SCTIM_y_lo_demas_vacio(string origen, FormatoExportacion formato)
    {
        var (original, conDos, sinEvento2) = Datos(origen);
        var valores = new Dictionary<string, ValoresExportacion> { [Clave(conDos)] = DosEventos, [Clave(sinEvento2)] = Ok };

        var generado = GenerarOk(original, formato, valores);
        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);

        Assert.Equal(a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)), b.Encabezados);
        var k1 = a.Filas.ToList().FindIndex(f => Clave(f.Celdas[a.NombreColumnaId].Texto.Trim()) == Clave(conDos));
        var k2 = a.Filas.ToList().FindIndex(f => Clave(f.Celdas[a.NombreColumnaId].Texto.Trim()) == Clave(sinEvento2));

        Assert.Equal("24/11/2026", Texto(b.Filas[k1], "SCDAT"));
        Assert.Equal("0010", Texto(b.Filas[k1], "SCTIM"));
        Assert.Equal("OK", Texto(b.Filas[k1], "Evento1"));
        Assert.Equal("PROSTAG", Texto(b.Filas[k1], "Evento2"));
        Assert.Equal("", Texto(b.Filas[k1], "DIASPRE"));
        Assert.Equal("", Texto(b.Filas[k1], "DIASABO"));
        Assert.Equal("", Texto(b.Filas[k1], "RESABO"));

        // La vaca con un solo evento deja Evento2 vacía.
        Assert.Equal("OK", Texto(b.Filas[k2], "Evento1"));
        Assert.Equal("", Texto(b.Filas[k2], "Evento2"));

        // Las demás filas (sin tacto) tienen Evento2 vacía, y el archivo pasa la reapertura.
        for (int k = 0; k < b.Filas.Count; k++)
            if (k != k1) Assert.Equal("", Texto(b.Filas[k], "Evento2"));
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Sin_ningun_Evento2_la_columna_igual_esta_y_vacia(string origen, FormatoExportacion formato)
    {
        var (original, _, sinEvento2) = Datos(origen);
        var valores = new Dictionary<string, ValoresExportacion> { [Clave(sinEvento2)] = Ok };

        var b = LeerOk(GenerarOk(original, formato, valores).Contenido!);

        Assert.Contains("Evento2", b.Encabezados);
        Assert.All(b.Filas, f => Assert.Equal("", Texto(f, "Evento2")));
    }

    [Fact]
    public void Csv_a_csv_cada_linea_original_queda_igual_byte_por_byte_con_dos_eventos()
    {
        var original = Csv282();
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("20001")] = DosEventos };

        var generado = GenerarOk(original, FormatoExportacion.Csv, valores).Contenido!;

        var lineasOriginal = Lineas(original);
        var lineasGenerado = Lineas(generado);
        Assert.Equal(lineasOriginal.Count, lineasGenerado.Count);
        for (int i = 0; i < lineasOriginal.Count; i++)
            Assert.True(lineasGenerado[i].AsSpan().StartsWith(lineasOriginal[i]), $"La línea {i + 1} no conserva el original");

        var texto = Latin1(generado).Split('\n');
        Assert.EndsWith("\"SCDAT\";\"SCTIM\";\"Evento1\";\"Evento2\";\"DIASPRE\";\"DIASABO\";\"RESABO\";", texto[0]);
        Assert.EndsWith(";24/11/2026;0010;OK;PROSTAG;;;;", texto[1]);
        // Total: siete campos nuevos vacíos, cada uno con su separador.
        Assert.EndsWith(";;;;;;;", texto.Single(l => l.StartsWith("Total:")));
    }

    [Theory]
    [InlineData("Evento2")]
    [InlineData("EVENTO2")]
    [InlineData("evento2")]
    public void Un_original_que_ya_trae_Evento2_se_rechaza_sin_distinguir_mayusculas(string columna)
    {
        var original = GeneradorMuestras.Construir(["ID", "RPRO", columna], [["1001", "INSEMIN", "x"]]);

        foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
        {
            var r = new ExportadorDairyComp().Generar(original, formato, new Dictionary<string, ValoresExportacion>());
            Assert.Equal(FalloGeneracion.ColumnaDuplicada, r.Fallo);
            Assert.Equal("Evento2", r.ColumnaDuplicada);
            Assert.Null(r.Contenido);
        }
    }

    [Fact]
    public void Evento_2_con_espacio_no_cuenta_como_columna_repetida()
    {
        var original = GeneradorMuestras.Construir(["ID", "RPRO", "Evento 2"], [["1001", "INSEMIN", "x"]]);
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("1001")] = DosEventos };

        foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
        {
            var generado = GenerarOk(original, formato, valores);
            var b = LeerOk(generado.Contenido!);
            Assert.Equal(["ID", "RPRO", "Evento 2", .. ColumnasExportacion.Todas.Select(c => c.Nombre)], b.Encabezados);
            Assert.Equal("x", Texto(b.Filas[0], "Evento 2"));        // la del original no se toca
            Assert.Equal("PROSTAG", Texto(b.Filas[0], "Evento2"));
            Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
        }
    }

    [Theory]
    [InlineData(FormatoExportacion.Csv)]
    [InlineData(FormatoExportacion.Xls)]
    public void Filas_excluidas_y_Total_salen_con_Evento2_vacia(FormatoExportacion formato)
    {
        var original = GeneradorMuestras.Construir(["ID", "RPRO"],
        [
            ["", "INSEMIN"], ["12 34", "SECA"], ["500", "SECA"], ["500", "VACIA"], ["777", "FRESCA"], ["Total:5"],
        ]);
        var valores = new Dictionary<string, ValoresExportacion>
        {
            [Clave("500")] = DosEventos,   // repetido en el archivo: ninguna de las dos filas lo recibe
            [Clave("777")] = DosEventos,
        };

        var b = LeerOk(GenerarOk(original, formato, valores).Contenido!);

        Assert.Equal(5, b.Filas.Count);
        var conEvento2 = b.Filas.Where(f => Texto(f, "Evento2").Length > 0).ToList();
        Assert.Single(conEvento2);                 // solo la vaca 777
        Assert.Equal("PROSTAG", Texto(conEvento2[0], "Evento2"));
    }

    [Fact]
    public void El_validador_detecta_un_Evento2_cambiado_en_el_archivo_generado()
    {
        var original = Csv282();
        var valores = new Dictionary<string, ValoresExportacion> { [Clave("20001")] = DosEventos };
        var generado = GenerarOk(original, FormatoExportacion.Csv, valores).Contenido!;
        var tocado = Encoding.Latin1.GetBytes(Latin1(generado).Replace("OK;PROSTAG;", "OK;OTRO;"));

        var r = new ExportadorDairyComp().Validar(original, tocado, FormatoExportacion.Csv, valores);

        Assert.False(r.EsValido);
    }

    private static List<byte[]> Lineas(byte[] bytes)
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
}
