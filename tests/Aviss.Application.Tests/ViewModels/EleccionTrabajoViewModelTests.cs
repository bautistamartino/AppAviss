using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Pantalla "Elegir con qué trabajar": solo establecimiento y rodeo (spec 006, RF-1, RF-2, RF-3).
public class EleccionTrabajoViewModelTests
{
    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public EleccionTrabajoViewModel Vm { get; }

        public Banco() => Vm = new EleccionTrabajoViewModel(Campo, Navegacion, Sesion);
    }

    // ------------------------------------------------------------------ RF-1: sin veterinario

    [Fact]
    public void La_pantalla_no_tiene_lista_de_veterinarios_ni_formulario_de_alta()
    {
        // Ni miembros públicos (propiedades, comandos) que mencionen al veterinario, ni constructor que pida las preferencias.
        var nombres = typeof(EleccionTrabajoViewModel).GetMembers().Select(m => m.Name);
        Assert.DoesNotContain(nombres, n => n.Contains("Veterinario", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nombres, n => n.Contains("Matricula", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nombres, n => n.Contains("FormularioNuevo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void La_sesion_no_tiene_veterinario_ni_para_cambiarlo()
    {
        var nombres = typeof(SesionTrabajo).GetMembers().Select(m => m.Name);
        Assert.DoesNotContain(nombres, n => n.Contains("Veterinario", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ RF-2: Empezar con solo el rodeo

    [Fact]
    public async Task Con_un_rodeo_elegido_se_puede_empezar_sin_pedir_nada_mas()
    {
        var b = new Banco();
        b.Campo.Rodeos = [Datos.Rodeo()];

        await b.Vm.CargarAsync();   // un solo rodeo: queda elegido solo

        Assert.True(b.Vm.PuedeEmpezar);
        Assert.False(b.Vm.FaltaAlgo);
        Assert.Equal("", b.Vm.QueFalta);
        Assert.True(b.Vm.EmpezarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sin_rodeo_no_empieza()
    {
        var b = new Banco();
        b.Campo.Rodeos = [Datos.Rodeo(), Datos.Rodeo()];   // dos rodeos del mismo establecimiento: no se elige solo
        await b.Vm.CargarAsync();

        Assert.False(b.Vm.PuedeEmpezar);
        await b.Vm.EmpezarCommand.ExecuteAsync(null);

        Assert.Empty(b.Navegacion.Destinos);
        Assert.Null(b.Sesion.Rodeo);
        Assert.False(b.Sesion.EstaLista);
    }

    [Fact]
    public async Task Empezar_llena_la_sesion_con_el_rodeo_y_navega_al_trabajo()
    {
        var b = new Banco();
        var rodeo = Datos.Rodeo();
        b.Campo.Rodeos = [rodeo];
        await b.Vm.CargarAsync();

        await b.Vm.EmpezarCommand.ExecuteAsync(null);

        Assert.Equal(rodeo, b.Sesion.Rodeo);
        Assert.True(b.Sesion.EstaLista);
        Assert.Equal(["Trabajo"], b.Navegacion.Destinos);
    }

    [Fact]
    public async Task Que_falta_solo_habla_del_establecimiento_y_del_rodeo()
    {
        var b = new Banco();
        var norte = Guid.NewGuid();
        var sur = Guid.NewGuid();
        b.Campo.Rodeos = [Datos.Rodeo("Tambo Norte", "A", norte, Guid.NewGuid()), Datos.Rodeo("Tambo Sur", "B", sur, Guid.NewGuid())];
        await b.Vm.CargarAsync();

        // Sin establecimiento ni rodeo.
        Assert.True(b.Vm.FaltaAlgo);
        Assert.Equal("Falta elegir un establecimiento y un rodeo", b.Vm.QueFalta);

        // Con establecimiento de dos rodeos, falta el rodeo.
        b.Campo.Rodeos = [Datos.Rodeo("Tambo Norte", "A", norte, Guid.NewGuid()), Datos.Rodeo("Tambo Norte", "B", norte, Guid.NewGuid())];
        await b.Vm.CargarAsync();
        Assert.Equal("Falta elegir un rodeo", b.Vm.QueFalta);
        Assert.DoesNotContain("veterinario", b.Vm.QueFalta, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ RF-3: no recuerda nada de quién trabaja

    [Fact]
    public async Task Empezar_no_guarda_nada_en_las_preferencias_y_la_pantalla_no_las_recibe()
    {
        // El constructor ya no recibe preferencias (no hay nada que recordar); la sesión queda sin autor.
        var b = new Banco();
        b.Campo.Rodeos = [Datos.Rodeo()];
        await b.Vm.CargarAsync();
        await b.Vm.EmpezarCommand.ExecuteAsync(null);

        Assert.Equal(1, typeof(EleccionTrabajoViewModel).GetConstructors().Single().GetParameters()
            .Count(p => p.ParameterType == typeof(SesionTrabajo)));
        Assert.DoesNotContain(typeof(EleccionTrabajoViewModel).GetConstructors().Single().GetParameters(),
            p => p.ParameterType.Name.Contains("Preferencias"));
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
    public async Task Volver_al_inicio_navega_sin_empezar_a_trabajar()
    {
        var b = new Banco();
        await b.Vm.CargarAsync();

        await b.Vm.VolverAlInicioCommand.ExecuteAsync(null);

        Assert.Equal(["Inicio"], b.Navegacion.Destinos);
        Assert.False(b.Sesion.EstaLista);
    }

    [Fact]
    public async Task Una_falla_al_cargar_muestra_un_mensaje_fijo()
    {
        var b = new Banco();
        var vm = new EleccionTrabajoViewModel(new FalloCampo(), b.Navegacion, b.Sesion);

        await vm.CargarAsync();

        Assert.Equal(EleccionTrabajoViewModel.MensajeFalla, vm.MensajeError);
    }


    // ------------------------------------------------------------------ resetear un rodeo (para pruebas), con doble verificación

    private static async Task<(Banco Banco, RodeoParaTrabajarDto Rodeo)> BancoConUnRodeoAsync()
    {
        var b = new Banco();
        var rodeo = Datos.Rodeo(rodeo: "Rodeo Mayo");
        b.Campo.Rodeos = [rodeo];
        await b.Vm.CargarAsync();
        return (b, rodeo);
    }

    [Fact]
    public async Task Pedir_el_reseteo_abre_la_primera_pregunta_y_no_resetea_nada()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();

        b.Vm.PedirReseteoCommand.Execute(rodeo);

        Assert.True(b.Vm.EsPrimerPasoReseteo);
        Assert.False(b.Vm.EsSegundoPasoReseteo);
        Assert.Equal("Rodeo Mayo", b.Vm.NombreRodeoAResetear);
        Assert.Empty(b.Campo.Reseteados);
    }

    [Fact]
    public async Task La_primera_confirmacion_pasa_a_la_segunda_y_tampoco_resetea()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();
        b.Vm.PedirReseteoCommand.Execute(rodeo);

        b.Vm.ConfirmarPrimerPasoReseteoCommand.Execute(null);

        Assert.True(b.Vm.EsSegundoPasoReseteo);
        Assert.Empty(b.Campo.Reseteados);
    }

    [Fact]
    public async Task Solo_la_segunda_confirmacion_resetea_el_rodeo_y_vuelve_a_leer_la_lista()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();
        var nuevo = Datos.Rodeo(rodeo: "Rodeo Mayo");
        b.Campo.OnResetear = _ => { b.Campo.Rodeos = [nuevo]; return Resultado.Ok(Guid.NewGuid()); };
        b.Vm.PedirReseteoCommand.Execute(rodeo);
        b.Vm.ConfirmarPrimerPasoReseteoCommand.Execute(null);

        await b.Vm.ConfirmarReseteoCommand.ExecuteAsync(null);

        Assert.Equal([rodeo.RodeoId], b.Campo.Reseteados);
        Assert.False(b.Vm.HayReseteo);
        Assert.Equal(0, b.Vm.PasoReseteo);
        Assert.Single(b.Vm.Rodeos.Where(r => r.RodeoId == nuevo.RodeoId));
        Assert.Contains("Rodeo Mayo", b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Confirmar_en_la_primera_pregunta_no_resetea_nada()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();
        b.Vm.PedirReseteoCommand.Execute(rodeo);

        await b.Vm.ConfirmarReseteoCommand.ExecuteAsync(null);   // se saltea la primera confirmación

        Assert.Empty(b.Campo.Reseteados);
        Assert.True(b.Vm.EsPrimerPasoReseteo);
    }

    [Fact]
    public async Task Cancelar_en_cualquier_pregunta_cierra_todo_sin_resetear()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();
        b.Vm.PedirReseteoCommand.Execute(rodeo);
        b.Vm.ConfirmarPrimerPasoReseteoCommand.Execute(null);

        b.Vm.CancelarReseteoCommand.Execute(null);

        Assert.False(b.Vm.HayReseteo);
        Assert.Equal(0, b.Vm.PasoReseteo);
        Assert.Empty(b.Campo.Reseteados);
    }

    [Fact]
    public async Task Si_el_reseteo_falla_se_muestra_el_motivo_y_se_cierra_la_pregunta()
    {
        var (b, rodeo) = await BancoConUnRodeoAsync();
        b.Campo.OnResetear = _ => Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo no existe.");
        b.Vm.PedirReseteoCommand.Execute(rodeo);
        b.Vm.ConfirmarPrimerPasoReseteoCommand.Execute(null);

        await b.Vm.ConfirmarReseteoCommand.ExecuteAsync(null);

        Assert.Equal("El rodeo no existe.", b.Vm.MensajeError);
        Assert.False(b.Vm.HayReseteo);
        Assert.False(b.Vm.HayMensajeInfo);
    }

    // La fachada que siempre falla (disco, base): el ViewModel no debe caerse.
    private sealed class FalloCampo : FakeCampoService
    {
        public override Task<IReadOnlyList<RodeoParaTrabajarDto>> ListarRodeosParaTrabajarAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("falla");
    }
}
