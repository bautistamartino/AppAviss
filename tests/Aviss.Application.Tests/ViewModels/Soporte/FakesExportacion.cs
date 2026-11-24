using Aviss.Application.Comun;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels.Soporte;

/// <summary>
/// El servicio de exportación de prueba para la pantalla "Exportar" (spec 007, T4): devuelve lo que el test le cargue y anota qué se
/// le pidió. Con <see cref="Bloquear"/> la exportación queda "en curso" hasta que el test la libera (o la cancela), como el real.
/// </summary>
public sealed class FakeExportacionService : IExportacionService
{
    public List<RodeoParaExportarDto> Rodeos { get; set; } = [];
    public Resultado<ResumenExportacionDto>? ResumenAEntregar { get; set; }
    public Func<Resultado<ExportacionEntregadaDto>> ResultadoExportar { get; set; } =
        () => Resultado.Ok(new ExportacionEntregadaDto(new ArchivoGuardado("Rodeo_aviss_20261124_1000.csv", "Descargas", "fake://x"), FormatoExportacion.Csv, 10, 4, true));

    public Exception? ExcepcionAlListar { get; set; }
    public Exception? ExcepcionAlResumir { get; set; }
    public Exception? ExcepcionAlExportar { get; set; }

    public int Listados { get; private set; }
    public List<Guid> Resumenes { get; } = [];
    public List<(Guid RodeoId, FormatoExportacion Formato)> Exportaciones { get; } = [];

    private TaskCompletionSource? _bloqueo;

    /// <summary>La próxima exportación espera hasta <see cref="Liberar"/> o hasta que se cancele su token.</summary>
    public void Bloquear() => _bloqueo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Liberar() => _bloqueo?.TrySetResult();

    public Task<IReadOnlyList<RodeoParaExportarDto>> ListarRodeosAsync(CancellationToken ct = default)
    {
        Listados++;
        if (ExcepcionAlListar is not null) throw ExcepcionAlListar;
        return Task.FromResult<IReadOnlyList<RodeoParaExportarDto>>(Rodeos.ToList());
    }

    public Task<Resultado<ResumenExportacionDto>> ObtenerResumenAsync(Guid rodeoId, CancellationToken ct = default)
    {
        Resumenes.Add(rodeoId);
        if (ExcepcionAlResumir is not null) throw ExcepcionAlResumir;
        return Task.FromResult(ResumenAEntregar ?? Resultado.Ok(new ResumenExportacionDto(rodeoId, 20, 12, 8, null, null)));
    }

    public async Task<Resultado<ExportacionEntregadaDto>> ExportarAsync(Guid rodeoId, FormatoExportacion formato, CancellationToken ct = default)
    {
        Exportaciones.Add((rodeoId, formato));
        if (_bloqueo is { } bloqueo)
        {
            using var registro = ct.Register(() => bloqueo.TrySetCanceled(ct));
            await bloqueo.Task;
            _bloqueo = null;
        }
        ct.ThrowIfCancellationRequested();
        if (ExcepcionAlExportar is not null) throw ExcepcionAlExportar;
        return ResultadoExportar();
    }
}

/// <summary>La entrega de archivos de prueba para la pantalla: solo anota qué se compartió.</summary>
public sealed class FakeEntregaArchivoPantalla : IEntregaArchivo
{
    public List<ArchivoGuardado> Compartidos { get; } = [];
    public Exception? ExcepcionAlCompartir { get; set; }

    public Task<ResultadoGuardadoArchivo> GuardarAsync(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct = default)
        => throw new NotSupportedException("La pantalla no guarda: lo hace el servicio.");

    public Task CompartirAsync(ArchivoGuardado archivo, CancellationToken ct = default)
    {
        if (ExcepcionAlCompartir is not null) throw ExcepcionAlCompartir;
        Compartidos.Add(archivo);
        return Task.CompletedTask;
    }
}
