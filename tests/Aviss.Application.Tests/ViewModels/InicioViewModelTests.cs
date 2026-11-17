using Aviss.App.ViewModels;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

public class InicioViewModelTests
{
    [Fact]
    public async Task Trabajar_va_a_la_eleccion_de_veterinario_y_rodeo()
    {
        var nav = new FakeNavegacion();
        var vm = new InicioViewModel(nav);

        await vm.TrabajarCommand.ExecuteAsync(null);

        Assert.Equal(["Eleccion"], nav.Destinos);
    }

    [Fact]
    public async Task Importar_rodeo_sigue_yendo_a_importar()
    {
        var nav = new FakeNavegacion();
        var vm = new InicioViewModel(nav);

        await vm.ImportarRodeoCommand.ExecuteAsync(null);

        Assert.Equal(["Importar"], nav.Destinos);
    }
}
