using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Arma registros exportables de prueba con valores por defecto razonables.</summary>
internal static class R
{
    public static DateTime Utc(int a, int m, int d, int h = 12, int min = 0)
        => new(a, m, d, h, min, 0, DateTimeKind.Utc);

    public static RegistroExportable Reg(
        string caravana = "100", DateTime? fecha = null, long orden = 1, string evento = "OK", TipoRegistroExportable tipo = TipoRegistroExportable.Tacto,
        int? diasPreniez = null, int? diasAborto = null, ResultadoAborto? resultado = null)
        => new(caravana, tipo, fecha ?? Utc(2026, 11, 5), orden, evento, diasPreniez, diasAborto, resultado);
}
