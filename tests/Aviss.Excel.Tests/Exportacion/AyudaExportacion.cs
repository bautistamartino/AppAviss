using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;
using Aviss.Domain.Reglas;

namespace Aviss.Excel.Tests.Exportacion;

/// <summary>Piezas comunes de los tests de exportación. Todo sale de las muestras anonimizadas de tests/muestras o de datos inventados.</summary>
internal static class AyudaExportacion
{
    public static readonly PerfilDairyComp Perfil = new();

    public static byte[] Muestra(string nombre)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aviss.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "muestras", nombre));
    }

    public static byte[] Xls226() => Muestra("dairy-comp-226-vacas.xls");
    public static byte[] Csv282() => Muestra("dairy-comp-282-vacas.csv");

    public static string Clave(string caravana)
    {
        Assert.True(Caravana.TryCrear(caravana, out var c, out _));
        return c!.Clave;
    }

    // Tres vacas con evento: una con "Preñada fecha previa" (días de preñez), una con Aborto (días y resultado) y una OK.
    public static readonly ValoresExportacion Previa = new("24/11/2026", "0905", "Preñada fecha previa", "215", "", "");
    public static readonly ValoresExportacion Aborto = new("24/11/2026", "1530", "Aborto", "", "120", "Vacía OK");
    public static readonly ValoresExportacion Ok = new("24/11/2026", "0010", "OK", "", "", "");

    /// <summary>Los valores de tres vacas de la muestra de 226 (IDs 10000, 10013 y 10026).</summary>
    public static Dictionary<string, ValoresExportacion> Valores226() => new()
    {
        [Clave("10000")] = Previa,
        [Clave("10013")] = Aborto,
        [Clave("10026")] = Ok,
    };

    /// <summary>Los valores de tres vacas de la muestra de 282 (IDs 20001, 20008 y 20015).</summary>
    public static Dictionary<string, ValoresExportacion> Valores282() => new()
    {
        [Clave("20001")] = Previa,
        [Clave("20008")] = Aborto,
        [Clave("20015")] = Ok,
    };

    public static LecturaExcel LeerOk(byte[] bytes)
    {
        var r = Perfil.Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto, $"Se esperaba una lectura correcta, pero se rechazó: {r.Rechazo}");
        return r.Lectura!;
    }

    public static ResultadoGeneracionArchivo GenerarOk(byte[] original, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valores)
    {
        var r = new ExportadorDairyComp().Generar(original, formato, valores);
        Assert.True(r.EsCorrecto, $"Se esperaba un archivo, pero falló: {r.Fallo}");
        return r;
    }

    public static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
