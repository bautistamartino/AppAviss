using System.Diagnostics;
using Aviss.Application.Comandos;
using Aviss.Application.Importacion;
using Aviss.Excel;
using Aviss.Infrastructure.Tests.Soporte;
using NPOI.HSSF.UserModel;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// RF-26: con 20.000 filas de datos, leer y validar tarda menos de 10 s y confirmar menos de 15 s (en la PC de desarrollo,
/// no en la tablet). Usa el perfil real y un .xls armado en el momento. Se excluye con <c>--filter "Categoria!=Rendimiento"</c>.
/// </summary>
[Trait("Categoria", "Rendimiento")]
public sealed class ImportacionRendimientoTests : IDisposable
{
    private readonly EscenarioImportacion _e = new(new PerfilDairyComp());
    public void Dispose() => _e.Dispose();

    private static byte[] Armar20MilFilas()
    {
        using var libro = new HSSFWorkbook();
        var hoja = libro.CreateSheet("Hoja1");
        string[] encabezados = ["ID", "CVET", "FPART", "LACT", "DEL", "COM", "RPRO"];
        var fila0 = hoja.CreateRow(0);
        for (var c = 0; c < encabezados.Length; c++) fila0.CreateCell(c).SetCellValue(encabezados[c]);
        string[] estados = ["PREÑADA", "INSEMIN", "SECA", "VACIA", "FRESCA", "NOINSEM", ""];
        for (var i = 0; i < LimitesImportacion.MaxFilas; i++)
        {
            var f = hoja.CreateRow(i + 1);
            f.CreateCell(0).SetCellValue(100000 + i);          // ID numérico, único
            f.CreateCell(1).SetCellValue(i % 9000);
            f.CreateCell(2).SetCellValue("  1/ 3/26");
            f.CreateCell(3).SetCellValue(i % 6);
            f.CreateCell(4).SetCellValue(i % 400);
            f.CreateCell(5).SetCellValue("observación " + (i % 50));
            f.CreateCell(6).SetCellValue(estados[i % estados.Length]);
        }
        using var ms = new MemoryStream();
        libro.Write(ms, true);
        return ms.ToArray();
    }

    [Fact]
    public async Task Veinte_mil_filas_se_leen_y_validan_en_menos_de_10_segundos_y_se_confirman_en_menos_de_15()
    {
        var archivo = Armar20MilFilas();

        var reloj = Stopwatch.StartNew();
        var prep = await _e.Importacion.PrepararAsync("20mil.xls", new MemoryStream(archivo));
        var lectura = reloj.Elapsed;
        Assert.True(prep.EsCorrecto, prep.Mensaje);
        Assert.Equal(LimitesImportacion.MaxFilas, prep.Valor.Validas.Count);

        reloj.Restart();
        var r = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(prep.Valor.Id, null, "Tambo grande", "Rodeo grande", false));
        var confirmacion = reloj.Elapsed;
        Assert.True(r.EsCorrecto, r.Mensaje);

        Assert.Equal(LimitesImportacion.MaxFilas, _e.Cuenta("Animal"));
        Console.WriteLine($"RENDIMIENTO 20.000 filas: lectura+validación {lectura.TotalSeconds:F2} s, confirmación {confirmacion.TotalSeconds:F2} s");
        Assert.True(lectura < TimeSpan.FromSeconds(10), $"Lectura y validación tardaron {lectura.TotalSeconds:F2} s (tope 10 s).");
        Assert.True(confirmacion < TimeSpan.FromSeconds(15), $"La confirmación tardó {confirmacion.TotalSeconds:F2} s (tope 15 s).");
    }
}
