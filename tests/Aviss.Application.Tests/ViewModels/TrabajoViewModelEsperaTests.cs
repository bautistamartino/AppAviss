using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Spec 004: En Espera y Rechazo en la pantalla de trabajo (lógica de ViewModel, sin MAUI).
public class TrabajoViewModelEsperaTests
{
    private static readonly EventoCampoDto Prenada = new(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA", true, true);
    private static readonly DateTime Ahora = new(2026, 11, 17, 13, 10, 0, DateTimeKind.Utc);

    private sealed class RelojFijo : IRelojService
    {
        public DateTime UtcAhora => Ahora;
    }

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeLector Lector { get; } = new();
        public FakeAvisos Avisos { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            Sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [Prenada];
            var prefs = new FakePreferencias { ControlPorMangaPorDefecto = true };
            Vm = new TrabajoViewModel(Campo, Lector, prefs, Avisos, new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), Sesion, Lector, new RelojFijo());
        }

        /// <summary>Una vaca existente con CVET: leerla la marca y abre su ficha.</summary>
        public Guid Vaca(string caravana, string cvet = "PRE")
        {
            var animal = Guid.NewGuid();
            Campo.Lecturas[caravana] = new ResultadoLecturaDto(TipoResultadoLectura.Marcada, caravana, animal, caravana,
                Datos.Ficha(animal, caravana, cvet: cvet), Guid.NewGuid(), 1, 1, null, null, false);
            return animal;
        }

        public async Task<Guid> AbrirFichaAsync(string caravana)
        {
            var animal = Vaca(caravana);
            await Vm.EncolarLecturaAsync(caravana);
            return animal;
        }

        /// <summary>Deja una vaca en espera del lado del servicio (ficha y lista) y devuelve su animal.</summary>
        public Guid VacaEnEspera(string caravana, DateTime? desde = null)
        {
            var animal = Guid.NewGuid();
            Campo.Fichas[animal] = Datos.Ficha(animal, caravana, cvet: "PRE", enEspera: true);
            Campo.Lecturas[caravana] = Datos.EnEspera(animal, caravana);
            Campo.Esperas = [.. Campo.Esperas, Datos.Espera(caravana, animal, desde)];
            return animal;
        }
    }

    // ------------------------------------------------------------------ botón "En espera" (RF-1)

    [Fact]
    public async Task El_boton_En_espera_solo_se_ofrece_con_una_ficha_de_una_vaca_que_entra_en_tacto()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        Assert.False(b.Vm.PuedePonerEnEspera);   // sin ficha

        await b.AbrirFichaAsync("1001");
        Assert.True(b.Vm.PuedePonerEnEspera);
    }

    [Fact]
    public async Task Con_CVET_0_no_hay_boton_ni_se_puede_poner_en_espera()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "2002", cvet: "0", entraEnTacto: false);
        b.Campo.Lecturas["2002"] = Datos.SinTacto(animal, "2002");
        await b.Vm.IniciarAsync();
        await b.Vm.EncolarLecturaAsync("2002");
        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.False(b.Vm.PuedePonerEnEspera);
        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);
        Assert.Empty(b.Campo.EsperasPuestas);
    }

    [Fact]
    public async Task Poner_en_espera_guarda_la_espera_cierra_la_ficha_sin_tacto_y_descarta_evento_y_observacion()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        var animal = await b.AbrirFichaAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        b.Vm.ObservacionPendiente = "algo";
        b.Campo.Esperas = [Datos.Espera("1001", animal)];

        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.EsperasPuestas);
        Assert.Equal(animal, cmd.AnimalId);
        Assert.Empty(b.Campo.Tactos);
        Assert.False(b.Vm.HayFicha);
        Assert.Null(b.Vm.EventoElegido);
        Assert.Equal("", b.Vm.ObservacionPendiente);
        Assert.Equal(["1001"], b.Vm.Esperas.Select(e => e.Caravana));   // la botonera se actualiza al instante
        Assert.Equal(1, b.Avisos.Confirmaciones - 1);                    // una confirmación por la marca y otra por la espera
    }

    [Fact]
    public async Task Si_poner_en_espera_se_rechaza_la_ficha_sigue_abierta_con_el_motivo()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.AbrirFichaAsync("1001");
        b.Campo.OnPonerEnEspera = _ => Resultado.Error<Guid>(CodigoError.VacaYaEnEspera, "Ya está en espera.");

        await b.Vm.PonerEnEsperaCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.Equal("Ya está en espera.", b.Vm.MensajeError);
    }

    [Fact]
    public async Task Una_ficha_que_ya_viene_de_una_espera_no_ofrece_ponerla_en_espera_otra_vez()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.VacaEnEspera("7712");
        await b.Vm.EncolarLecturaAsync("7712");

        Assert.True(b.Vm.EsEsperaAbierta);
        Assert.False(b.Vm.PuedePonerEnEspera);
    }

    // ------------------------------------------------------------------ botonera y lista (RF-2)

    [Fact]
    public async Task Sin_esperas_la_botonera_no_aparece()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();

        Assert.False(b.Vm.HayEsperas);
        Assert.Empty(b.Vm.EsperasBotonera);
        Assert.False(b.Vm.HayMasEsperas);
    }

    [Fact]
    public async Task La_botonera_muestra_las_primeras_4_con_su_tiempo_y_el_resto_va_en_el_boton_mas_N()
    {
        var b = new Banco();
        foreach (var c in new[] { "1", "2", "3", "4", "5", "6" }) b.VacaEnEspera(c, Ahora.AddMinutes(-3));
        await b.Vm.IniciarAsync();

        Assert.True(b.Vm.HayEsperas);
        Assert.Equal(6, b.Vm.Esperas.Count);
        Assert.Equal(["1", "2", "3", "4"], b.Vm.EsperasBotonera.Select(e => e.Caravana));
        Assert.True(b.Vm.HayMasEsperas);
        Assert.Equal("+2", b.Vm.TextoMasEsperas);
        Assert.Equal("1 · 3 min", b.Vm.EsperasBotonera[0].TextoBotonera);
    }

    [Fact]
    public async Task La_lista_completa_muestra_caravana_cvet_hora_y_tiempo_esperando()
    {
        var b = new Banco();
        b.VacaEnEspera("7712", Ahora.AddMinutes(-3));
        await b.Vm.IniciarAsync();

        var item = b.Vm.Esperas[0];
        var texto = item.TextoLista;
        Assert.Contains("7712", texto);
        if (!string.IsNullOrWhiteSpace(item.Cvet)) Assert.Contains($"CVET {item.Cvet}", texto);
        Assert.Contains($"desde {item.Hora}", texto);
        Assert.Contains("hace 3 min", texto);
    }

    [Fact]
    public async Task Con_4_o_menos_no_hay_boton_mas_N()
    {
        var b = new Banco();
        foreach (var c in new[] { "1", "2", "3", "4" }) b.VacaEnEspera(c);
        await b.Vm.IniciarAsync();

        Assert.False(b.Vm.HayMasEsperas);
    }

    [Fact]
    public async Task La_lista_completa_muestra_caravana_CVET_hora_y_tiempo_esperando()
    {
        var b = new Banco();
        b.VacaEnEspera("7712", Ahora.AddMinutes(-75));
        await b.Vm.IniciarAsync();

        var item = Assert.Single(b.Vm.Esperas);
        Assert.Equal("7712", item.Caravana);
        Assert.Equal("PRE", item.Cvet);
        Assert.Equal(Ahora.AddMinutes(-75).ToLocalTime().ToString("HH:mm"), item.Hora);
        Assert.Equal("1 h 15 min", item.Tiempo);
    }

    [Fact]
    public async Task El_refresco_de_cada_minuto_vuelve_a_leer_las_esperas()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        var consultas = b.Campo.ConsultasDeEsperas;

        b.VacaEnEspera("9");
        await b.Vm.RecalcularAsync();
        await b.Vm.CargarEsperasAsync();

        Assert.True(b.Campo.ConsultasDeEsperas > consultas);
        Assert.Single(b.Vm.Esperas);
    }

    // ------------------------------------------------------------------ abrir una espera (RF-3, RF-5)

    [Fact]
    public async Task Abrir_una_espera_muestra_su_ficha_sin_marcar_ni_sumar_al_conteo()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        var contador = b.Vm.Contador;

        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);

        Assert.True(b.Vm.HayFicha);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.True(b.Vm.EsEsperaAbierta);
        Assert.Empty(b.Campo.LecturasProcesadas);   // no se leyó nada: no se marca
        Assert.Equal(contador, b.Vm.Contador);
    }

    [Fact]
    public async Task Abrir_una_espera_que_ya_no_esta_pendiente_no_abre_la_ficha_y_refresca_la_botonera()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "7712", cvet: "PRE", enEspera: false);   // otra tablet la resolvió
        b.Campo.Esperas = [];

        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);

        Assert.False(b.Vm.HayFicha);
        Assert.Empty(b.Vm.Esperas);
        Assert.NotNull(b.Vm.MensajeError);
    }

    [Fact]
    public async Task Leer_una_vaca_en_espera_abre_su_ficha_sin_aviso_de_repetida_y_sin_sumar()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        var contador = b.Vm.Contador;

        await b.Vm.EncolarLecturaAsync("7712");

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.True(b.Vm.EsEsperaAbierta);
        Assert.False(b.Vm.HayAviso);
        Assert.Equal(contador, b.Vm.Contador);
        Assert.Equal(0, b.Avisos.Repetidos);
    }

    // ------------------------------------------------------------------ resolver y saltar (RF-3)

    [Fact]
    public async Task Entrar_con_un_tacto_desde_una_espera_lo_guarda_y_vuelve_a_leer_las_esperas()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        var consultas = b.Campo.ConsultasDeEsperas;
        b.Campo.Esperas = [];   // el servicio la resolvió en la misma transacción

        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Single(b.Campo.Tactos);
        Assert.False(b.Vm.HayFicha);
        Assert.True(b.Campo.ConsultasDeEsperas > consultas);
        Assert.Empty(b.Vm.Esperas);
    }

    [Fact]
    public async Task Si_el_tacto_se_rechaza_la_espera_sigue_y_la_ficha_queda_abierta_con_el_motivo()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        b.Campo.OnRegistrarTacto = _ => Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La hora de la tablet no es válida.");

        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.True(b.Vm.EsEsperaAbierta);
        Assert.Single(b.Vm.Esperas);
        Assert.Equal("La hora de la tablet no es válida.", b.Vm.MensajeError);
    }

    [Fact]
    public async Task Saltar_en_una_ficha_de_espera_saltea_la_espera_sin_guardar_registros()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        b.Campo.Esperas = [];

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.EsperasSalteadas);
        Assert.Equal(animal, cmd.AnimalId);
        Assert.False(b.Vm.HayFicha);
        Assert.Empty(b.Campo.Tactos);
        Assert.Empty(b.Campo.Rechazos);
        Assert.Empty(b.Vm.Esperas);
    }

    [Fact]
    public async Task Saltar_en_una_ficha_normal_solo_la_cierra()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");   // hay una espera de otra vaca: no se toca
        await b.Vm.IniciarAsync();
        await b.AbrirFichaAsync("1001");

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        Assert.False(b.Vm.HayFicha);
        Assert.Empty(b.Campo.EsperasSalteadas);
        Assert.Single(b.Vm.Esperas);
    }

    [Fact]
    public async Task Si_saltear_falla_la_ficha_queda_abierta_con_el_motivo()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        b.Campo.OnSaltearEspera = _ => Resultado.Error(CodigoError.EsperaNoPendiente, "Esa vaca ya no está en espera.");

        await b.Vm.CerrarFichaCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.Equal("Esa vaca ya no está en espera.", b.Vm.MensajeError);
    }

    // ------------------------------------------------------------------ nada más las saca (RF-4, RF-10)

    [Fact]
    public async Task Terminar_el_tacto_avisa_cuantas_vacas_hay_en_espera_sin_sacarlas()
    {
        var b = new Banco();
        b.VacaEnEspera("1");
        b.VacaEnEspera("2");
        await b.Vm.IniciarAsync();

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayEsperaPendiente);
        Assert.Equal("Hay 2 vacas en espera", b.Vm.TextoEsperaPendiente);
        Assert.Empty(b.Campo.EsperasSalteadas);
        Assert.Equal(2, b.Vm.Esperas.Count);
    }

    [Fact]
    public async Task Con_una_sola_espera_el_aviso_va_en_singular_y_sin_esperas_no_hay_aviso()
    {
        var b = new Banco();
        b.VacaEnEspera("1");
        await b.Vm.IniciarAsync();
        Assert.Equal("Hay 1 vaca en espera", b.Vm.TextoEsperaPendiente);

        var vacio = new Banco();
        await vacio.Vm.IniciarAsync();
        Assert.False(vacio.Vm.HayEsperaPendiente);
        Assert.Equal("", vacio.Vm.TextoEsperaPendiente);
    }

    // ------------------------------------------------------------------ lista de espera (RF-15)

    [Fact]
    public async Task Con_la_lista_abierta_una_lectura_se_guarda_y_se_procesa_al_cerrarla()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        b.Vaca("1001");

        await b.Vm.AbrirListaEsperaCommand.ExecuteAsync(null);
        Assert.True(b.Vm.HayListaEspera);
        await b.Vm.EncolarLecturaAsync("1001");
        Assert.Empty(b.Campo.LecturasProcesadas);   // el cuadro frena las lecturas

        await b.Vm.CerrarListaEsperaCommand.ExecuteAsync(null);

        Assert.False(b.Vm.HayListaEspera);
        Assert.Equal(["1001"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
    }

    [Fact]
    public async Task Con_la_lista_abierta_la_lectura_de_una_vaca_en_espera_abre_su_ficha_al_cerrarla()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();

        await b.Vm.AbrirListaEsperaCommand.ExecuteAsync(null);
        await b.Vm.EncolarLecturaAsync("7712");
        await b.Vm.CerrarListaEsperaCommand.ExecuteAsync(null);

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.False(b.Vm.HayAviso);
    }

    [Fact]
    public async Task Abrir_una_espera_desde_la_lista_cierra_la_lista_y_muestra_la_ficha()
    {
        var b = new Banco();
        var animal = b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirListaEsperaCommand.ExecuteAsync(null);

        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);

        Assert.False(b.Vm.HayListaEspera);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
    }

    // ------------------------------------------------------------------ Rechazo (RF-6)

    [Fact]
    public async Task La_lista_de_eventos_trae_el_Rechazo_primero_en_Otro_evento_y_el_catalogo_no_lo_incluye()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();

        Assert.Equal(["PREÑADA"], b.Vm.Selector.Principales.Select(e => e.Nombre));
        Assert.Equal(["RECHAZO"], b.Vm.Selector.Otros.Select(e => e.Nombre));
        Assert.Equal(EventosCampo.Rechazo, b.Vm.Selector.Otros[0].Id);
    }

    [Fact]
    public async Task Sin_estados_activos_el_Rechazo_igual_esta_en_la_lista()
    {
        var b = new Banco();
        b.Campo.Estados = [];
        await b.Vm.IniciarAsync();

        Assert.Equal(["RECHAZO"], b.Vm.Selector.Otros.Select(e => e.Nombre));
        Assert.False(b.Vm.Selector.HayPrincipales);
        Assert.Equal([EventosCampo.Rechazo], b.Vm.Selector.Otros.Select(e => e.Id));
    }

    [Fact]
    public async Task Un_estado_creado_con_Nuevo_evento_queda_en_Otro_evento_despues_del_Rechazo()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.AbrirFichaAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.Selector.VerOtrosCommand.Execute(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        b.Vm.NuevoEventoNombre = "Aborto";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Equal(["RECHAZO", "ABORTO"], b.Vm.Selector.Otros.Select(e => e.Nombre));
    }

    [Fact]
    public async Task Entrar_con_Rechazo_guarda_un_rechazo_con_el_motivo_y_no_un_tacto()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        var animal = await b.AbrirFichaAsync("1001");
        var rechazoId = Guid.NewGuid();
        b.Campo.OnRegistrarRechazo = _ => Resultado.Ok(rechazoId);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(TrabajoViewModel.EventoRechazo);
        b.Vm.ObservacionPendiente = "Pata";

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Rechazos);
        Assert.Equal((animal, "Pata"), (cmd.AnimalId, cmd.Observaciones));
        Assert.Empty(b.Campo.Tactos);
        Assert.False(b.Vm.HayFicha);
        Assert.Equal("1001: RECHAZO", b.Vm.UltimoGuardado);
        Assert.Equal((TipoDeshacer.Rechazo, rechazoId), (b.Vm.AvisoDeshacer!.Tipo, b.Vm.AvisoDeshacer.Id));
    }

    [Fact]
    public async Task El_motivo_del_rechazo_es_opcional()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.AbrirFichaAsync("1001");

        await b.Vm.ElegirEventoCommand.ExecuteAsync(TrabajoViewModel.EventoRechazo);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(b.Campo.Rechazos).Observaciones);
    }

    [Fact]
    public async Task Un_rechazo_desde_una_espera_la_resuelve_en_la_misma_operacion()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        b.Campo.Esperas = [];

        await b.Vm.ElegirEventoCommand.ExecuteAsync(TrabajoViewModel.EventoRechazo);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Single(b.Campo.Rechazos);
        Assert.Empty(b.Vm.Esperas);
    }

    [Fact]
    public async Task Si_el_rechazo_se_rechaza_la_ficha_sigue_abierta_con_el_evento_elegido_y_el_motivo()
    {
        var b = new Banco();
        b.VacaEnEspera("7712");
        await b.Vm.IniciarAsync();
        await b.Vm.AbrirEsperaCommand.ExecuteAsync(b.Vm.Esperas[0]);
        b.Campo.OnRegistrarRechazo = _ => Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La hora de la tablet no es válida.");

        await b.Vm.ElegirEventoCommand.ExecuteAsync(TrabajoViewModel.EventoRechazo);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayFicha);
        Assert.True(b.Vm.HayEventoElegido);
        Assert.Single(b.Vm.Esperas);
        Assert.Equal("La hora de la tablet no es válida.", b.Vm.MensajeError);
    }

    [Fact]
    public async Task Deshacer_el_aviso_de_un_rechazo_manda_el_tipo_Rechazo()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.AbrirFichaAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(TrabajoViewModel.EventoRechazo);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        await b.Vm.DeshacerAvisoCommand.ExecuteAsync(null);

        Assert.Equal(TipoDeshacer.Rechazo, Assert.Single(b.Campo.Deshaceres).Tipo);
        Assert.Equal("Se deshizo el rechazo.", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task El_aviso_de_repetida_describe_el_rechazo_con_su_motivo_y_Corregir_deshace_un_rechazo()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        var rechazoId = Guid.NewGuid();
        var ultimo = new UltimoRegistroDto(TipoRegistroCampo.Rechazo, rechazoId, Ahora, null, 1, "Pata");
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001", ultimo, puedeCorregir: true);
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "1001");
        await b.Vm.IniciarAsync();

        await b.Vm.EncolarLecturaAsync("1001");

        Assert.Contains("RECHAZO · Pata", b.Vm.AvisoDetalle);
        await b.Vm.CorregirCommand.ExecuteAsync(null);
        await b.Vm.ConfirmarCorregirCommand.ExecuteAsync(null);
        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal((TipoDeshacer.Rechazo, rechazoId), (cmd.Tipo, cmd.Id));
    }
}
