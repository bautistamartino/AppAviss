using System.Text;
using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Microsoft.Extensions.DependencyInjection;
using NPOI.HSSF.UserModel;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// El contenedor real de la app (AddAvissInfrastructure) sobre una base de prueba, con la entrega y el exportador de prueba
/// (el exportador delega en el real) y el reloj y la zona falsos. Sirve para el servicio de exportación (spec 007).
/// </summary>
public sealed class EscenarioExportacion : IDisposable
{
    private readonly ServiceProvider _proveedor;

    public BaseDeDatosDePrueba Base { get; }
    public FakeReloj Reloj => Base.Reloj;
    public FakeZonaHoraria Zona { get; } = new();
    public FakeEntregaArchivo Entrega { get; } = new();
    public FakeExportadorArchivo Exportador { get; } = new();

    public Guid EstadoSeca { get; }
    public Guid EstadoVacia { get; }

    public EscenarioExportacion()
    {
        Base = new BaseDeDatosDePrueba();
        var servicios = new ServiceCollection();
        servicios.AddAvissInfrastructure(new RutasAviss(Base.Directorio));
        // La última registración gana: se reemplazan las piezas que el test controla.
        servicios.AddSingleton<IRelojService>(Base.Reloj);
        servicios.AddSingleton<IZonaHoraria>(Zona);
        servicios.AddSingleton<IEntregaArchivo>(Entrega);
        servicios.AddSingleton<IExportadorArchivo>(Exportador);
        _proveedor = servicios.BuildServiceProvider();

        EstadoSeca = Base.Estado("SECA", "SECA", "SECA");
        EstadoVacia = Base.Estado("VACIA", "VACIA", "VACIA");
    }

    public IExportacionService Servicio => _proveedor.GetRequiredService<IExportacionService>();

    public long Cuenta(string tabla, string? donde = null)
        => Base.Contar($"SELECT COUNT(*) FROM {tabla}" + (donde is null ? "" : " WHERE " + donde));

    /// <summary>Un rodeo con su original guardado. Los nombres son reconocibles para comprobar que ningún mensaje los filtra.</summary>
    public Guid Rodeo(byte[] original, string archivo, Guid? establecimientoId = null, string nombreRodeo = "Lote Secreto")
    {
        var id = Base.Rodeo(establecimientoId ?? Base.Establecimiento("Establo Reservado"));
        Base.Ejecutar("UPDATE Rodeo SET Nombre = $n, ArchivoOrigenNombre = $a, ArchivoOrigenBlob = $b WHERE Id = $id",
            ("$n", nombreRodeo), ("$a", archivo), ("$b", original), ("$id", id));
        return id;
    }

    /// <summary>Una vaca importada del rodeo; <paramref name="cvet"/> va al JSON del original, como lo deja la importación.</summary>
    public Guid Vaca(Guid rodeoId, string caravana, string? cvet = "1")
        => Base.Animal(rodeoId, caravana, "Importado",
            datosOriginalesJson: cvet is null ? $"{{\"ID\":\"{caravana}\"}}" : $"{{\"ID\":\"{caravana}\",\"CVET\":\"{cvet}\"}}");

    public string EstadoDelRodeo(Guid rodeoId) => Base.Escalar("SELECT Estado FROM Rodeo WHERE Id = $id", ("$id", rodeoId))?.ToString() ?? "";

    // ---------- Originales de prueba ----------

    /// <summary>Un CSV al estilo de Dairy Comp (Windows-1252, comillas en el encabezado, relleno y un ';' final).</summary>
    public static byte[] CsvOriginal(params string[] caravanas)
    {
        var texto = new StringBuilder("\"ID\";\"CVET\";\"RPRO\";\r\n");
        foreach (var c in caravanas) texto.Append($" {c} ;1    ;SECA    ;\r\n");
        return Encoding.Latin1.GetBytes(texto.ToString());
    }

    /// <summary>Lo mismo pero con un encabezado extra (para probar la colisión de nombres, RF-14).</summary>
    public static byte[] CsvConColumna(string columnaExtra, params string[] caravanas)
    {
        var texto = new StringBuilder($"\"ID\";\"CVET\";\"RPRO\";\"{columnaExtra}\";\r\n");
        foreach (var c in caravanas) texto.Append($" {c} ;1    ;SECA    ;x ;\r\n");
        return Encoding.Latin1.GetBytes(texto.ToString());
    }

    /// <summary>Un .xls con ID, CVET y RPRO.</summary>
    public static byte[] XlsOriginal(IEnumerable<string> caravanas)
    {
        using var libro = new HSSFWorkbook();
        var hoja = libro.CreateSheet("Hoja1");
        var cab = hoja.CreateRow(0);
        string[] encabezados = ["ID", "CVET", "RPRO"];
        for (var c = 0; c < encabezados.Length; c++) cab.CreateCell(c).SetCellValue(encabezados[c]);
        var fila = 1;
        foreach (var caravana in caravanas)
        {
            var f = hoja.CreateRow(fila++);
            f.CreateCell(0).SetCellValue(double.Parse(caravana));
            f.CreateCell(1).SetCellValue(1);
            f.CreateCell(2).SetCellValue("SECA");
        }
        using var ms = new MemoryStream();
        libro.Write(ms, true);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _proveedor.Dispose();
        Base.Dispose();
    }
}
