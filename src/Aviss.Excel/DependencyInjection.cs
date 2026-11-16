using Aviss.Application.Importacion;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Excel;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el perfil de Dairy Comp como el formato de importación. No guarda estado, así que es singleton.
    /// Para soportar otro sistema se registra otro <see cref="IExcelProfile"/>; el resto de la importación no cambia (RF-25).
    /// </summary>
    public static IServiceCollection AddAvissExcel(this IServiceCollection servicios)
    {
        servicios.AddSingleton<IExcelProfile, PerfilDairyComp>();
        return servicios;
    }
}
