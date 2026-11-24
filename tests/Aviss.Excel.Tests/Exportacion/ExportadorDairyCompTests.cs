using System.Security.Cryptography;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using static Aviss.Excel.Tests.Exportacion.AyudaExportacion;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>
/// Los cuatro cruces de formato (original .xls o CSV, salida .xls o CSV) con las dos muestras anonimizadas: mismas filas y columnas con
/// los mismos valores, RPRO idéntica, columnas nuevas a la derecha y vacías donde corresponde (spec 007, RF-4, RF-5, RF-11, RF-13, RF-17).
/// </summary>
public class ExportadorDairyCompTests
{
    public static IEnumerable<object[]> Cruces()
    {
        foreach (var origen in new[] { "xls226", "csv282" })
            foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
                yield return [origen, formato];
    }

    private static (byte[] Original, Dictionary<string, ValoresExportacion> Valores) Datos(string origen)
        => origen == "xls226" ? (Xls226(), Valores226()) : (Csv282(), Valores282());

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Cada_cruce_conserva_filas_y_columnas_originales_y_agrega_las_nuevas_a_la_derecha(string origen, FormatoExportacion formato)
    {
        var (original, valores) = Datos(origen);
        var copiaDelOriginal = (byte[])original.Clone();

        var generado = GenerarOk(original, formato, valores);

        Assert.Equal(formato == FormatoExportacion.Csv ? "csv" : "xls", generado.Extension);
        Assert.Equal(copiaDelOriginal, original); // el original nunca se modifica (RF-5)

        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);

        // Columnas: las originales primero y las nuevas a la derecha (RF-4).
        Assert.Equal(a.Encabezados.Concat(ColumnasExportacion.Todas.Select(c => c.Nombre)), b.Encabezados);
        Assert.Equal(a.Filas.Count, b.Filas.Count);
        Assert.Equal(a.TotalDeclarado, b.TotalDeclarado);

        // Cada celda original, con el mismo valor (RF-13: incluida RPRO).
        for (int k = 0; k < a.Filas.Count; k++)
            foreach (var encabezado in a.Encabezados)
                Assert.Equal(Normalizada(a.Filas[k], encabezado), Normalizada(b.Filas[k], encabezado));

        Assert.Equal(a.Filas.Count, generado.Filas);
        Assert.Equal(3, generado.FilasConValores);
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void Las_vacas_con_evento_llevan_sus_valores_y_las_demas_salen_vacias(string origen, FormatoExportacion formato)
    {
        var (original, valores) = Datos(origen);
        var generado = GenerarOk(original, formato, valores);

        var a = LeerOk(original);
        var b = LeerOk(generado.Contenido!);
        int conValores = 0;

        for (int k = 0; k < a.Filas.Count; k++)
        {
            var id = Clave(a.Filas[k].Celdas[a.NombreColumnaId].Texto.Trim());
            var esperado = valores.TryGetValue(id, out var v) ? v : ValoresExportacion.Vacios;
            var obtenido = ColumnasExportacion.Todas.Select(c => Texto(b.Filas[k], c.Nombre)).ToArray();
            Assert.Equal(esperado.EnOrden(), obtenido);
            if (!esperado.EsVacio) conValores++;
        }
        Assert.Equal(3, conValores);
    }

    [Fact]
    public void Los_dos_formatos_de_salida_dan_los_mismos_valores_para_las_mismas_vacas()
    {
        // RF-17: el formato de salida no cambia lo que lleva cada vaca.
        foreach (var origen in new[] { "xls226", "csv282" })
        {
            var (original, valores) = Datos(origen);
            var comoXls = LeerOk(GenerarOk(original, FormatoExportacion.Xls, valores).Contenido!);
            var comoCsv = LeerOk(GenerarOk(original, FormatoExportacion.Csv, valores).Contenido!);

            Assert.Equal(comoXls.Filas.Count, comoCsv.Filas.Count);
            for (int k = 0; k < comoXls.Filas.Count; k++)
                foreach (var c in ColumnasExportacion.Todas)
                    Assert.Equal(Texto(comoXls.Filas[k], c.Nombre), Texto(comoCsv.Filas[k], c.Nombre));
        }
    }

    [Fact]
    public void El_hash_del_original_no_cambia_al_exportar()
    {
        var original = Xls226();
        var antes = SHA256.HashData(original);
        GenerarOk(original, FormatoExportacion.Csv, Valores226());
        GenerarOk(original, FormatoExportacion.Xls, Valores226());
        Assert.Equal(antes, SHA256.HashData(original));
    }

    [Theory]
    [MemberData(nameof(Cruces))]
    public void El_archivo_generado_pasa_la_validacion(string origen, FormatoExportacion formato)
    {
        var (original, valores) = Datos(origen);
        var exportador = new ExportadorDairyComp();
        var generado = GenerarOk(original, formato, valores);

        var resultado = exportador.Validar(original, generado.Contenido!, formato, valores);

        Assert.True(resultado.EsValido, $"La validación falló: {resultado.Diferencia}");
    }

    [Fact]
    public void Un_original_que_ya_trae_una_columna_nueva_se_rechaza_sin_importar_las_mayusculas()
    {
        var original = GeneradorMuestras.Construir(["ID", "RPRO", "scdat"], [["1001", "INSEMIN", "x"]]);

        foreach (var formato in new[] { FormatoExportacion.Csv, FormatoExportacion.Xls })
        {
            var r = new ExportadorDairyComp().Generar(original, formato, new Dictionary<string, ValoresExportacion>());
            Assert.False(r.EsCorrecto);
            Assert.Equal(FalloGeneracion.ColumnaDuplicada, r.Fallo);
            Assert.Null(r.Contenido);
        }
    }

    [Fact]
    public void Un_original_ilegible_devuelve_un_fallo_sin_lanzar()
    {
        foreach (var nombre in new[] { "corrupto.xls", "vacio.xls", "texto-plano.xls", "con-contrasena.xls", "xlsx-renombrado.xls", "sin-columna-id.xls" })
        {
            var r = new ExportadorDairyComp().Generar(Muestra(nombre), FormatoExportacion.Csv, new Dictionary<string, ValoresExportacion>());
            Assert.Equal(FalloGeneracion.OriginalIlegible, r.Fallo);
        }
    }

    [Theory]
    [InlineData(FormatoExportacion.Csv)]
    [InlineData(FormatoExportacion.Xls)]
    public void Las_filas_excluidas_los_ids_repetidos_y_la_fila_Total_salen_sin_valores(FormatoExportacion formato)
    {
        // ID vacío, ID con espacio en el medio, ID repetido (las dos filas) y una vaca buena; más la fila Total.
        var original = GeneradorMuestras.Construir(["ID", "RPRO"],
        [
            ["", "INSEMIN"], ["12 34", "SECA"], ["500", "SECA"], ["500", "VACIA"], ["777", "FRESCA"], ["Total:5"],
        ]);
        var valores = new Dictionary<string, ValoresExportacion>
        {
            [Clave("500")] = Previa,   // repetido en el archivo: ninguna de las dos filas lo recibe
            [Clave("777")] = Ok,
        };

        var generado = GenerarOk(original, formato, valores);
        var b = LeerOk(generado.Contenido!);

        Assert.Equal(5, b.Filas.Count);
        Assert.Equal(5, b.TotalDeclarado);
        var conValores = b.Filas.Where(f => ColumnasExportacion.Todas.Any(c => Texto(f, c.Nombre).Length > 0)).ToList();
        Assert.Single(conValores);                       // solo la vaca 777
        Assert.Equal("OK", Texto(conValores[0], ColumnasExportacion.Evento.Nombre));
        Assert.Equal(1, generado.FilasConValores);
        Assert.True(new ExportadorDairyComp().Validar(original, generado.Contenido!, formato, valores).EsValido);
    }

    private static string Texto(FilaLeida fila, string columna)
        => fila.Celdas.TryGetValue(columna, out var c) && !c.EsVacia ? c.Texto : "";

    /// <summary>Texto de una celda para comparar entre formatos (el CSV no guarda tipos ni los espacios de los bordes de un texto numérico).</summary>
    private static string Normalizada(FilaLeida fila, string columna) => Texto(fila, columna).Trim();
}
