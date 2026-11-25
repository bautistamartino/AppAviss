using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>
/// Spec 009: con una ficha abierta, la lectura de otra vaca que entra en tacto NO la reemplaza en silencio: abre un aviso con tres
/// opciones (poner en espera la actual y abrir la nueva, cancelar la lectura, saltar la actual y abrir la nueva) y no marca a la
/// nueva hasta que el veterinario elige. Lógica de ViewModel, sin MAUI.
/// </summary>
public class TrabajoViewModelReemplazoTests
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

        /// <summary>Una vaca que entra en tacto: leerla la marca (sin ficha abierta) o, con una ficha abierta, pregunta.</summary>
        public Guid Vaca(string caravana, int contador = 1, string cvet = "PRE")
        {
            var animal = Guid.NewGuid();
            Campo.Lecturas[caravana] = Datos.Marcada(animal, caravana, contador: contador) with { Ficha = Datos.Ficha(animal, caravana, cvet: cvet) };
            Campo.Fichas[animal] = Datos.Ficha(animal, caravana, cvet: cvet);
            return animal;
        }

        /// <summary>Una vaca que está en espera (ficha y lista del servicio).</summary>
        public Guid VacaEnEspera(string caravana)
        {
            var animal = Guid.NewGuid();
            Campo.Fichas[animal] = Datos.Ficha(animal, caravana, cvet: "PRE", enEspera: true);
            Campo.Lecturas[caravana] = Datos.EnEspera(animal, caravana);
            Campo.Esperas = [.. Campo.Esperas, Datos.Espera(caravana, animal)];
            return animal;
        }

        /// <summary>Deja abierta la ficha de la vaca "1001" (marcada) y el contador del servicio en 1.</summary>
        public async Task<Guid> FichaAbiertaAsync(string caravana = "1001")
        {
            var animal = Vaca(caravana, contador: 1);
            Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 1, procesadas: 1);
            await Vm.EncolarLecturaAsync(caravana, esManual: true);
            return animal;
        }

        /// <summary>Abre el aviso: la ficha de "1001" está abierta y se lee "1002".</summary>
        public async Task<(Guid Actual, Guid Nueva)> AvisoAbiertoAsync()
        {
            var actual = await FichaAbiertaAsync("1001");
            var nueva = Vaca("1002", contador: 2);
            await Vm.EncolarLecturaAsync("1002", esManual: true);
            return (actual, nueva);
        }

        /// <summary>Cuando el servicio marca de verdad a la nueva, el contador sube a 2.</summary>
        public void ElServicioSumaAlMarcar() => Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 2, procesadas: 2);
    }

    // ------------------------------------------------------------------ RF-1 y RF-2: el aviso

    [Fact]
    public async Task Con_una_ficha_abierta_leer_otra_vaca_abre_el_aviso_manda_la_bandera_y_no_marca()
    {
        var b = new Banco();

        var (actual, _) = await b.AvisoAbiertoAsync();

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.True(b.Vm.HayConfirmarReemplazo);
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);   // la ficha actual sigue ahí, debajo del aviso
        Assert.Equal(["1001", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.False(b.Campo.LecturasProcesadas[0].HayFichaConTactoAbierta);   // sin ficha abierta no se pregunta
        Assert.True(b.Campo.LecturasProcesadas[1].HayFichaConTactoAbierta);
        Assert.Equal(1, b.Vm.Contador);                  // la nueva NO se marcó: el conteo no se movió
        Assert.Equal(1, b.Avisos.Confirmaciones);        // solo la de la primera marca
        Assert.Equal(1, b.Avisos.Repetidos);             // el aviso suena (o vibra) una vez para que se note
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task El_aviso_trae_la_caravana_actual_y_la_nueva_con_su_CVET()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync("1001");
        b.Vaca("1002", cvet: "PRE");

        await b.Vm.EncolarLecturaAsync("1002", esManual: true);

        Assert.Equal("1001 · CVET PRE", b.Vm.ReemplazoCaravanaActual);
        Assert.Equal("1002 · CVET PRE", b.Vm.ReemplazoCaravanaNueva);
    }

    [Fact]
    public async Task El_aviso_aparece_igual_sin_evento_ni_observacion_y_lo_dice_cuando_hay_algo_sin_guardar()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        Assert.False(b.Vm.HayReemplazoConEventoSinGuardar);   // nada elegido: el aviso aparece igual, sin la advertencia

        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        await b.Vm.EncolarLecturaAsync("1002", esManual: true);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.True(b.Vm.HayReemplazoConEventoSinGuardar);
        Assert.Contains("se perderían", b.Vm.TextoReemplazoAdvertencia);
    }

    [Fact]
    public async Task Una_observacion_sin_guardar_tambien_dispara_la_advertencia()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync();
        b.Vm.ObservacionPendiente = "renguea";
        b.Vaca("1002");

        await b.Vm.EncolarLecturaAsync("1002", esManual: true);

        Assert.True(b.Vm.HayReemplazoConEventoSinGuardar);
    }

    [Fact]
    public async Task La_busqueda_manual_hace_lo_mismo_que_la_lectura()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync();
        b.Vaca("1002");

        b.Vm.TextoBusqueda = "1002";
        await b.Vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.True(b.Campo.LecturasProcesadas[1].HayFichaConTactoAbierta);
    }

    // ------------------------------------------------------------------ RF-3: poner en espera la actual y abrir la nueva

    [Fact]
    public async Task Opcion_1_pone_en_espera_la_actual_marca_la_nueva_y_abre_su_ficha()
    {
        var b = new Banco();
        var (actual, nueva) = await b.AvisoAbiertoAsync();
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);   // el evento elegido en la actual se descarta
        b.Campo.Esperas = [Datos.Espera("1001", actual)];
        b.ElServicioSumaAlMarcar();

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(actual, Assert.Single(b.Campo.EsperasPuestas).AnimalId);
        Assert.Equal(["1001"], b.Vm.Esperas.Select(e => e.Caravana));   // aparece en la botonera
        Assert.Empty(b.Campo.Tactos);
        Assert.Equal(nueva, b.Vm.Ficha!.AnimalId);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Null(b.Vm.EventoElegido);
        Assert.Equal("", b.Vm.ObservacionPendiente);
        // La segunda llamada es la marca de verdad: sin la bandera.
        Assert.Equal(["1001", "1002", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.False(b.Campo.LecturasProcesadas[2].HayFichaConTactoAbierta);
        Assert.Equal(2, b.Vm.Contador);
        Assert.Equal("Marcada: 1002", b.Vm.UltimoGuardado);
    }

    // ------------------------------------------------------------------ RF-4: cancelar la lectura

    [Fact]
    public async Task Opcion_2_cancela_la_lectura_no_marca_y_deja_la_ficha_actual_con_su_evento_y_observacion()
    {
        var b = new Banco();
        var (actual, _) = await b.AvisoAbiertoAsync();
        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);   // (cierra el aviso para poder elegir el evento)
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        b.Vm.ObservacionPendiente = "renguea";
        await b.Vm.EncolarLecturaAsync("1002", esManual: true);
        var procesadasAntes = b.Campo.LecturasProcesadas.Count;
        var confirmacionesAntes = b.Avisos.Confirmaciones;

        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(procesadasAntes, b.Campo.LecturasProcesadas.Count);   // no hay ningún pedido de marca
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Equal(Prenada, b.Vm.EventoElegido);
        Assert.Equal("renguea", b.Vm.ObservacionPendiente);
        Assert.Equal(1, b.Vm.Contador);
        Assert.Equal(confirmacionesAntes, b.Avisos.Confirmaciones);
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);   // "Último guardado" no cambia
        Assert.Equal("Lectura cancelada: no se marcó", b.Vm.MensajeInfo);
        Assert.Empty(b.Campo.EsperasPuestas);
        Assert.Empty(b.Campo.EsperasSalteadas);
    }

    // ------------------------------------------------------------------ RF-5: saltar la actual y abrir la nueva

    [Fact]
    public async Task Opcion_3_salta_la_actual_sin_tacto_ni_espera_marca_la_nueva_y_abre_su_ficha()
    {
        var b = new Banco();
        var (_, nueva) = await b.AvisoAbiertoAsync();
        b.ElServicioSumaAlMarcar();

        await b.Vm.SaltarYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.EsperasPuestas);
        Assert.Empty(b.Campo.EsperasSalteadas);   // la actual no venía de una espera
        Assert.Equal(nueva, b.Vm.Ficha!.AnimalId);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(2, b.Vm.Contador);
        Assert.False(b.Campo.LecturasProcesadas[2].HayFichaConTactoAbierta);
    }

    // ------------------------------------------------------------------ RF-6: la vaca nueva está en espera

    [Fact]
    public async Task Una_vaca_en_espera_leida_con_una_ficha_abierta_pasa_por_el_aviso()
    {
        var b = new Banco();
        await b.FichaAbiertaAsync("1001");
        var enEspera = b.VacaEnEspera("7712");

        await b.Vm.EncolarLecturaAsync("7712", esManual: true);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.NotEqual(enEspera, b.Vm.Ficha!.AnimalId);   // todavía no se abrió
    }

    [Fact]
    public async Task Con_la_nueva_en_espera_las_opciones_1_y_3_solo_abren_su_ficha_sin_marcar_ni_sumar()
    {
        foreach (var opcion in new[] { 1, 3 })
        {
            var b = new Banco();
            var actual = await b.FichaAbiertaAsync("1001");
            var enEspera = b.VacaEnEspera("7712");
            await b.Vm.EncolarLecturaAsync("7712", esManual: true);
            var procesadas = b.Campo.LecturasProcesadas.Count;

            if (opcion == 1) await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);
            else await b.Vm.SaltarYAbrirNuevaCommand.ExecuteAsync(null);

            Assert.Equal(enEspera, b.Vm.Ficha!.AnimalId);
            Assert.True(b.Vm.EsEsperaAbierta);
            Assert.Equal(procesadas, b.Campo.LecturasProcesadas.Count);   // no se pidió ninguna marca
            Assert.Equal(1, b.Vm.Contador);                                // no suma
            Assert.Equal(opcion == 1 ? new[] { actual } : Array.Empty<Guid>(), b.Campo.EsperasPuestas.Select(e => e.AnimalId));
        }
    }

    [Fact]
    public async Task Cancelar_con_la_nueva_en_espera_la_deja_en_espera_sin_tocarla()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync("1001");
        b.VacaEnEspera("7712");
        await b.Vm.EncolarLecturaAsync("7712", esManual: true);

        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);

        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Empty(b.Campo.EsperasSalteadas);
        Assert.Empty(b.Campo.EsperasPuestas);
    }

    [Fact]
    public async Task Sin_ficha_abierta_una_vaca_en_espera_abre_su_ficha_directo_como_antes()
    {
        var b = new Banco();
        var enEspera = b.VacaEnEspera("7712");

        await b.Vm.EncolarLecturaAsync("7712", esManual: true);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(enEspera, b.Vm.Ficha!.AnimalId);
    }

    // ------------------------------------------------------------------ RF-7: la actual viene de una espera

    private static async Task<(Banco Banco, Guid Actual)> BancoConActualEnEsperaYAvisoAsync()
    {
        var b = new Banco();
        var actual = b.VacaEnEspera("7712");
        await b.Vm.EncolarLecturaAsync("7712", esManual: true);   // abre la ficha de la espera (sin ficha previa)
        b.Vaca("1002", contador: 2);
        b.ElServicioSumaAlMarcar();
        await b.Vm.EncolarLecturaAsync("1002", esManual: true);
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        return (b, actual);
    }

    [Fact]
    public async Task Opcion_1_con_la_actual_en_espera_la_deja_pendiente_sin_crear_otra_espera()
    {
        var (b, _) = await BancoConActualEnEsperaYAvisoAsync();

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.EsperasPuestas);
        Assert.Empty(b.Campo.EsperasSalteadas);
        Assert.Equal("1002", b.Vm.Ficha!.Caravana);
    }

    [Fact]
    public async Task Opcion_3_con_la_actual_en_espera_la_saltea()
    {
        var (b, actual) = await BancoConActualEnEsperaYAvisoAsync();

        await b.Vm.SaltarYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(actual, Assert.Single(b.Campo.EsperasSalteadas).AnimalId);
        Assert.Empty(b.Campo.EsperasPuestas);
        Assert.Equal("1002", b.Vm.Ficha!.Caravana);
    }

    // ------------------------------------------------------------------ RF-8: lecturas que no abren el aviso

    [Fact]
    public async Task La_misma_vaca_de_la_ficha_se_ignora_sin_aviso()
    {
        var b = new Banco();
        var animal = await b.FichaAbiertaAsync("1001");
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001");

        await b.Vm.EncolarLecturaAsync("1001", esManual: true);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(0, b.Avisos.Repetidos);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Una_vaca_con_CVET_0_se_marca_suena_la_chicharra_y_la_ficha_abierta_no_se_toca()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        b.Campo.Lecturas["2224"] = Datos.SinTacto(Guid.NewGuid(), "2224", contador: 2);
        b.ElServicioSumaAlMarcar();

        await b.Vm.EncolarLecturaAsync("2224", esManual: true);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);   // no hay aviso de tres opciones
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.Equal(Prenada, b.Vm.EventoElegido);          // el evento cargado sigue ahí
        Assert.Equal(1, b.Avisos.SinTactos);
        Assert.Equal(2, b.Vm.Contador);                     // se marcó y suma al conteo
        Assert.Equal("2224: no entra en tacto (CVET 0)", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task Una_caravana_no_encontrada_deja_la_ficha_abierta_al_cerrar_el_cuadro()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync("1001");

        await b.Vm.EncolarLecturaAsync("9999", esManual: true);
        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        await b.Vm.CerrarNoEncontradaCommand.ExecuteAsync(null);

        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Una_vaca_ya_procesada_muestra_el_aviso_de_repetido_y_Seguir_deja_la_ficha_abierta()
    {
        var b = new Banco();
        var actual = await b.FichaAbiertaAsync("1001");
        b.Campo.Lecturas["2002"] = Datos.Repetido(Guid.NewGuid(), "2002");

        await b.Vm.EncolarLecturaAsync("2002", esManual: true);
        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);   // no es el de tres opciones
        await b.Vm.SeguirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Sin_ficha_abierta_Seguir_sigue_dejando_todo_listo_para_la_proxima_lectura()
    {
        var b = new Banco();
        b.Campo.Lecturas["2002"] = Datos.Repetido(Guid.NewGuid(), "2002");

        await b.Vm.EncolarLecturaAsync("2002", esManual: true);
        await b.Vm.SeguirCommand.ExecuteAsync(null);

        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task Con_la_ficha_de_una_vaca_CVET_0_abierta_leer_otra_que_entra_en_tacto_abre_el_aviso()
    {
        var b = new Banco();
        var cero = Guid.NewGuid();
        b.Campo.Fichas[cero] = Datos.Ficha(cero, "2224", cvet: "0", entraEnTacto: false);
        b.Campo.Lecturas["2224"] = Datos.SinTacto(cero, "2224");
        await b.Vm.EncolarLecturaAsync("2224", esManual: true);
        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);
        Assert.True(b.Vm.MostrarCartelCvetCero);
        b.Vaca("1002");

        await b.Vm.EncolarLecturaAsync("1002", esManual: true);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.True(b.Campo.LecturasProcesadas[^1].HayFichaConTactoAbierta);   // la ficha de CVET 0 cuenta como ficha abierta
        Assert.Equal("2224", b.Vm.ReemplazoCaravanaActual.Split(' ')[0]);
    }

    [Fact]
    public async Task Con_la_ficha_de_CVET_0_abierta_la_opcion_1_la_pone_en_espera_como_a_cualquier_otra()
    {
        var b = new Banco();
        var cero = Guid.NewGuid();
        b.Campo.Fichas[cero] = Datos.Ficha(cero, "2224", cvet: "0", entraEnTacto: false);
        b.Campo.Lecturas["2224"] = Datos.SinTacto(cero, "2224");
        await b.Vm.EncolarLecturaAsync("2224", esManual: true);
        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);
        b.Vaca("1002");
        await b.Vm.EncolarLecturaAsync("1002", esManual: true);

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(cero, Assert.Single(b.Campo.EsperasPuestas).AnimalId);
        Assert.Equal("1002", b.Vm.Ficha!.Caravana);
    }

    // ------------------------------------------------------------------ RF-9: lecturas con el aviso abierto

    [Fact]
    public async Task Con_el_aviso_abierto_se_guarda_solo_la_ultima_y_la_del_mismo_codigo_se_ignora()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.Vaca("1003", contador: 3);
        b.Vaca("1004", contador: 3);

        await b.Vm.EncolarLecturaAsync("1003", esManual: true);
        await b.Vm.EncolarLecturaAsync("1004", esManual: true);
        await b.Vm.EncolarLecturaAsync("1002", esManual: true);   // el bastón repite la del aviso: no reemplaza a la guardada

        Assert.Equal(["1001", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));   // nada se procesó todavía
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);

        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);

        // Al cerrar se procesa la última guardada (1004) con la ficha actual todavía abierta: vuelve a preguntar por ella.
        Assert.Equal(["1001", "1002", "1004"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal("1004", b.Vm.ReemplazoCaravanaNueva.Split(' ')[0]);
        Assert.Equal("1001 · CVET PRE", b.Vm.ReemplazoCaravanaActual);
    }

    [Fact]
    public async Task La_lectura_guardada_se_procesa_con_el_resultado_de_la_opcion_ya_aplicado()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.Vaca("1003", contador: 3);
        await b.Vm.EncolarLecturaAsync("1003", esManual: true);
        b.ElServicioSumaAlMarcar();

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        // Orden: se marcó la 1002 (sin bandera) y recién después se procesó la 1003, con la ficha de la 1002 ya abierta (bandera).
        Assert.Equal(["1001", "1002", "1002", "1003"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.True(b.Campo.LecturasProcesadas[3].HayFichaConTactoAbierta);
        Assert.Equal("1002 · CVET PRE", b.Vm.ReemplazoCaravanaActual);   // el aviso nuevo es entre la 1002 (actual) y la 1003
        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
    }

    [Fact]
    public async Task El_mismo_codigo_repetido_mientras_se_abre_la_nueva_se_sigue_ignorando()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.ElServicioSumaAlMarcar();
        var repetida = Task.CompletedTask;
        // Justo cuando el servicio marca a la 1002 (la actual ya se cerró y el cuadro todavía está abierto), el bastón la repite.
        b.Campo.OnProcesarLectura = cmd =>
        {
            if (!cmd.HayFichaConTactoAbierta && cmd.Codigo == "1002") repetida = b.Vm.EncolarLecturaAsync("1002", esManual: true);
            return Resultado.Ok(b.Campo.Lecturas[cmd.Codigo]);
        };

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);
        await repetida;

        // La repetida se ignoró: no hubo un segundo pedido de la 1002, no se reabrió el aviso y el conteo es el de una sola marca.
        Assert.Equal(["1001", "1002", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal("1002", b.Vm.Ficha!.Caravana);
    }

    // ------------------------------------------------------------------ RF-10: errores

    [Fact]
    public async Task Si_poner_en_espera_se_rechaza_el_motivo_queda_en_el_aviso_y_no_se_marca_nada()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.Campo.OnPonerEnEspera = _ => Resultado.Error<Guid>(CodigoError.FechaAnteriorAlUltimoRegistro, "Revisá la hora del dispositivo.");

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal("Revisá la hora del dispositivo.", b.Vm.MensajeCuadro);
        Assert.Equal(["1001", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));   // la nueva no se marcó
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);
    }

    [Fact]
    public async Task Si_saltear_la_espera_se_rechaza_el_aviso_sigue_abierto_con_el_motivo()
    {
        var (b, actual) = await BancoConActualEnEsperaYAvisoAsync();
        b.Campo.OnSaltearEspera = _ => Resultado.Error(CodigoError.EsperaNoPendiente, "Esa vaca ya no está en espera.");

        await b.Vm.SaltarYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal("Esa vaca ya no está en espera.", b.Vm.MensajeCuadro);
        Assert.Equal(actual, b.Vm.Ficha!.AnimalId);
        Assert.DoesNotContain(b.Campo.LecturasProcesadas, l => !l.HayFichaConTactoAbierta && l.Codigo == "1002");
    }

    [Fact]
    public async Task Una_vaca_que_ya_estaba_en_espera_se_toma_como_exito()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.Campo.OnPonerEnEspera = _ => Resultado.Error<Guid>(CodigoError.VacaYaEnEspera, "Esa vaca ya está en espera.");
        b.ElServicioSumaAlMarcar();

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal("1002", b.Vm.Ficha!.Caravana);
    }

    [Fact]
    public async Task Si_se_rechaza_la_marca_de_la_nueva_la_actual_conserva_su_espera_y_no_se_abre_nada()
    {
        var b = new Banco();
        var (actual, _) = await b.AvisoAbiertoAsync();
        b.Campo.Esperas = [Datos.Espera("1001", actual)];
        b.Campo.OnProcesarLectura = c => c.HayFichaConTactoAbierta
            ? Resultado.Ok(Datos.RequiereDecision(Guid.NewGuid(), "1002"))
            : Resultado.Error<ResultadoLecturaDto>(CodigoError.FechaAnteriorAlUltimoRegistro, "Revisá la hora del dispositivo.");

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(actual, Assert.Single(b.Campo.EsperasPuestas).AnimalId);   // la actual quedó en espera
        Assert.Null(b.Vm.Ficha);                                                 // y salió de la pantalla
        Assert.Equal("Revisá la hora del dispositivo.", b.Vm.MensajeError);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Equal(1, b.Vm.Contador);                                          // la nueva quedó sin marcar
    }

    [Fact]
    public async Task Una_falla_inesperada_muestra_el_mensaje_fijo_sin_caravanas_y_deja_el_aviso_abierto()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();
        b.Campo.OnPonerEnEspera = _ => throw new InvalidOperationException("detalle interno 1001");

        await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal(TrabajoViewModel.MensajeFalla, b.Vm.MensajeCuadro);
        Assert.DoesNotContain("1001", b.Vm.MensajeCuadro);
    }

    // ------------------------------------------------------------------ RF-11: el aviso sobrevive a un recálculo

    [Fact]
    public async Task El_aviso_sigue_abierto_tras_recalcular_la_pantalla_y_no_guardo_nada()
    {
        var b = new Banco();
        await b.AvisoAbiertoAsync();

        await b.Vm.RecalcularAsync();

        Assert.Equal(CuadroTrabajo.ConfirmarReemplazo, b.Vm.Cuadro);
        Assert.Equal(["1001", "1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Empty(b.Campo.EsperasPuestas);
    }

    // ------------------------------------------------------------------ RF-12: el cierre automático de la manga

    private static async Task<Banco> BancoConMangaPorCompletarAsync()
    {
        var b = new Banco();
        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        b.Vm.TextoCantidadManga = "2";
        await b.Vm.GuardarCantidadMangaCommand.ExecuteAsync(null);
        b.Campo.OnFinalizar = _ =>
        {
            b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: false);
            return Resultado.Ok(Datos.ResultadoCierre(null, aviss: 2));
        };
        await b.AvisoAbiertoAsync();   // la 1001 es la primera de 2; la 1002 es la que completa la manga
        return b;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Las_opciones_1_y_3_corren_el_cierre_automatico_de_la_manga_al_marcar_a_la_nueva(int opcion)
    {
        var b = await BancoConMangaPorCompletarAsync();
        Assert.Empty(b.Campo.Finalizados);   // con el aviso abierto todavía no se completó
        b.ElServicioSumaAlMarcar();

        if (opcion == 1) await b.Vm.PonerEnEsperaYAbrirNuevaCommand.ExecuteAsync(null);
        else await b.Vm.SaltarYAbrirNuevaCommand.ExecuteAsync(null);

        Assert.Single(b.Campo.Finalizados);
        Assert.Equal(1, b.Avisos.MangasCompletas);
    }

    [Fact]
    public async Task Cancelar_no_cierra_la_manga_ni_cambia_el_conteo()
    {
        var b = await BancoConMangaPorCompletarAsync();

        await b.Vm.CancelarLecturaCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.Finalizados);
        Assert.Equal(0, b.Avisos.MangasCompletas);
        Assert.Equal("Manga 1 · 1/2", b.Vm.TextoManga);
    }
}
