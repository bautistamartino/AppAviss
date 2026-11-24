using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Excel.Exportacion;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// El exportador de prueba (spec 007): por defecto delega en el real (<see cref="ExportadorDairyComp"/>) y cuenta las llamadas; un
/// test puede imponerle un resultado de generación o de validación para probar los caminos de error del servicio.
/// </summary>
public sealed class FakeExportadorArchivo : IExportadorArchivo
{
    private readonly ExportadorDairyComp _real = new();

    public ResultadoGeneracionArchivo? ResultadoGenerar { get; set; }
    public ResultadoValidacionArchivo? ResultadoValidar { get; set; }
    public Exception? ExcepcionAlGenerar { get; set; }
    public int Generaciones { get; private set; }
    public int Validaciones { get; private set; }

    public ResultadoGeneracionArchivo Generar(byte[] original, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        Generaciones++;
        if (ExcepcionAlGenerar is not null) throw ExcepcionAlGenerar;
        return ResultadoGenerar ?? _real.Generar(original, formato, valoresPorCaravana);
    }

    public ResultadoValidacionArchivo Validar(byte[] original, byte[] generado, FormatoExportacion formato, IReadOnlyDictionary<string, ValoresExportacion> valoresPorCaravana)
    {
        Validaciones++;
        return ResultadoValidar ?? _real.Validar(original, generado, formato, valoresPorCaravana);
    }
}
