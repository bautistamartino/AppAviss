using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// La entrega de archivos de prueba (spec 007): "guarda" en memoria, sin pisar nombres repetidos, y deja que el test haga fallar el
/// guardado (<see cref="Fallo"/> o <see cref="Excepcion"/>) o mire el momento exacto en que se guarda (<see cref="AlGuardar"/>).
/// </summary>
public sealed class FakeEntregaArchivo : IEntregaArchivo
{
    public sealed record Guardado(string Nombre, byte[] Contenido, FormatoExportacion Formato);

    public List<Guardado> Guardados { get; } = new();
    public List<ArchivoGuardado> Compartidos { get; } = new();

    /// <summary>Si tiene valor, el guardado falla con ese motivo y no queda nada.</summary>
    public FalloGuardado? Fallo { get; set; }

    /// <summary>Si tiene valor, el guardado lanza esta excepción (disco que falla a mitad).</summary>
    public Exception? Excepcion { get; set; }

    /// <summary>Se ejecuta al empezar a guardar, antes de comprobar la cancelación.</summary>
    public Action? AlGuardar { get; set; }

    public Task<ResultadoGuardadoArchivo> GuardarAsync(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct = default)
    {
        AlGuardar?.Invoke();
        ct.ThrowIfCancellationRequested();
        if (Excepcion is not null) throw Excepcion;
        if (Fallo is { } fallo) return Task.FromResult(ResultadoGuardadoArchivo.Fallido(fallo));

        // Nunca pisa: si el nombre ya existe, le agrega un número antes de la extensión.
        var nombre = nombreSugerido;
        var punto = nombreSugerido.LastIndexOf('.');
        for (var n = 1; Guardados.Any(g => g.Nombre == nombre); n++)
            nombre = nombreSugerido[..punto] + $" ({n})" + nombreSugerido[punto..];

        Guardados.Add(new Guardado(nombre, (byte[])contenido.Clone(), formato));
        return Task.FromResult(ResultadoGuardadoArchivo.Correcto(new ArchivoGuardado(nombre, "Descargas", "fake://" + nombre)));
    }

    public Task CompartirAsync(ArchivoGuardado archivo, CancellationToken ct = default)
    {
        Compartidos.Add(archivo);
        return Task.CompletedTask;
    }
}
