using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>
/// Spec 009, RF-15: "Ver ficha" de una vaca con CVET 0 abre la ficha COMPLETA (todos los datos del Excel, Evento, Observación, Saltar,
/// En espera y Entrar) con un cartel informativo. Caso de referencia: la vaca 1019 del rodeo BAUTI1, cuya ficha "no mostraba ningún dato".
/// </summary>
public class TrabajoViewModelCvetCeroTests
{
    private static readonly EventoCampoDto Prenada = new(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA", true, true);

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeLector Lector { get; } = new();
        public FakeAvisos Avisos { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            var sesion = new SesionTrabajo();
            sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [Prenada];
            var prefs = new FakePreferencias { ControlPorMangaPorDefecto = true };
            Vm = new TrabajoViewModel(Campo, Lector, prefs, Avisos, new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), sesion, Lector);
        }

        /// <summary>
        /// La vaca 1019 como la arma el servicio real: la ficha sale de <see cref="ConstructorFicha"/> con los datos originales del Excel,
        /// y leerla da "SinTacto" (CVET 0: se marca y no abre ficha).
        /// </summary>
        public Guid Vaca1019()
        {
            var animal = Guid.NewGuid();
            var armada = ConstructorFicha.Armar(ConstructorFichaTests.Json1019, [], new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);
            Campo.Fichas[animal] = new FichaAnimalDto(animal, "1019", null, null, "PREÑADA", null, armada.Campos, armada.Inseminaciones,
                armada.UltimoToro, armada.Historial, armada.Cvet, ReglasCvet.EntraEnTacto(armada.Cvet), armada.Extras, false, armada.RechazoVigente);
            Campo.Lecturas["1019"] = Datos.SinTacto(animal, "1019", contador: 5);
            Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 5);
            return animal;
        }

        public async Task<Guid> AbrirFichaDe1019Async()
        {
            var animal = Vaca1019();
            await Vm.EncolarLecturaAsync("1019");
            await Vm.VerFichaSinTactoCommand.ExecuteAsync(null);
            return animal;
        }
    }

    // ------------------------------------------------------------------ el problema de la vaca 1019

    [Fact]
    public async Task Ver_la_ficha_de_la_1019_muestra_todos_sus_datos_en_la_pestana_Datos()
    {
        var b = new Banco();

        var animal = await b.AbrirFichaDe1019Async();

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        var filas = b.Vm.FilasDatos.ToDictionary(f => f.Nombre, f => f.Valor);
        Assert.Equal("PREÑADA", filas["Estado"]);
        Assert.Equal("2", filas["LACT"]);
        Assert.Equal("211", filas["DEL"]);
        Assert.Equal("23/04/26", filas["FPART"]);
        Assert.Equal("10/07/26", filas["FCELO"]);
        Assert.Equal("WINDSTAR", filas["UTORO"]);
        Assert.Equal("133", filas["DDUC"]);
        Assert.Equal("133", filas["DCC"]);
        Assert.Equal("PREÑADA", filas["RPRO"]);
        Assert.Equal("18/04/27", filas["FAPAR"]);
        Assert.Equal("0", filas["LECHE"]);
    }

    [Fact]
    public async Task La_ficha_de_CVET_0_muestra_el_cartel_informativo_y_no_el_bloqueo()
    {
        var b = new Banco();
        await b.AbrirFichaDe1019Async();

        Assert.True(b.Vm.MostrarCartelCvetCero);
        Assert.Equal("CVET 0: no estaba en la lista de tacto", b.Vm.TextoCartelCvetCero);
        Assert.False(b.Vm.HayMotivoTacto);   // sin "Motivo del tacto": no hay un CVET que lo explique
        Assert.Equal("ID 1019", b.Vm.TituloFicha);
    }

    [Fact]
    public async Task Una_ficha_que_entra_en_tacto_no_muestra_el_cartel_de_CVET_0()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["9"] = Datos.Marcada(animal, "9") with { Ficha = Datos.Ficha(animal, "9", cvet: "PRE") };

        await b.Vm.EncolarLecturaAsync("9");

        Assert.False(b.Vm.MostrarCartelCvetCero);
    }

    // ------------------------------------------------------------------ se puede tactar y poner en espera

    [Fact]
    public async Task En_la_ficha_de_CVET_0_se_puede_elegir_evento_y_observacion_y_Entrar_guarda_el_tacto_sin_sumar_al_conteo()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaDe1019Async();

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        b.Vm.ObservacionPendiente = "se tacta igual";
        Assert.True(b.Vm.EntrarCommand.CanExecute(null));

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var tacto = Assert.Single(b.Campo.Tactos);
        Assert.Equal(animal, tacto.AnimalId);
        Assert.Equal("se tacta igual", tacto.Observaciones);
        Assert.False(b.Vm.HayFicha);
        Assert.Equal(["1019"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));   // no se volvió a leer ni marcar
        Assert.Equal(5, b.Vm.Contador);                                                // no suma al conteo de la manga
    }

    [Fact]
    public async Task En_la_ficha_de_CVET_0_se_puede_abrir_la_observacion()
    {
        var b = new Banco();
        await b.AbrirFichaDe1019Async();

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Observacion, b.Vm.Cuadro);
    }

    [Fact]
    public async Task En_la_ficha_de_CVET_0_el_boton_En_espera_funciona_y_deja_la_vaca_en_la_botonera()
    {
        var b = new Banco();
        var animal = await b.AbrirFichaDe1019Async();
        Assert.True(b.Vm.PuedePonerEnEspera);
        b.Campo.Esperas = [Datos.Espera("1019", animal, cvet: "0")];

        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);

        Assert.Equal(animal, Assert.Single(b.Campo.EsperasPuestas).AnimalId);
        Assert.False(b.Vm.HayFicha);
        Assert.Equal(["1019"], b.Vm.Esperas.Select(e => e.Caravana));
    }

    [Fact]
    public async Task Saltar_cierra_la_ficha_de_CVET_0_y_vuelve_el_Ultimo_con_la_opcion_de_verla()
    {
        var b = new Banco();
        await b.AbrirFichaDe1019Async();

        b.Vm.CerrarFichaCommand.Execute(null);

        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.True(b.Vm.HayVerFichaSinTacto);
    }

    [Fact]
    public async Task Una_ficha_de_CVET_0_que_se_rechaza_al_guardar_queda_abierta_con_el_motivo()
    {
        var b = new Banco();
        await b.AbrirFichaDe1019Async();
        b.Campo.OnRegistrarTacto = _ => Resultado.Error<Guid>(CodigoError.FechaAnteriorAlUltimoRegistro, "Revisá la hora del dispositivo.");

        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.Equal("Revisá la hora del dispositivo.", b.Vm.MensajeError);
    }
}
