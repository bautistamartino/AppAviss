using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Puertos;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Franja "Rechazo" de la ficha: solo se ve si la vaca tiene un rechazo vigente (antes se veía siempre).
public class TrabajoViewModelRechazoVigenteTests
{
    private sealed class RelojFijo : IRelojService
    {
        public DateTime UtcAhora => new(2026, 11, 17, 13, 10, 0, DateTimeKind.Utc);
    }

    private static TrabajoViewModel Crear(FakeCampoService campo)
    {
        var sesion = new SesionTrabajo();
        sesion.Empezar(Datos.Vet, Datos.Rodeo());
        var lector = new FakeLector();
        return new TrabajoViewModel(campo, lector, new FakePreferencias { ControlPorMangaPorDefecto = true }, new FakeAvisos(),
            new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), sesion, lector, new RelojFijo());
    }

    private static void Vaca(FakeCampoService campo, string caravana, RechazoVigenteDto? rechazo)
    {
        var animal = Guid.NewGuid();
        campo.Lecturas[caravana] = new ResultadoLecturaDto(TipoResultadoLectura.Marcada, caravana, animal, caravana,
            Datos.Ficha(animal, caravana, cvet: "PRE", rechazoVigente: rechazo), Guid.NewGuid(), 1, 1, null, null, false);
    }

    private static RechazoVigenteDto Rechazo(string? motivo) => new(Guid.NewGuid(), motivo, DateTime.UtcNow);

    [Fact]
    public async Task Sin_rechazo_vigente_la_franja_no_se_ve()
    {
        var campo = new FakeCampoService();
        Vaca(campo, "1001", null);
        var vm = Crear(campo);
        await vm.IniciarAsync();
        await vm.EncolarLecturaAsync("1001");

        Assert.True(vm.HayFicha);
        Assert.False(vm.HayRechazoVigente);
        Assert.Equal("", vm.TextoRechazoVigente);
    }

    [Fact]
    public async Task Con_rechazo_con_motivo_muestra_Rechazo_y_el_motivo()
    {
        var campo = new FakeCampoService();
        Vaca(campo, "1001", Rechazo("Cojera"));
        var vm = Crear(campo);
        await vm.IniciarAsync();
        await vm.EncolarLecturaAsync("1001");

        Assert.True(vm.HayRechazoVigente);
        Assert.Equal("Rechazo: Cojera", vm.TextoRechazoVigente);
    }

    [Fact]
    public async Task Con_rechazo_sin_motivo_muestra_solo_Rechazo()
    {
        var campo = new FakeCampoService();
        Vaca(campo, "1001", Rechazo(null));
        Vaca(campo, "1002", Rechazo("  "));
        var vm = Crear(campo);
        await vm.IniciarAsync();
        await vm.EncolarLecturaAsync("1001");

        Assert.True(vm.HayRechazoVigente);
        Assert.Equal("Rechazo", vm.TextoRechazoVigente);

        vm.Ficha = vm.Ficha! with { RechazoVigente = Rechazo("  ") };
        Assert.Equal("Rechazo", vm.TextoRechazoVigente);
    }

    [Fact]
    public async Task Al_cambiar_de_ficha_la_franja_se_actualiza_y_avisa_el_cambio()
    {
        var campo = new FakeCampoService();
        Vaca(campo, "1001", Rechazo("Cojera"));
        var vm = Crear(campo);
        await vm.IniciarAsync();
        var avisados = new List<string?>();
        vm.PropertyChanged += (_, e) => avisados.Add(e.PropertyName);
        await vm.EncolarLecturaAsync("1001");
        Assert.True(vm.HayRechazoVigente);
        Assert.Contains(nameof(TrabajoViewModel.HayRechazoVigente), avisados);
        Assert.Contains(nameof(TrabajoViewModel.TextoRechazoVigente), avisados);

        vm.Ficha = vm.Ficha! with { RechazoVigente = null };
        Assert.False(vm.HayRechazoVigente);
        Assert.Equal("", vm.TextoRechazoVigente);
    }
}
