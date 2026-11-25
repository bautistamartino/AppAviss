using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>
/// Spec 009 (modificada 2026-11-25): con CUALQUIER ficha abierta, una lectura del bastón o del simulador no hace nada con el
/// animal (no abre otra ficha, no marca, no suena, no cuenta, no pregunta): solo muestra un mensaje corto. La búsqueda a mano
/// y los toques explícitos no cambian.
/// </summary>
public class TrabajoViewModelLecturaIgnoradaTests
{
    private const string Mensaje = "Terminá con esta vaca antes de leer otra";

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeAvisos Avisos { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            var sesion = new SesionTrabajo();
            sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [new EventoCampoDto(Guid.NewGuid(), "PREÑADA", true, true)];
            var lector = new FakeLector();
            Vm = new TrabajoViewModel(Campo, lector, new FakePreferencias { ControlPorMangaPorDefecto = true }, Avisos, new FakePantalla(),
                new FakeTemporizador(), new FakeNavegacion(), sesion, lector);
        }

        public Guid Vaca(string caravana, string cvet = "PRE")
        {
            var animal = Guid.NewGuid();
            Campo.Lecturas[caravana] = Datos.Marcada(animal, caravana, contador: 1) with { Ficha = Datos.Ficha(animal, caravana, cvet: cvet) };
            Campo.Fichas[animal] = Datos.Ficha(animal, caravana, cvet: cvet);
            return animal;
        }

        /// <summary>Una vaca de CVET 0 (la lectura la marca pero no abre ficha), con su ficha disponible para "Ver ficha".</summary>
        public Guid VacaCvetCero(string caravana, int contador = 2)
        {
            var animal = Guid.NewGuid();
            Campo.Fichas[animal] = Datos.Ficha(animal, caravana, cvet: "0", entraEnTacto: false);
            Campo.Lecturas[caravana] = Datos.SinTacto(animal, caravana, contador: contador);
            return animal;
        }

        public async Task<Guid> FichaAbiertaAsync(string caravana = "1001")
        {
            var animal = Vaca(caravana);
            Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 1, procesadas: 1);
            await Vm.EncolarLecturaAsync(caravana);   // sin ficha abierta, el bastón marca y abre la ficha como siempre
            return animal;
        }
    }

    [Fact]
    public async Task Con_ficha_de_una_vaca_que_entra_en_tacto_una_lectura_del_baston_no_hace_nada_y_avisa()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync();
        b.Vaca("1002");
        var procesadas = b.Campo.LecturasProcesadas.Count;
        var confirmaciones = b.Avisos.Confirmaciones;

        await b.Vm.EncolarLecturaAsync("1002");

        Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);   // ni siquiera se le pregunta al servicio
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);              // sin aviso de tres opciones
        Assert.Equal(Mensaje, b.Vm.MensajeInfo);
        Assert.True(b.Vm.HayMensajeInfo);
        Assert.DoesNotContain("1002", b.Vm.MensajeInfo);               // sin datos del animal leído
        Assert.Equal(confirmaciones, b.Avisos.Confirmaciones);
        Assert.Equal(0, b.Avisos.Repetidos);
        Assert.Equal(0, b.Avisos.SinTactos);
        Assert.Equal(1, b.Vm.Contador);
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.EsperasPuestas);
    }

    [Fact]
    public async Task Con_ficha_de_CVET_0_abierta_una_lectura_del_baston_tampoco_hace_nada()
    {
        var b = new Banco();
        var cero = b.VacaCvetCero("2224");
        await b.Vm.EncolarLecturaAsync("2224");              // sin ficha: se marca y queda el "Ver ficha"
        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);
        Assert.Equal(cero, b.Vm.Ficha!.AnimalId);
        b.Vaca("1002");
        var procesadas = b.Campo.LecturasProcesadas.Count;

        await b.Vm.EncolarLecturaAsync("1002");

        Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);
        Assert.Equal(cero, b.Vm.Ficha!.AnimalId);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(Mensaje, b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Leer_una_vaca_CVET_0_con_ficha_abierta_ya_no_la_marca_ni_suena_la_chicharra()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync();
        b.VacaCvetCero("2224");
        var procesadas = b.Campo.LecturasProcesadas.Count;

        await b.Vm.EncolarLecturaAsync("2224");

        Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);   // no se pidió ninguna marca
        Assert.Equal(0, b.Avisos.SinTactos);
        Assert.Equal(1, b.Vm.Contador);
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Equal(Mensaje, b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Leer_la_misma_vaca_de_la_ficha_se_ignora_sin_mensaje()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync("1001");

        await b.Vm.EncolarLecturaAsync("1001");   // el bastón repite la lectura de la vaca que se está tactando

        Assert.Null(b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Sin_ficha_abierta_la_lectura_sigue_igual_marca_y_abre_la_ficha()
    {
        var b = new Banco();
        var animal = b.Vaca("1001");

        await b.Vm.EncolarLecturaAsync("1001");

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal(1, b.Avisos.Confirmaciones);
        Assert.Null(b.Vm.MensajeInfo);
        Assert.False(b.Campo.LecturasProcesadas[0].HayFichaConTactoAbierta);
    }

    [Fact]
    public async Task Sin_ficha_abierta_una_vaca_CVET_0_se_marca_y_suena_como_hoy()
    {
        var b = new Banco();
        b.VacaCvetCero("2224");

        await b.Vm.EncolarLecturaAsync("2224");

        Assert.Equal(1, b.Avisos.SinTactos);
        Assert.Equal("2224: no entra en tacto (CVET 0)", b.Vm.UltimoGuardado);
        Assert.Null(b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task La_busqueda_a_mano_con_ficha_abierta_sigue_funcionando_como_hoy()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync();
        b.Vaca("1002");

        b.Vm.TextoBusqueda = "1002";
        await b.Vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);   // el aviso de tres opciones se dispara solo desde acá
        Assert.True(b.Campo.LecturasProcesadas[^1].HayFichaConTactoAbierta);
        Assert.NotEqual(Mensaje, b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Con_un_cuadro_abierto_la_lectura_se_guarda_y_al_cerrarlo_se_ignora_con_el_mensaje()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync();
        b.Vaca("1002");
        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);   // cuadro que frena las lecturas
        Assert.Equal(CuadroTrabajo.CantidadManga, b.Vm.Cuadro);
        var procesadas = b.Campo.LecturasProcesadas.Count;

        await b.Vm.EncolarLecturaAsync("1002");
        Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);

        await b.Vm.CerrarCantidadMangaCommand.ExecuteAsync(null);

        Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Equal(Mensaje, b.Vm.MensajeInfo);
    }
}
