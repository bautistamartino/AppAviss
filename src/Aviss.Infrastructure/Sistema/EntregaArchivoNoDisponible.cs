using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Infrastructure.Sistema;

/// <summary>
/// La entrega de archivos "de respaldo": no guarda nada y lo dice. Es lo que usa Infrastructure si la plataforma no registró la suya
/// (en Android la registra <c>MauiProgram</c>, spec 007 T4). Así el contenedor siempre puede armar <c>IExportacionService</c>.
/// </summary>
internal sealed class EntregaArchivoNoDisponible : IEntregaArchivo
{
    public Task<ResultadoGuardadoArchivo> GuardarAsync(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct = default)
        => Task.FromResult(ResultadoGuardadoArchivo.Fallido(FalloGuardado.NoDisponible));

    public Task CompartirAsync(ArchivoGuardado archivo, CancellationToken ct = default) => Task.CompletedTask;
}
