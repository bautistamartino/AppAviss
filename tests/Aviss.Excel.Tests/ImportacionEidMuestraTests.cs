using System.Text;
using Aviss.Application.Importacion;
using Aviss.Domain.Entidades;

namespace Aviss.Excel.Tests;

/// <summary>Spec 010: el perfil reconoce la columna EID y la muestra con EID se importa como se espera (RF-1, RF-3, RF-7, RF-8, RF-9).</summary>
public class ImportacionEidMuestraTests
{
    private static byte[] Muestra()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aviss.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "muestras", "dairy-comp-eid-282-vacas.csv"));
    }

    private static LecturaExcel Leer(byte[] bytes)
    {
        var r = new PerfilDairyComp().Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto, $"{r.Rechazo} {r.Mensaje}");
        return r.Lectura!;
    }

    private static ResultadoValidacion Validar(LecturaExcel l)
    {
        var ahora = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        return ValidadorImportacion.Validar(l, [
            EstadoReproductivo.Crear("PRENADA", "Preñada", "PREÑADA", ahora),
            EstadoReproductivo.Crear("INSEMIN", "Inseminada", "INSEMIN", ahora),
            EstadoReproductivo.Crear("SECA", "Seca", "SECA", ahora),
            EstadoReproductivo.Crear("VACIA", "Vacía", "VACIA", ahora),
            EstadoReproductivo.Crear("FRESCA", "Fresca", "FRESCA", ahora),
            EstadoReproductivo.Crear("NOINSEM", "No inseminar", "NOINSEM", ahora),
        ]);
    }

    // ---------- RF-1 ----------

    [Fact]
    public void La_muestra_se_lee_con_la_columna_eid_en_segundo_lugar()
    {
        var lectura = Leer(Muestra());

        Assert.Equal("EID", lectura.NombreColumnaEid);
        Assert.Equal(["ID", "EID"], lectura.Encabezados.Take(2));
        Assert.Equal(282, lectura.Filas.Count);          // "Total:;282" no cuenta como vaca
        Assert.Equal(282, lectura.TotalDeclarado);
    }

    [Fact]
    public void El_eid_se_reconoce_en_minusculas_con_espacios_y_en_cualquier_posicion()
    {
        var bytes = GeneradorMuestras.Construir(
            ["ID", "RPRO", " eid "], [["1", "VACIA", "999000111111111"]]);

        var lectura = Leer(bytes);

        Assert.Equal("eid", lectura.NombreColumnaEid);   // el nombre original, sin los espacios de los extremos
        Assert.Equal("999000111111111", Validar(lectura).Validas[0].IdElectronico);
    }

    [Fact]
    public void Un_xls_con_eid_numerico_de_quince_digitos_es_un_eid_valido()
    {
        var bytes = GeneradorMuestras.Construir(
            ["ID", "EID", "RPRO"], [["1", 999000222222222d, "VACIA"], ["2", "999 000 333333333", "VACIA"]]);

        var r = Validar(Leer(bytes));

        Assert.Equal(["999000222222222", "999000333333333"], r.Validas.Select(v => v.IdElectronico));
        Assert.DoesNotContain(r.Advertencias, a => a.Tipo is TipoAdvertencia.EidInvalido or TipoAdvertencia.EidRepetido);
    }

    // ---------- RF-7 ----------

    [Fact]
    public void Los_archivos_sin_columna_eid_no_la_tienen_ni_avisan()
    {
        var csv = Leer(File.ReadAllBytes(Path.Combine(CarpetaMuestras(), "dairy-comp-282-vacas.csv")));
        var xls = Leer(File.ReadAllBytes(Path.Combine(CarpetaMuestras(), "dairy-comp-226-vacas.xls")));

        foreach (var lectura in new[] { csv, xls })
        {
            Assert.Null(lectura.NombreColumnaEid);
            Assert.DoesNotContain("EID", lectura.ColumnasOpcionalesFaltantes);   // EID no es una columna opcional con aviso
            var r = Validar(lectura);
            Assert.False(r.HayColumnaEid);
            Assert.All(r.Validas, v => Assert.Null(v.IdElectronico));
        }
    }

    // ---------- RF-3, RF-8 ----------

    [Fact]
    public void Importar_la_muestra_deja_272_con_eid_y_10_sin_eid_sin_advertencias_de_eid()
    {
        var r = Validar(Leer(Muestra()));

        Assert.Equal(282, r.Validas.Count);
        Assert.Empty(r.Excluidas);
        Assert.True(r.HayColumnaEid);
        Assert.Equal(272, r.ConEid);
        Assert.Equal(10, r.SinEid);
        Assert.DoesNotContain(r.Advertencias, a => a.Tipo is TipoAdvertencia.EidInvalido or TipoAdvertencia.EidRepetido);
        Assert.All(r.Validas.Where(v => v.IdElectronico is not null), v =>
        {
            Assert.Equal(15, v.IdElectronico!.Length);
            Assert.StartsWith("999", v.IdElectronico);   // inventados: nunca un EID real (982...)
        });
        // Las 10 sin EID son las frescas (traían "-").
        Assert.All(r.Validas.Where(v => v.IdElectronico is null), v => Assert.Contains("FRESCA", v.DatosOriginalesJson));
    }

    // ---------- RF-9 ----------

    [Fact]
    public void Los_datos_originales_conservan_eid_scdat_y_sctim_y_no_hay_avisos_por_ellos()
    {
        var r = Validar(Leer(Muestra()));

        // La primera fila (i = 0) trae SCDAT y SCTIM con valor.
        var primera = r.Validas[0].DatosOriginalesJson;
        Assert.Contains("\"EID\":\"999000458000000\"", primera);
        Assert.Contains("\"SCDAT\":", primera);
        Assert.Contains("\"SCTIM\":", primera);

        Assert.DoesNotContain(r.Advertencias, a => a.Mensaje.Contains("SCDAT") || a.Mensaje.Contains("SCTIM"));
        Assert.DoesNotContain(r.Advertencias, a => a.Tipo == TipoAdvertencia.ColumnaOpcionalFaltante && a.Valor is "SCDAT" or "SCTIM");
    }

    private static string CarpetaMuestras()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aviss.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "muestras");
    }
}
