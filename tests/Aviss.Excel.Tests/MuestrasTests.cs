using Aviss.Application.Importacion;

namespace Aviss.Excel.Tests;

/// <summary>
/// Los archivos de <c>tests/muestras/</c> son generados por código (<see cref="GeneradorMuestras"/>): nunca hay datos reales.
/// El test <c>Generar_muestras</c> los escribe (solo si AVISS_GENERAR_MUESTRAS=1); el resto comprueba que lo commiteado
/// coincide con lo generado.
/// </summary>
public class MuestrasTests
{
    private static string CarpetaMuestras()
    {
        // Sube desde la carpeta de salida del test hasta encontrar la raíz del repo (donde está Aviss.slnx).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aviss.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "muestras");
    }

    [Fact]
    [Trait("Categoria", "Generador")]
    public void Generar_muestras()
    {
        if (Environment.GetEnvironmentVariable("AVISS_GENERAR_MUESTRAS") != "1") return; // no hace nada en una corrida normal

        var carpeta = CarpetaMuestras();
        Directory.CreateDirectory(carpeta);
        foreach (var (nombre, bytes) in GeneradorMuestras.TodasLasMuestras())
            File.WriteAllBytes(Path.Combine(carpeta, nombre), bytes);
    }

    [Fact]
    public void La_generacion_es_determinista()
    {
        var a = GeneradorMuestras.TodasLasMuestras();
        var b = GeneradorMuestras.TodasLasMuestras();

        foreach (var nombre in a.Keys)
            Assert.True(a[nombre].AsSpan().SequenceEqual(b[nombre]), $"{nombre} cambia entre generaciones");
    }

    [Fact]
    public void Los_archivos_commiteados_coinciden_con_lo_generado()
    {
        var carpeta = CarpetaMuestras();
        foreach (var (nombre, bytes) in GeneradorMuestras.TodasLasMuestras())
        {
            var ruta = Path.Combine(carpeta, nombre);
            Assert.True(File.Exists(ruta), $"Falta {nombre}: correr con AVISS_GENERAR_MUESTRAS=1 (ver tests/muestras/README.md)");
            Assert.True(File.ReadAllBytes(ruta).AsSpan().SequenceEqual(bytes), $"{nombre} no coincide con lo generado");
        }
    }

    [Fact]
    public void No_hay_en_la_carpeta_archivos_que_el_generador_no_conozca()
    {
        var conocidos = GeneradorMuestras.TodasLasMuestras().Keys.ToHashSet();
        var sobrantes = Directory.GetFiles(CarpetaMuestras())
            .Select(Path.GetFileName)
            .Where(n => n is not ("README.md" or ".gitkeep") && !conocidos.Contains(n!))
            .ToList();

        Assert.Empty(sobrantes); // un Excel que no salga del generador podría ser un archivo real de un tambo
    }

    [Fact]
    public void La_muestra_commiteada_de_226_vacas_se_lee_con_el_perfil()
    {
        var bytes = File.ReadAllBytes(Path.Combine(CarpetaMuestras(), "dairy-comp-226-vacas.xls"));

        var r = new PerfilDairyComp().Leer(bytes, LimitesImportacion.MaxFilas);

        Assert.True(r.EsCorrecto);
        Assert.Equal(226, r.Lectura!.Filas.Count);
    }

    [Fact]
    public void El_README_existe()
        => Assert.True(File.Exists(Path.Combine(CarpetaMuestras(), "README.md")));
}
