using Aviss.Infrastructure.Sistema;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aviss.Infrastructure.Tests;

public class EspacioLibreProviderTests
{
    [Fact]
    public void En_un_directorio_temporal_devuelve_un_valor_positivo()
    {
        var bytes = new EspacioLibreProvider().BytesLibres(Path.GetTempPath());
        Assert.True(bytes > 0);
    }

    [Fact]
    public void Si_la_plataforma_registro_su_proveedor_antes_Infrastructure_no_lo_pisa()
    {
        // En Android la app registra su proveedor (StatFs) ANTES de AddAvissInfrastructure; el de Infrastructure es el de respaldo.
        var propio = new FakeEspacioLibre();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IEspacioLibreProvider>(propio);
        servicios.AddAvissInfrastructure(new RutasAviss(Path.Combine(Path.GetTempPath(), "aviss-test-espacio")));

        using var proveedor = servicios.BuildServiceProvider();
        Assert.Same(propio, proveedor.GetRequiredService<IEspacioLibreProvider>());
    }

    [Fact]
    public void Sin_proveedor_de_plataforma_se_usa_el_de_Infrastructure()
    {
        var servicios = new ServiceCollection();
        servicios.AddAvissInfrastructure(new RutasAviss(Path.Combine(Path.GetTempPath(), "aviss-test-espacio")));

        using var proveedor = servicios.BuildServiceProvider();
        Assert.IsType<EspacioLibreProvider>(proveedor.GetRequiredService<IEspacioLibreProvider>());
    }
}
