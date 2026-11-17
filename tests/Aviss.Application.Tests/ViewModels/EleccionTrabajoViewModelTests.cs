using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Entidades;

namespace Aviss.Application.Tests.ViewModels;

public class EleccionTrabajoViewModelTests
{
    private static readonly VeterinarioDto Ana = new(Guid.Parse("22222222-0000-0000-0000-000000000001"), "Dra. Ana", "M-1");
    private static readonly VeterinarioDto Luis = new(Guid.Parse("22222222-0000-0000-0000-000000000002"), "Dr. Luis", null);

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakePreferencias Preferencias { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public EleccionTrabajoViewModel Vm { get; }

        public Banco()
        {
            Campo.Veterinarios = [Ana, Luis];
            Vm = new EleccionTrabajoViewModel(Campo, Preferencias, Navegacion, Sesion);
        }
    }

    [Fact]
    public async Task El_veterinario_recordado_queda_preseleccionado()
    {
        var b = new Banco();
        b.Preferencias.Recordado = Luis.Id;

        await b.Vm.CargarAsync();

        Assert.Equal(Luis, b.Vm.VeterinarioSeleccionado);
        Assert.False(b.Vm.RecordadoNoDisponible);
    }

    [Fact]
    public async Task Si_el_recordado_ya_no_esta_activo_se_pide_elegir_otro()
    {
        var b = new Banco();
        b.Preferencias.Recordado = Guid.NewGuid();   // dado de baja: no está en la lista de activos

        await b.Vm.CargarAsync();

        Assert.Null(b.Vm.VeterinarioSeleccionado);
        Assert.True(b.Vm.RecordadoNoDisponible);
        Assert.False(b.Vm.PuedeEmpezar);
    }

    [Fact]
    public async Task Sin_recordado_no_hay_nada_preseleccionado_ni_aviso()
    {
        var b = new Banco();

        await b.Vm.CargarAsync();

        Assert.Null(b.Vm.VeterinarioSeleccionado);
        Assert.False(b.Vm.RecordadoNoDisponible);
    }

    [Fact]
    public async Task Sin_asignar_no_se_ofrece_para_trabajar()
    {
        var b = new Banco();
        b.Campo.Veterinarios = [new VeterinarioDto(Usuario.SinAsignarId, Usuario.NombreSinAsignar, null), Ana];

        await b.Vm.CargarAsync();

        Assert.Equal([Ana], b.Vm.Veterinarios);
    }

    [Fact]
    public async Task Crear_un_veterinario_lo_suma_y_lo_deja_elegido()
    {
        var b = new Banco();
        await b.Vm.CargarAsync();
        b.Vm.NuevoNombre = "  Dra. Marta ";
        b.Vm.NuevaMatricula = "M-9";

        await b.Vm.CrearVeterinarioCommand.ExecuteAsync(null);

        Assert.Equal(("Dra. Marta", "M-9"), b.Campo.VeterinariosCreados.Single());
        Assert.Equal("Dra. Marta", b.Vm.VeterinarioSeleccionado!.Nombre);
        Assert.Equal(3, b.Vm.Veterinarios.Count);
        Assert.Equal("", b.Vm.NuevoNombre);
        Assert.False(b.Vm.HayMensajeError);
    }

    [Fact]
    public async Task Crear_con_un_nombre_repetido_muestra_el_error_y_no_elige_a_nadie()
    {
        var b = new Banco();
        b.Campo.OnCrearVeterinario = _ => Resultado.Error<Guid>(CodigoError.Duplicado, "Ya existe un veterinario con ese nombre.");
        await b.Vm.CargarAsync();
        b.Vm.NuevoNombre = "dra. ana";

        await b.Vm.CrearVeterinarioCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un veterinario con ese nombre.", b.Vm.MensajeError);
        Assert.Null(b.Vm.VeterinarioSeleccionado);
        Assert.Equal(2, b.Vm.Veterinarios.Count);
    }

    [Fact]
    public async Task Crear_sin_nombre_pide_el_nombre_y_no_llama_al_servicio()
    {
        var b = new Banco();

        await b.Vm.CrearVeterinarioCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayMensajeError);
        Assert.Empty(b.Campo.VeterinariosCreados);
    }

    [Fact]
    public async Task Sin_matricula_se_manda_nula()
    {
        var b = new Banco();
        b.Vm.NuevoNombre = "Dr. Pablo";
        b.Vm.NuevaMatricula = "  ";

        await b.Vm.CrearVeterinarioCommand.ExecuteAsync(null);

        Assert.Null(b.Campo.VeterinariosCreados.Single().Matricula);
    }

    [Fact]
    public async Task Sin_veterinario_o_sin_rodeo_no_empieza()
    {
        var b = new Banco();
        var rodeo = Datos.Rodeo();
        b.Campo.Rodeos = [rodeo];
        await b.Vm.CargarAsync();   // un solo rodeo: queda elegido, pero no hay veterinario

        Assert.False(b.Vm.PuedeEmpezar);
        await b.Vm.EmpezarCommand.ExecuteAsync(null);

        Assert.Empty(b.Navegacion.Destinos);
        Assert.Null(b.Sesion.Rodeo);
        Assert.Equal(0, b.Preferencias.Guardados);
    }

    [Fact]
    public async Task Empezar_guarda_la_preferencia_llena_la_sesion_y_navega_al_trabajo()
    {
        var b = new Banco();
        var rodeo = Datos.Rodeo();
        b.Campo.Rodeos = [rodeo];
        b.Preferencias.Recordado = Ana.Id;
        await b.Vm.CargarAsync();

        Assert.True(b.Vm.PuedeEmpezar);
        await b.Vm.EmpezarCommand.ExecuteAsync(null);

        Assert.Equal(Ana.Id, b.Preferencias.Recordado);
        Assert.Equal(1, b.Preferencias.Guardados);
        Assert.Equal(Ana, b.Sesion.Veterinario);
        Assert.Equal(rodeo, b.Sesion.Rodeo);
        Assert.Equal(["Trabajo"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Los_rodeos_se_muestran_por_establecimiento_con_su_avance()
    {
        var b = new Banco();
        var norte = Guid.NewGuid();
        var sur = Guid.NewGuid();
        b.Campo.Rodeos =
        [
            Datos.Rodeo("Tambo Norte", "Rodeo A", norte, Guid.NewGuid()),
            Datos.Rodeo("Tambo Norte", "Rodeo B", norte, Guid.NewGuid()),
            Datos.Rodeo("Tambo Sur", "Rodeo C", sur, Guid.NewGuid())
        ];
        await b.Vm.CargarAsync();

        Assert.Equal(2, b.Vm.Establecimientos.Count);
        Assert.Empty(b.Vm.Rodeos);   // con dos establecimientos no se elige solo

        b.Vm.EstablecimientoSeleccionado = b.Vm.Establecimientos.First(e => e.Nombre == "Tambo Norte");

        Assert.Equal(["Rodeo A", "Rodeo B"], b.Vm.Rodeos.Select(r => r.RodeoNombre));
        Assert.Null(b.Vm.RodeoSeleccionado);
        Assert.Equal(100, b.Vm.Rodeos[0].Avance.TotalVacas);
    }

    [Fact]
    public async Task Sin_rodeos_importados_lo_indica_y_ofrece_ir_a_importar()
    {
        var b = new Banco();

        await b.Vm.CargarAsync();
        Assert.True(b.Vm.NoHayRodeos);

        await b.Vm.IrAImportarCommand.ExecuteAsync(null);
        Assert.Equal(["Importar"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Una_falla_al_cargar_muestra_un_mensaje_fijo()
    {
        var b = new Banco();
        var vm = new EleccionTrabajoViewModel(new FalloCampo(), b.Preferencias, b.Navegacion, b.Sesion);

        await vm.CargarAsync();

        Assert.Equal(EleccionTrabajoViewModel.MensajeFalla, vm.MensajeError);
    }

    // La fachada que siempre falla (disco, base): el ViewModel no debe caerse.
    private sealed class FalloCampo : FakeCampoService
    {
        public override Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosActivosAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("falla");
    }
}
