using System.Text;

namespace Aviss.Rfid.Diagnostico;

/// <summary>Donde se anota todo lo que pasa en una sesión de diagnóstico (un archivo de texto).</summary>
public interface IRegistroDiagnostico
{
    void Escribir(string linea);
}

/// <summary>
/// Registro en un archivo de texto: uno por sesión, con la fecha en el nombre. Escribe y vacía el buffer en cada línea: si la
/// tablet se apaga o la app se cierra, no se pierde lo ya anotado. Solo guarda lo que manda el bastón y el nombre y dirección del
/// dispositivo; nunca datos de animales ni de tambos.
/// </summary>
public sealed class RegistroArchivo : IRegistroDiagnostico, IDisposable
{
    private readonly StreamWriter _escritor;
    private readonly object _candado = new();

    public RegistroArchivo(string carpeta, Func<DateTime>? ahoraUtc = null)
    {
        Directory.CreateDirectory(carpeta);
        var hora = (ahoraUtc ?? (() => DateTime.UtcNow))();
        var baseNombre = $"diagnostico-baston-{hora:yyyyMMdd-HHmmss}";

        // Dos sesiones en el mismo segundo: la segunda lleva "-2", "-3"... y no pisa a la primera.
        var ruta = Path.Combine(carpeta, baseNombre + ".txt");
        for (var n = 2; File.Exists(ruta) && n < 1000; n++)
            ruta = Path.Combine(carpeta, $"{baseNombre}-{n}.txt");

        Ruta = ruta;
        var flujo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        _escritor = new StreamWriter(flujo, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string Ruta { get; }

    public void Escribir(string linea)
    {
        lock (_candado) _escritor.WriteLine(linea);
    }

    public void Dispose()
    {
        lock (_candado) _escritor.Dispose();
    }
}
