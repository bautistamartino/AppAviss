using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Prueban la pantalla de trabajo sin MAUI: la fachada de campo, el lector, el tiempo y la plataforma son falsos.
public class TrabajoViewModelTests
{
    private static readonly EventoCampoDto Prenada = new(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA", true, false);
    private static readonly EventoCampoDto Vacia = new(Guid.Parse("33333333-0000-0000-0000-000000000002"), "VACIA", true, false);

    /// <summary>Todo lo que necesita el ViewModel, armado con falsos y con la sesión ya elegida.</summary>
    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeLector Lector { get; } = new();
        public FakePreferencias Preferencias { get; } = new();
        public FakeAvisos Avisos { get; } = new();
        public FakePantalla Pantalla { get; } = new();
        public FakeTemporizador Temporizador { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco(bool conSimulador = true, bool conSesion = true)
        {
            if (conSesion) Sesion.Empezar(Datos.Vet, Datos.Rodeo());
            Campo.Estados = [Prenada, Vacia];
            Preferencias.ControlPorMangaPorDefecto = true;   // las pruebas existentes trabajan con el control por manga prendido
            Vm = new TrabajoViewModel(Campo, Lector, Preferencias, Avisos, Pantalla, Temporizador, Navegacion, Sesion,
                conSimulador ? Lector : null);
        }

        /// <summary>Registra una vaca existente: leer su caravana la marca.</summary>
        public (Guid AnimalId, Guid MarcaId) Vaca(string caravana, int manga = 1, int contador = 1)
        {
            var animal = Guid.NewGuid();
            var marca = Guid.NewGuid();
            Campo.Lecturas[caravana] = Datos.Marcada(animal, caravana, marca, manga, contador);
            return (animal, marca);
        }

        public Task LeerAsync(string codigo) => Vm.EncolarLecturaAsync(codigo);

        /// <summary>Elegir el evento y tocar "Entrar": el tacto se guarda recién con "Entrar".</summary>
        public async Task ElegirYEntrarAsync(EventoCampoDto estado)
        {
            await Vm.ElegirEventoCommand.ExecuteAsync(estado);
            await Vm.EntrarCommand.ExecuteAsync(null);
        }
    }

    // ------------------------------------------------------------------ arranque

    [Fact]
    public async Task Al_iniciar_empieza_el_trabajo_carga_los_estados_y_enciende_la_pantalla_y_el_lector()
    {
        var b = new Banco();
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: true, contador: 7, procesadas: 30);

        await b.Vm.IniciarAsync();

        Assert.Equal([(Datos.RodeoId, Datos.Vet.Id)], b.Campo.IniciosDeTrabajo);
        Assert.Equal("Manga 2 · 7", b.Vm.TextoManga);
        Assert.True(b.Vm.MangaAbierta);
        Assert.Equal(30, b.Vm.Avance!.Procesadas);
        Assert.Equal(2, b.Vm.Estados.Count);
        Assert.True(b.Vm.HayEstadosActivos);
        Assert.True(b.Pantalla.Encendida);
        await Esperas.HastaAsync(() => b.Lector.Conexiones == 1);
        Assert.Equal("Tambo Norte", b.Vm.NombreEstablecimiento);
        Assert.Equal("Dra. Ana", b.Vm.NombreVeterinario);
    }

    [Fact]
    public async Task Sin_veterinario_y_rodeo_elegidos_vuelve_a_la_eleccion_y_no_empieza_nada()
    {
        var b = new Banco(conSesion: false);

        await b.Vm.IniciarAsync();

        Assert.Equal(["Eleccion"], b.Navegacion.Destinos);
        Assert.Empty(b.Campo.IniciosDeTrabajo);
    }

    [Fact]
    public async Task Detener_corta_el_lector_y_deja_que_la_pantalla_se_apague()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();

        await b.Vm.DetenerAsync();

        Assert.False(b.Pantalla.Encendida);
        Assert.Equal(1, b.Lector.Desconexiones);
    }

    [Fact]
    public async Task Volver_a_aparecer_no_vuelve_a_empezar_el_trabajo_para_no_cerrar_una_manga_que_cruzo_la_medianoche()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.DetenerAsync();
        var consultas = b.Campo.ConsultasDeEstado;

        await b.Vm.IniciarAsync();   // por ejemplo, al volver de "Últimos movimientos"

        Assert.Single(b.Campo.IniciosDeTrabajo);
        Assert.True(b.Campo.ConsultasDeEstado > consultas);   // solo recalcula
    }

    [Fact]
    public async Task Una_lectura_del_lector_llega_al_flujo_y_marca_la_vaca()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.Vm.IniciarAsync();

        b.Lector.Emitir("1001");

        await Esperas.HastaAsync(() => b.Vm.Ficha is not null);
        Assert.Equal("1001", b.Vm.Ficha!.Caravana);
    }

    // ------------------------------------------------------------------ marca automática y ficha

    [Fact]
    public async Task Una_lectura_marca_la_vaca_muestra_la_ficha_y_sube_el_contador()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001", manga: 1, contador: 1);
        b.Campo.Estado = Datos.EstadoTrabajo(abierta: true, contador: 1, procesadas: 1);

        await b.LeerAsync("1001");

        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal("Manga 1 · 1", b.Vm.TextoManga);
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);
        Assert.Equal(1, b.Avisos.Confirmaciones);
        // Marcar no muestra el aviso de deshacer (se puede deshacer desde "Movimientos").
        Assert.Null(b.Vm.AvisoDeshacer);
        Assert.Single(b.Campo.LecturasProcesadas);
        Assert.Equal(Datos.Vet.Id, b.Campo.LecturasProcesadas[0].UsuarioId);
    }

    [Fact]
    public async Task Una_lectura_rechazada_muestra_el_motivo_y_no_muestra_la_ficha()
    {
        var b = new Banco();
        b.Campo.OnProcesarLectura = _ => Resultado.Error<ResultadoLecturaDto>(CodigoError.FechaAnteriorAlUltimoRegistro, "Revisá la hora del dispositivo.");

        await b.LeerAsync("1001");

        Assert.Equal("Revisá la hora del dispositivo.", b.Vm.MensajeError);
        Assert.Null(b.Vm.Ficha);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
    }

    [Fact]
    public async Task Una_falla_inesperada_muestra_un_mensaje_fijo_sin_datos()
    {
        var b = new Banco();
        b.Campo.OnProcesarLectura = _ => throw new InvalidOperationException("detalle interno 1001");

        await b.LeerAsync("1001");

        Assert.Equal(TrabajoViewModel.MensajeFalla, b.Vm.MensajeError);
        Assert.DoesNotContain("1001", b.Vm.MensajeError);
    }

    [Fact]
    public async Task Otra_vaca_reemplaza_la_ficha_abierta()
    {
        var b = new Banco();
        b.Vaca("1001");
        var (segunda, _) = b.Vaca("1002", contador: 2);

        await b.LeerAsync("1001");
        await b.LeerAsync("1002");

        Assert.Equal(segunda, b.Vm.Ficha!.AnimalId);
        Assert.Equal(2, b.Campo.LecturasProcesadas.Count);   // las dos quedaron marcadas
    }

    [Fact]
    public async Task La_misma_vaca_de_la_ficha_abierta_se_ignora_sin_aviso()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        await b.LeerAsync("1001");
        // El servicio dice que ya estaba marcada: es la misma vaca de la ficha.
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001");

        await b.LeerAsync("1001");

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(0, b.Avisos.Repetidos);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);   // la ficha sigue abierta
    }

    // ------------------------------------------------------------------ CVET: solo entran en tacto las de CVET distinto de 0

    [Fact]
    public async Task Una_vaca_con_cvet_cero_hace_sonar_la_chicharra_suma_al_conteo_y_no_abre_la_ficha()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        var marca = Guid.NewGuid();
        b.Campo.Lecturas["2224"] = Datos.SinTacto(animal, "2224", marca, manga: 1, contador: 5);
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 5);   // lo que devuelve el servicio tras marcar

        await b.LeerAsync("2224");

        Assert.Equal(1, b.Avisos.SinTactos);
        Assert.Equal(0, b.Avisos.Confirmaciones);   // no es el "guardado" normal: suena la chicharra
        Assert.Null(b.Vm.Ficha);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);   // nada que cerrar: se puede leer la siguiente
        Assert.Equal(5, b.Vm.Contador);                     // suma al conteo de la manga
        Assert.True(b.Vm.MangaAbierta);
        Assert.Equal("2224: no entra en tacto (CVET 0)", b.Vm.UltimoGuardado);
        Assert.Null(b.Vm.AvisoDeshacer);   // tampoco acá hay aviso de deshacer (queda en "Movimientos")
    }

    [Fact]
    public async Task Despues_de_una_vaca_con_cvet_cero_la_siguiente_se_procesa_enseguida()
    {
        var b = new Banco();
        b.Campo.Lecturas["1"] = Datos.SinTacto(Guid.NewGuid(), "1");
        var (siguiente, _) = b.Vaca("2");

        await b.LeerAsync("1");
        await b.LeerAsync("2");

        Assert.Equal(["1", "2"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(siguiente, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Una_vaca_con_cvet_cero_cierra_la_ficha_de_la_anterior()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.Lecturas["2224"] = Datos.SinTacto(Guid.NewGuid(), "2224");
        await b.LeerAsync("1001");
        Assert.NotNull(b.Vm.Ficha);

        await b.LeerAsync("2224");

        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task La_ficha_de_una_vaca_que_entra_en_tacto_muestra_el_cvet_como_motivo()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["9"] = Datos.Marcada(animal, "9") with { Ficha = Datos.Ficha(animal, "9", cvet: "PRE") };

        await b.LeerAsync("9");

        Assert.True(b.Vm.HayMotivoTacto);
        Assert.Equal("Motivo del tacto · CVET: PRE", b.Vm.TextoMotivoTacto);
        Assert.False(b.Vm.NoEntraEnTacto);
        Assert.True(b.Vm.PuedeAgregarEventos);
    }

    // ---- "Ver ficha" de la última vaca que no entró en tacto (CVET 0), para mirar sus datos por curiosidad

    private static async Task<(Banco Banco, Guid Animal)> BancoConUnaVacaSinTactoAsync()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["2224"] = Datos.SinTacto(animal, "2224");
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "2224", cvet: "0", entraEnTacto: false);
        await b.LeerAsync("2224");
        return (b, animal);
    }

    [Fact]
    public async Task Tras_una_vaca_con_cvet_cero_aparece_la_opcion_de_ver_su_ficha()
    {
        var (b, _) = await BancoConUnaVacaSinTactoAsync();

        Assert.True(b.Vm.HayVerFichaSinTacto);
        Assert.Equal("Ver ficha de 2224", b.Vm.TextoVerFichaSinTacto);
    }

    [Fact]
    public async Task Ver_la_ficha_la_abre_solo_para_mirar_sin_marcar_de_nuevo_ni_dejar_tactar()
    {
        var (b, animal) = await BancoConUnaVacaSinTactoAsync();

        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
        Assert.True(b.Vm.NoEntraEnTacto);
        Assert.False(b.Vm.PuedeAgregarEventos);
        Assert.Equal(["2224"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));   // no se leyó ni se marcó otra vez
        Assert.Equal([animal], b.Campo.FichasPedidas);
        Assert.False(b.Vm.HayVerFichaSinTacto);   // ya no se está esperando: el botón no se ve mientras está la ficha
    }

    [Fact]
    public async Task Al_saltar_la_ficha_vuelve_el_Ultimo_y_la_opcion_de_verla()
    {
        var (b, _) = await BancoConUnaVacaSinTactoAsync();
        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);

        b.Vm.CerrarFichaCommand.Execute(null);

        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Equal("2224: no entra en tacto (CVET 0)", b.Vm.UltimoGuardado);
        Assert.True(b.Vm.HayVerFichaSinTacto);
    }

    [Fact]
    public async Task Una_lectura_posterior_o_un_tacto_guardado_hacen_desaparecer_la_opcion()
    {
        var (b, _) = await BancoConUnaVacaSinTactoAsync();
        b.Vaca("1001");

        await b.LeerAsync("1001");   // otra vaca que sí entra: "Marcada: 1001"
        b.Vm.CerrarFichaCommand.Execute(null);

        Assert.False(b.Vm.HayVerFichaSinTacto);
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task Sin_ninguna_vaca_con_cvet_cero_no_hay_opcion_de_ver_ficha()
    {
        var b = new Banco();
        b.Vaca("1001");

        await b.LeerAsync("1001");
        b.Vm.CerrarFichaCommand.Execute(null);

        Assert.False(b.Vm.HayVerFichaSinTacto);
    }

    [Fact]
    public async Task Si_no_se_puede_traer_la_ficha_se_muestra_el_motivo_y_no_se_abre_nada()
    {
        var (b, animal) = await BancoConUnaVacaSinTactoAsync();
        b.Campo.Fichas.Remove(animal);

        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);

        Assert.Equal("No se encontró la vaca.", b.Vm.MensajeError);
        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task Sin_una_vaca_con_cvet_cero_el_comando_de_ver_ficha_no_hace_nada()
    {
        var b = new Banco();

        await b.Vm.VerFichaSinTactoCommand.ExecuteAsync(null);

        Assert.Null(b.Vm.Ficha);
        Assert.Empty(b.Campo.FichasPedidas);
    }

    [Fact]
    public async Task Sin_dato_de_cvet_no_se_muestra_el_motivo_pero_se_puede_tactar()
    {
        var b = new Banco();
        b.Vaca("1001");

        await b.LeerAsync("1001");

        Assert.False(b.Vm.HayMotivoTacto);
        Assert.True(b.Vm.PuedeAgregarEventos);
    }

    [Fact]
    public async Task La_ficha_de_una_vaca_con_cvet_cero_no_deja_agregar_eventos_ni_observaciones()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        // Se llega a esta ficha con "Ver ficha" desde un aviso de repetida.
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "2224", cvet: "0", entraEnTacto: false);
        b.Campo.Lecturas["2224"] = Datos.Repetido(animal, "2224");
        await b.LeerAsync("2224");
        await b.Vm.VerFichaCommand.ExecuteAsync(null);
        Assert.NotNull(b.Vm.Ficha);

        Assert.True(b.Vm.NoEntraEnTacto);
        Assert.False(b.Vm.PuedeAgregarEventos);
        Assert.False(b.Vm.HayMotivoTacto);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        await b.ElegirYEntrarAsync(Prenada);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Tactos);
    }

    // ------------------------------------------------------------------ elegir el evento y "Entrar" (como en el Daily)

    [Fact]
    public async Task Elegir_un_evento_no_guarda_nada_queda_en_el_casillero_y_habilita_Entrar()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        Assert.Equal("Tocá para elegir", b.Vm.TextoEventoElegido);
        Assert.False(b.Vm.EntrarCommand.CanExecute(null));   // sin evento no se puede entrar

        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);

        Assert.Empty(b.Campo.Tactos);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Equal(Prenada, b.Vm.EventoElegido);
        Assert.Equal("PREÑADA", b.Vm.TextoEventoElegido);
        Assert.True(b.Vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Entrar_guarda_el_tacto_con_el_evento_elegido_y_la_observacion()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Cojera";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);
        Assert.Equal("Cojera", b.Vm.TextoCasilleroObservacion);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Prenada.Id, "Cojera", Datos.Vet.Id), (cmd.AnimalId, cmd.EstadoNuevoId, cmd.Observaciones, cmd.UsuarioId));
        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.EventoElegido);
        Assert.Equal("Opcional", b.Vm.TextoCasilleroObservacion);
        Assert.Equal(2, b.Avisos.Confirmaciones);   // vibró la marca y vibró el tacto
    }

    [Fact]
    public async Task Entrar_sin_evento_elegido_no_guarda_nada()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.Tactos);
        Assert.NotNull(b.Vm.Ficha);
    }

    [Fact]
    public async Task Elegir_otro_evento_reemplaza_al_anterior()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Vacia);
        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal(Vacia.Id, Assert.Single(b.Campo.Tactos).EstadoNuevoId);
    }

    [Fact]
    public async Task Un_tacto_rechazado_en_Entrar_conserva_el_evento_y_la_observacion()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.OnRegistrarTacto = _ => Resultado.Error<Guid>(CodigoError.EstadoDeBaja, "Ese estado está dado de baja.");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Revisar";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);

        await b.Vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal("Ese estado está dado de baja.", b.Vm.MensajeError);
        Assert.Equal(Prenada, b.Vm.EventoElegido);
        Assert.Equal("Revisar", b.Vm.ObservacionPendiente);
        Assert.NotNull(b.Vm.Ficha);
    }

    [Fact]
    public async Task Saltar_cierra_la_ficha_sin_guardar_y_descarta_lo_elegido()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);

        b.Vm.CerrarFichaCommand.Execute(null);   // el botón "Saltar"

        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.EventoElegido);
        Assert.Empty(b.Campo.Tactos);
        Assert.False(b.Vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Una_ficha_nueva_empieza_sin_evento_elegido_y_en_la_pestana_de_datos()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Vaca("1002", contador: 2);
        await b.LeerAsync("1001");
        await b.Vm.ElegirEventoCommand.ExecuteAsync(Prenada);
        b.Vm.VerHistorialCommand.Execute(null);
        Assert.True(b.Vm.MostrarHistorial);

        await b.LeerAsync("1002");

        Assert.Null(b.Vm.EventoElegido);
        Assert.False(b.Vm.MostrarHistorial);
        Assert.True(b.Vm.HayDatos);
    }

    [Fact]
    public async Task Las_pestanas_alternan_entre_datos_e_historial()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        Assert.True(b.Vm.HayDatos);

        b.Vm.VerHistorialCommand.Execute(null);
        Assert.False(b.Vm.HayDatos);
        Assert.True(b.Vm.MostrarHistorial);

        b.Vm.VerDatosCommand.Execute(null);
        Assert.True(b.Vm.HayDatos);
    }

    [Fact]
    public async Task El_titulo_de_la_ficha_lleva_el_id_y_el_cvet_si_entra_en_tacto()
    {
        var b = new Banco();
        var conCvet = Guid.NewGuid();
        var sinCvet = Guid.NewGuid();
        b.Campo.Lecturas["1014"] = Datos.Marcada(conCvet, "1014") with { Ficha = Datos.Ficha(conCvet, "1014", cvet: "PRE") };
        b.Campo.Lecturas["77"] = Datos.Marcada(sinCvet, "77");

        await b.LeerAsync("1014");
        Assert.Equal("ID 1014 · CVET PRE", b.Vm.TituloFicha);

        await b.LeerAsync("77");
        Assert.Equal("ID 77", b.Vm.TituloFicha);
    }

    [Fact]
    public async Task La_tabla_de_datos_trae_el_estado_los_campos_conocidos_y_las_columnas_extra_con_valor()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        var ficha = Datos.Ficha(animal, "1014", cvet: "PRE") with
        {
            EstadoNombre = "INSEMIN",
            Campos = [new("LACT", "3"), new("DEL", "95"), new("FPART", ""), new("FAPAR", ""), new("COM", "")],
            Inseminaciones = "2",
            UltimoToro = "FUNKI",
            Extras = [new("DCC", "0"), new("LECHE", "24")]
        };
        b.Campo.Lecturas["1014"] = Datos.Marcada(animal, "1014") with { Ficha = ficha };

        await b.LeerAsync("1014");

        Assert.Equal(
            [("Estado", "INSEMIN"), ("LACT", "3"), ("DEL", "95"), ("VINS", "2"), ("UTORO", "FUNKI"), ("DCC", "0"), ("LECHE", "24")],
            b.Vm.FilasDatos.Select(f => (f.Nombre, f.Valor)));
    }

    // ------------------------------------------------------------------ tacto con un toque

    [Fact]
    public async Task Un_toque_en_un_estado_guarda_el_tacto_y_deja_todo_listo_para_la_siguiente()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        var tactoId = Guid.NewGuid();
        b.Campo.OnRegistrarTacto = _ => Resultado.Ok(tactoId);
        await b.LeerAsync("1001");

        await b.ElegirYEntrarAsync(Prenada);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal(animal, cmd.AnimalId);
        Assert.Equal(Prenada.Id, cmd.EstadoNuevoId);
        Assert.Equal(Datos.Vet.Id, cmd.UsuarioId);
        Assert.Null(b.Vm.Ficha);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Equal("1001: PREÑADA", b.Vm.UltimoGuardado);
        Assert.Equal(TipoDeshacer.Tacto, b.Vm.AvisoDeshacer!.Tipo);
        Assert.Equal(tactoId, b.Vm.AvisoDeshacer.Id);
        Assert.Equal(2, b.Avisos.Confirmaciones);   // la marca y el tacto
    }

    [Fact]
    public async Task Un_tacto_rechazado_deja_la_ficha_abierta_con_el_motivo()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.OnRegistrarTacto = _ => Resultado.Error<Guid>(CodigoError.EstadoDeBaja, "Ese estado está dado de baja.");
        await b.LeerAsync("1001");

        await b.ElegirYEntrarAsync(Prenada);

        Assert.Equal("Ese estado está dado de baja.", b.Vm.MensajeError);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
    }

    // ------------------------------------------------------------------ "Agregar evento" y "Agregar observación"

    [Fact]
    public async Task Agregar_evento_abre_el_cuadro_con_las_opciones_y_elegir_una_guarda_el_tacto()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        await b.LeerAsync("1001");

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);
        Assert.True(b.Vm.HayEventos);

        await b.ElegirYEntrarAsync(Prenada);

        var cmd = Assert.Single(b.Campo.Tactos);
        Assert.Equal((animal, Prenada.Id), (cmd.AnimalId, cmd.EstadoNuevoId));
        Assert.Null(cmd.Observaciones);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task Sin_ficha_abierta_agregar_evento_u_observacion_no_abre_nada()
    {
        var b = new Banco();

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task Crear_un_evento_nuevo_lo_suma_a_la_lista_y_no_guarda_ningun_tacto()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.Vm.IniciarAsync();   // carga los estados
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        Assert.True(b.Vm.HayFormularioEvento);
        b.Vm.NuevoEventoNombre = "  Aborto ";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Equal([("Aborto", "ABORTO")], b.Campo.EstadosCreados);   // sin código de Dairy Comp: nombre en mayúsculas
        Assert.Contains(b.Vm.Estados, e => e.Nombre == "ABORTO");
        Assert.False(b.Vm.HayFormularioEvento);
        Assert.Equal(CuadroTrabajo.Eventos, b.Vm.Cuadro);   // sigue la lista abierta para elegirlo
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Crear_un_evento_con_codigo_de_dairy_comp_manda_ese_codigo()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        b.Vm.NuevoEventoNombre = "Aborto";
        b.Vm.NuevoEventoCodigoDc = " ABORT ";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Equal([("Aborto", "ABORT")], b.Campo.EstadosCreados);
    }

    [Fact]
    public async Task Crear_un_evento_sin_nombre_pide_el_nombre_y_no_llama_al_servicio()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        b.Vm.NuevoEventoNombre = "   ";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Equal("Escribí el nombre del evento.", b.Vm.MensajeCuadro);
        Assert.True(b.Vm.HayFormularioEvento);
        Assert.Empty(b.Campo.EstadosCreados);
    }

    [Fact]
    public async Task Un_evento_repetido_muestra_el_motivo_y_deja_el_formulario_abierto()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.OnCrearEstado = _ => Resultado.Error<Guid>(CodigoError.Duplicado, "Ya existe un estado con ese nombre.");
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        b.Vm.NuevoEventoNombre = "Vacia";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un estado con ese nombre.", b.Vm.MensajeCuadro);
        Assert.True(b.Vm.HayFormularioEvento);
    }

    [Fact]
    public async Task Cancelar_el_evento_nuevo_cierra_el_formulario_y_al_reabrir_la_lista_arranca_cerrado()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        b.Vm.AbrirNuevoEventoCommand.Execute(null);

        b.Vm.CancelarNuevoEventoCommand.Execute(null);
        Assert.False(b.Vm.HayFormularioEvento);

        b.Vm.AbrirNuevoEventoCommand.Execute(null);
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        Assert.False(b.Vm.HayFormularioEvento);
    }

    [Fact]
    public async Task Sin_el_cuadro_de_eventos_abierto_no_se_puede_crear_un_evento()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        b.Vm.NuevoEventoNombre = "Aborto";

        await b.Vm.CrearEventoCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.EstadosCreados);
    }

    [Fact]
    public async Task Cerrar_el_cuadro_de_eventos_vuelve_a_la_ficha_sin_guardar()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);

        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task La_observacion_se_guarda_con_el_proximo_evento_y_despues_se_borra()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Observacion, b.Vm.Cuadro);
        b.Vm.TextoObservacion = "  Cojera pata izquierda ";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        Assert.Equal("Cojera pata izquierda", b.Vm.ObservacionPendiente);
        Assert.True(b.Vm.HayObservacionPendiente);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Tactos);   // la observación sola no guarda nada

        await b.Vm.AbrirEventosCommand.ExecuteAsync(null);
        await b.ElegirYEntrarAsync(Prenada);

        Assert.Equal("Cojera pata izquierda", Assert.Single(b.Campo.Tactos).Observaciones);
        Assert.Equal("", b.Vm.ObservacionPendiente);
        Assert.False(b.Vm.HayObservacionPendiente);
    }

    [Fact]
    public async Task Una_observacion_vacia_no_deja_nada_pendiente_y_se_puede_quitar()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "   ";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);
        Assert.False(b.Vm.HayObservacionPendiente);

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Revisar";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);
        b.Vm.QuitarObservacionCommand.Execute(null);

        Assert.False(b.Vm.HayObservacionPendiente);
    }

    [Fact]
    public async Task Cancelar_la_observacion_deja_la_anterior_y_al_reabrir_se_ve_el_texto_guardado()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Primera";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        Assert.Equal("Primera", b.Vm.TextoObservacion);
        b.Vm.TextoObservacion = "Cambiada sin guardar";
        await b.Vm.CerrarCuadroAgregarCommand.ExecuteAsync(null);

        Assert.Equal("Primera", b.Vm.ObservacionPendiente);
    }

    [Fact]
    public async Task La_observacion_pendiente_se_descarta_al_cerrar_la_ficha()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Algo";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        b.Vm.CerrarFichaCommand.Execute(null);

        Assert.False(b.Vm.HayObservacionPendiente);
    }

    [Fact]
    public async Task Un_tacto_rechazado_conserva_la_observacion_pendiente()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.OnRegistrarTacto = _ => Resultado.Error<Guid>(CodigoError.EstadoDeBaja, "Ese estado está dado de baja.");
        await b.LeerAsync("1001");
        await b.Vm.AbrirObservacionCommand.ExecuteAsync(null);
        b.Vm.TextoObservacion = "Cojera";
        await b.Vm.GuardarObservacionCommand.ExecuteAsync(null);

        await b.ElegirYEntrarAsync(Prenada);

        Assert.Equal("Cojera", b.Vm.ObservacionPendiente);
        Assert.NotNull(b.Vm.Ficha);
    }

    [Fact]
    public async Task Sin_ficha_abierta_tocar_un_estado_no_guarda_nada()
    {
        var b = new Banco();

        await b.ElegirYEntrarAsync(Prenada);

        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Sin_estados_activos_se_puede_leer_y_marcar_pero_no_tactar()
    {
        var b = new Banco();
        b.Campo.Estados = [];
        b.Vaca("1001");
        await b.Vm.IniciarAsync();

        await b.LeerAsync("1001");

        Assert.False(b.Vm.HayEstadosActivos);
        Assert.NotNull(b.Vm.Ficha);   // se marcó y se ve la ficha
    }

    // ------------------------------------------------------------------ aviso "Animal repetido" / "Ya pasó"

    [Fact]
    public async Task Una_vaca_ya_marcada_en_la_manga_actual_muestra_Animal_repetido_con_sonido()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001");

        await b.LeerAsync("1001");

        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);
        Assert.Equal("Animal repetido", b.Vm.AvisoTitulo);
        Assert.Equal("1001", b.Vm.AvisoCaravana);
        Assert.Equal(1, b.Avisos.Repetidos);
        Assert.Empty(b.Campo.Tactos);
        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task Una_vaca_que_ya_paso_en_otra_manga_dice_Ya_paso_con_la_manga()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.YaPaso(Guid.NewGuid(), "1001", Datos.Ultimo(manga: 1));

        await b.LeerAsync("1001");

        Assert.Equal("Ya pasó en la manga 1", b.Vm.AvisoTitulo);
        Assert.Equal(1, b.Avisos.Repetidos);
    }

    [Fact]
    public async Task Ver_ficha_abre_la_ficha_sin_marcar_de_nuevo()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001");
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "1001");
        await b.LeerAsync("1001");

        await b.Vm.VerFichaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal([animal], b.Campo.FichasPedidas);
        Assert.Single(b.Campo.LecturasProcesadas);   // no se procesó otra lectura
    }

    [Fact]
    public async Task Seguir_cierra_el_aviso_y_espera_la_siguiente_lectura()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.Repetido(Guid.NewGuid(), "1001");
        await b.LeerAsync("1001");

        await b.Vm.SeguirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Empty(b.Campo.Deshaceres);
    }

    [Fact]
    public async Task Una_lectura_nueva_con_el_aviso_abierto_lo_cierra_y_se_procesa()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.Repetido(Guid.NewGuid(), "1001");
        var (segunda, _) = b.Vaca("1002");
        await b.LeerAsync("1001");

        await b.LeerAsync("1002");

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(segunda, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Corregir_pide_confirmacion_y_recien_al_confirmar_deshace_lo_ultimo_y_abre_la_ficha()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        var ultimo = Datos.Ultimo(TipoRegistroCampo.Tacto, "PREÑADA");
        b.Campo.Lecturas["1001"] = Datos.Repetido(animal, "1001", ultimo, puedeCorregir: true);
        b.Campo.Fichas[animal] = Datos.Ficha(animal, "1001");
        await b.LeerAsync("1001");
        Assert.True(b.Vm.PuedeCorregir);

        await b.Vm.CorregirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.ConfirmarCorregir, b.Vm.Cuadro);
        Assert.Contains("PREÑADA", b.Vm.TextoCorregir);
        Assert.Empty(b.Campo.Deshaceres);   // todavía no se deshizo nada

        await b.Vm.ConfirmarCorregirCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(TipoDeshacer.Tacto, cmd.Tipo);
        Assert.Equal(ultimo.RegistroId, cmd.Id);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task Corregir_con_No_vuelve_al_aviso_sin_deshacer()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.Repetido(Guid.NewGuid(), "1001", Datos.Ultimo(TipoRegistroCampo.Tacto, "VACIA"), puedeCorregir: true);
        await b.LeerAsync("1001");
        await b.Vm.CorregirCommand.ExecuteAsync(null);

        await b.Vm.CancelarCorregirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Deshaceres);
    }

    [Fact]
    public async Task Si_Corregir_no_se_puede_muestra_el_motivo_y_deja_el_aviso_abierto()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.Repetido(Guid.NewGuid(), "1001", Datos.Ultimo(TipoRegistroCampo.Tacto, "VACIA"), puedeCorregir: true);
        b.Campo.OnDeshacer = _ => Resultado.Error<ResultadoDeshacerDto>(CodigoError.TactoNoEsElUltimo, "Ya no es lo último.");
        await b.LeerAsync("1001");
        await b.Vm.CorregirCommand.ExecuteAsync(null);

        await b.Vm.ConfirmarCorregirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);
        Assert.Equal("Ya no es lo último.", b.Vm.MensajeError);
    }

    [Fact]
    public async Task Corregir_no_se_ofrece_si_lo_ultimo_es_la_marca_de_pasada()
    {
        var b = new Banco();
        b.Campo.Lecturas["1001"] = Datos.Repetido(Guid.NewGuid(), "1001", Datos.Ultimo(TipoRegistroCampo.Marca), puedeCorregir: false);
        await b.LeerAsync("1001");

        await b.Vm.CorregirCommand.ExecuteAsync(null);

        Assert.False(b.Vm.PuedeCorregir);
        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);   // no pasó a la confirmación
    }

    // ------------------------------------------------------------------ caravana no encontrada y búsqueda manual

    [Fact]
    public async Task Una_caravana_inexistente_ofrece_buscar_a_mano_sin_guardar_nada()
    {
        var b = new Banco();

        await b.LeerAsync("9999");

        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        Assert.Equal("9999", b.Vm.CodigoNoEncontrado);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Buscar_a_mano_desde_no_encontrada_cierra_el_cuadro_sin_guardar()
    {
        var b = new Banco();
        await b.LeerAsync("9999");

        await b.Vm.BuscarAManoCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task La_busqueda_manual_marca_igual_que_una_lectura()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        b.Vm.TextoBusqueda = " 1001 ";

        await b.Vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal("1001", b.Campo.LecturasProcesadas.Single().Codigo);   // sin espacios
        Assert.Equal("", b.Vm.TextoBusqueda);
    }

    [Fact]
    public async Task La_busqueda_manual_vacia_no_hace_nada()
    {
        var b = new Banco();
        b.Vm.TextoBusqueda = "   ";

        await b.Vm.BuscarCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.LecturasProcesadas);
    }

    // ------------------------------------------------------------------ aviso de 5 segundos con "Deshacer"

    // El aviso de deshacer de 5 segundos ya no sale al marcar una vaca (con o sin tacto); sale al guardar un tacto o al cerrar una manga.
    // Estas pruebas usan el tacto como origen del aviso.

    private static async Task<Guid> GuardarTactoAsync(Banco b, string caravana, Guid? tactoId = null)
    {
        var id = tactoId ?? Guid.NewGuid();
        b.Campo.OnRegistrarTacto = _ => Resultado.Ok(id);
        await b.LeerAsync(caravana);
        await b.ElegirYEntrarAsync(Prenada);
        return id;
    }

    [Fact]
    public async Task Marcar_una_vaca_no_muestra_el_aviso_de_deshacer()
    {
        var b = new Banco();
        b.Vaca("1001");

        await b.LeerAsync("1001");

        Assert.Null(b.Vm.AvisoDeshacer);
        Assert.Equal(0, b.Temporizador.Pendientes(TrabajoViewModel.DuracionAvisoDeshacer));
    }

    [Fact]
    public async Task El_aviso_de_deshacer_se_va_solo_a_los_5_segundos()
    {
        var b = new Banco();
        b.Vaca("1001");
        await GuardarTactoAsync(b, "1001");
        Assert.NotNull(b.Vm.AvisoDeshacer);
        Assert.Equal(1, b.Temporizador.Pendientes(TrabajoViewModel.DuracionAvisoDeshacer));

        b.Temporizador.Liberar(TrabajoViewModel.DuracionAvisoDeshacer);

        await Esperas.HastaAsync(() => b.Vm.AvisoDeshacer is null);
    }

    [Fact]
    public async Task Deshacer_desde_el_aviso_anula_el_tacto_y_quita_el_aviso()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        b.Campo.OnDeshacer = c => Resultado.Ok(new ResultadoDeshacerDto(c.Tipo, animal, null, null, null));
        var tacto = await GuardarTactoAsync(b, "1001");

        await b.Vm.DeshacerAvisoCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(TipoDeshacer.Tacto, cmd.Tipo);
        Assert.Equal(tacto, cmd.Id);
        Assert.Null(b.Vm.AvisoDeshacer);
        Assert.Equal("Se deshizo el tacto.", b.Vm.UltimoGuardado);
    }

    [Fact]
    public async Task El_aviso_de_deshacer_sobrevive_a_un_aviso_de_repetido()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.Lecturas["2002"] = Datos.Repetido(Guid.NewGuid(), "2002");
        await GuardarTactoAsync(b, "1001");
        var aviso = b.Vm.AvisoDeshacer;

        await b.LeerAsync("2002");

        Assert.Equal(CuadroTrabajo.Aviso, b.Vm.Cuadro);
        Assert.Same(aviso, b.Vm.AvisoDeshacer);
    }

    [Fact]
    public async Task Un_aviso_nuevo_reemplaza_al_anterior_y_cancela_su_tiempo()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Vaca("1002", contador: 2);
        await GuardarTactoAsync(b, "1001");
        var tactoB = await GuardarTactoAsync(b, "1002");

        Assert.Equal(tactoB, b.Vm.AvisoDeshacer!.Id);
        Assert.Equal(1, b.Temporizador.Pendientes(TrabajoViewModel.DuracionAvisoDeshacer));   // el del primero se canceló

        b.Temporizador.Liberar(TrabajoViewModel.DuracionAvisoDeshacer);
        await Esperas.HastaAsync(() => b.Vm.AvisoDeshacer is null);
    }

    [Fact]
    public async Task Si_ya_no_se_puede_deshacer_se_rechaza_con_el_motivo()
    {
        var b = new Banco();
        b.Vaca("1001");
        await GuardarTactoAsync(b, "1001");
        b.Campo.OnDeshacer = _ => Resultado.Error<ResultadoDeshacerDto>(CodigoError.MarcaNoEsLaUltima, "Antes hay que deshacer lo posterior.");

        await b.Vm.DeshacerAvisoCommand.ExecuteAsync(null);

        Assert.Equal("Antes hay que deshacer lo posterior.", b.Vm.MensajeError);
        Assert.Null(b.Vm.AvisoDeshacer);   // el aviso se quita al tocarlo; el motivo queda en el mensaje de error
    }

    // ------------------------------------------------------------------ Finalizar manga

    [Fact]
    public async Task Finalizar_manga_muestra_el_contador_y_las_caravanas_y_Coincide_la_cierra()
    {
        var b = new Banco();
        b.Campo.OnResumen = () => Resultado.Ok(Datos.Resumen(1, "1", "2", "3"));
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: false);

        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);
        Assert.Equal(3, b.Vm.ResumenManga!.Contador);
        Assert.Empty(b.Campo.Finalizados);   // todavía no se cerró

        await b.Vm.CoincideCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Finalizados);
        Assert.Null(cmd.CantidadReal);   // "Coincide"
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(TipoDeshacer.Cierre, b.Vm.AvisoDeshacer!.Tipo);
        Assert.Equal("Manga 2 · 0", b.Vm.TextoManga);   // la siguiente queda por empezar
    }

    [Fact]
    public async Task No_coincide_pide_la_cantidad_real_y_cierra_con_esa_cantidad()
    {
        var b = new Banco();
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        await b.Vm.NoCoincideCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.CantidadReal, b.Vm.Cuadro);
        b.Vm.CantidadRealTexto = "5";
        await b.Vm.ConfirmarCantidadRealCommand.ExecuteAsync(null);

        Assert.Equal(5, b.Campo.Finalizados.Single().CantidadReal);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-2")]
    [InlineData("3,5")]
    public async Task Una_cantidad_real_invalida_muestra_el_motivo_y_no_cierra(string texto)
    {
        var b = new Banco();
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        await b.Vm.NoCoincideCommand.ExecuteAsync(null);
        b.Vm.CantidadRealTexto = texto;

        await b.Vm.ConfirmarCantidadRealCommand.ExecuteAsync(null);

        Assert.Equal(MensajesCampo.Para(CodigoError.CantidadInvalida), b.Vm.MensajeCuadro);
        Assert.Empty(b.Campo.Finalizados);
        Assert.Equal(CuadroTrabajo.CantidadReal, b.Vm.Cuadro);
    }

    [Fact]
    public async Task Volver_a_corregir_cierra_el_cuadro_sin_cerrar_la_manga()
    {
        var b = new Banco();
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        await b.Vm.NoCoincideCommand.ExecuteAsync(null);

        await b.Vm.VolverACorregirCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Finalizar_sin_manga_abierta_avisa_que_no_hay_nada_para_finalizar()
    {
        var b = new Banco();
        b.Campo.OnResumen = () => Resultado.Error<ResumenMangaDto>(CodigoError.NoHayMangaAbierta, MensajesCampo.Para(CodigoError.NoHayMangaAbierta));

        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(MensajesCampo.Para(CodigoError.NoHayMangaAbierta), b.Vm.MensajeError);
    }

    [Fact]
    public async Task Un_cierre_rechazado_deja_el_cuadro_abierto_con_el_motivo()
    {
        var b = new Banco();
        b.Campo.OnFinalizar = _ => Resultado.Error<ResultadoCierreDto>(CodigoError.NoHayMangaAbierta, "No hay una manga abierta.");
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        await b.Vm.CoincideCommand.ExecuteAsync(null);

        Assert.Equal("No hay una manga abierta.", b.Vm.MensajeCuadro);
        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);
    }

    // ------------------------------------------------------------------ Terminar el tacto

    [Fact]
    public async Task Terminar_el_tacto_exige_las_dos_confirmaciones_en_orden_y_recien_ahi_cierra()
    {
        var b = new Banco();

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);          // primera: la verificación
        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);         // saltearse la segunda no sirve
        Assert.Empty(b.Campo.Terminados);

        await b.Vm.CoincideCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.SegundaConfirmacion, b.Vm.Cuadro);   // segunda: "¿Terminar el tacto?"
        Assert.Empty(b.Campo.Terminados);                               // todavía no se cerró nada

        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        Assert.Single(b.Campo.Terminados);
        Assert.Equal(EtapaTrabajo.Terminado, b.Vm.Etapa);
        Assert.True(b.Vm.PuedeDeshacerTerminado);
        Assert.Contains("Manga 1 cerrada", b.Vm.TextoResumenTerminado);
    }

    [Fact]
    public async Task No_seguir_trabajando_no_cierra_nada()
    {
        var b = new Banco();
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.CoincideCommand.ExecuteAsync(null);

        await b.Vm.NoTerminarCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Empty(b.Campo.Terminados);
        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Si_se_interrumpe_entre_las_dos_confirmaciones_no_se_cierra_nada()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.CoincideCommand.ExecuteAsync(null);

        await b.Vm.DetenerAsync();   // la app pasa a segundo plano o se cierra

        Assert.Empty(b.Campo.Terminados);
        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Terminar_con_No_coincide_lleva_la_cantidad_real_a_la_segunda_confirmacion()
    {
        var b = new Banco();
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.NoCoincideCommand.ExecuteAsync(null);
        b.Vm.CantidadRealTexto = "12";
        await b.Vm.ConfirmarCantidadRealCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.SegundaConfirmacion, b.Vm.Cuadro);

        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        Assert.Equal(12, b.Campo.Terminados.Single().CantidadReal);
    }

    [Fact]
    public async Task Terminar_sin_manga_abierta_solo_pide_la_segunda_confirmacion_y_no_cierra_nada()
    {
        var b = new Banco();
        b.Campo.OnResumen = () => Resultado.Error<ResumenMangaDto>(CodigoError.NoHayMangaAbierta, "No hay manga abierta.");
        b.Campo.OnTerminar = _ => Resultado.Ok(new ResultadoCierreDto(null, Datos.Avance()));

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.SegundaConfirmacion, b.Vm.Cuadro);   // sin verificación

        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        Assert.Equal(EtapaTrabajo.Terminado, b.Vm.Etapa);
        Assert.False(b.Vm.PuedeDeshacerTerminado);   // no hay cierre para deshacer
        Assert.Contains("No había una manga abierta", b.Vm.TextoResumenTerminado);
    }

    [Fact]
    public async Task Deshacer_del_resumen_reabre_la_manga_y_vuelve_a_trabajar()
    {
        var b = new Banco();
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.CoincideCommand.ExecuteAsync(null);
        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);
        var cierreId = b.Campo.Terminados.Count == 1 ? b.Vm.ResultadoTerminado!.Cierre!.CierreId : Guid.Empty;

        await b.Vm.DeshacerTerminadoCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(TipoDeshacer.Cierre, cmd.Tipo);
        Assert.Equal(cierreId, cmd.Id);
        Assert.Equal(EtapaTrabajo.Esperando, b.Vm.Etapa);
        Assert.Null(b.Vm.ResultadoTerminado);
    }

    [Fact]
    public async Task Salir_del_resumen_vuelve_a_elegir_rodeo()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.CoincideCommand.ExecuteAsync(null);
        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        await b.Vm.SalirCommand.ExecuteAsync(null);

        Assert.Equal(["Eleccion"], b.Navegacion.Destinos);
        Assert.Null(b.Sesion.Rodeo);
        Assert.False(b.Pantalla.Encendida);
    }

    [Fact]
    public async Task Terminado_el_tacto_las_lecturas_ya_no_marcan_nada()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);
        await b.Vm.CoincideCommand.ExecuteAsync(null);
        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        await b.LeerAsync("1001");

        Assert.Empty(b.Campo.LecturasProcesadas);
    }

    // ------------------------------------------------------------------ lecturas con un cuadro abierto (RF-19)

    [Fact]
    public async Task Con_un_cuadro_abierto_se_guarda_solo_la_ultima_lectura_y_se_procesa_al_cerrarlo()
    {
        var b = new Banco();
        b.Vaca("1001");
        var (ultima, _) = b.Vaca("1002", contador: 2);
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);   // cuadro de verificación abierto

        await b.LeerAsync("1001");
        await b.LeerAsync("1002");
        Assert.Empty(b.Campo.LecturasProcesadas);   // nada se procesó todavía

        await b.Vm.VolverACorregirCommand.ExecuteAsync(null);

        Assert.Equal(["1002"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(ultima, b.Vm.Ficha!.AnimalId);
    }

    // ------------------------------------------------------------------ cantidad de la manga: el contador "7/10"

    private static async Task PonerCantidadAsync(Banco b, string texto)
    {
        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        b.Vm.TextoCantidadManga = texto;
        await b.Vm.GuardarCantidadMangaCommand.ExecuteAsync(null);
    }

    [Fact]
    public void Sin_cantidad_el_contador_cuenta_solo_las_marcadas_como_siempre()
    {
        var b = new Banco();

        Assert.False(b.Vm.HayMeta);
        Assert.Null(b.Vm.MetaManga);
        Assert.Equal("Manga 1 · 0", b.Vm.TextoManga);
        Assert.False(b.Vm.MangaCompleta);
        Assert.False(b.Vm.MangaExcedida);
    }

    [Fact]
    public async Task Poner_la_cantidad_muestra_el_contador_contra_ese_numero_y_cierra_el_cuadro()
    {
        var b = new Banco();
        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.CantidadManga, b.Vm.Cuadro);
        Assert.True(b.Vm.HayCantidadManga);

        b.Vm.TextoCantidadManga = " 10 ";
        await b.Vm.GuardarCantidadMangaCommand.ExecuteAsync(null);

        Assert.Equal(10, b.Vm.MetaManga);
        Assert.True(b.Vm.HayMeta);
        Assert.Equal("Manga 1 · 0/10", b.Vm.TextoManga);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task El_contador_sigue_a_las_marcas_y_avisa_cuando_la_manga_esta_completa_y_cuando_sobran()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");

        b.Vm.Contador = 7;
        Assert.Equal("Manga 1 · 7/10", b.Vm.TextoManga);
        Assert.False(b.Vm.MangaCompleta);
        Assert.False(b.Vm.MangaExcedida);

        b.Vm.Contador = 10;
        Assert.True(b.Vm.MangaCompleta);
        Assert.False(b.Vm.MangaExcedida);

        b.Vm.Contador = 11;
        Assert.False(b.Vm.MangaCompleta);
        Assert.True(b.Vm.MangaExcedida);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("2,5")]
    [InlineData("1000")]
    public async Task Una_cantidad_invalida_muestra_el_motivo_y_no_cierra_ni_cambia_nada(string texto)
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "12");
        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        b.Vm.TextoCantidadManga = texto;

        await b.Vm.GuardarCantidadMangaCommand.ExecuteAsync(null);

        Assert.Equal("Escribí cuántas vacas entran en la manga (de 1 a 999).", b.Vm.MensajeCuadro);
        Assert.Equal(CuadroTrabajo.CantidadManga, b.Vm.Cuadro);
        Assert.Equal(12, b.Vm.MetaManga);
    }

    [Fact]
    public async Task Al_reabrir_el_cuadro_trae_la_cantidad_puesta_y_cancelar_no_la_cambia()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "15");

        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        Assert.Equal("15", b.Vm.TextoCantidadManga);
        b.Vm.TextoCantidadManga = "99";
        await b.Vm.CerrarCantidadMangaCommand.ExecuteAsync(null);

        Assert.Equal(15, b.Vm.MetaManga);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task Al_pasar_a_otra_manga_la_cantidad_se_borra_y_hay_que_ponerla_de_nuevo()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: false);

        await b.Vm.RecalcularAsync();

        Assert.Equal(2, b.Vm.NumeroManga);
        Assert.False(b.Vm.HayMeta);
        Assert.Equal("Manga 2 · 0", b.Vm.TextoManga);
    }

    [Fact]
    public async Task Cerrar_la_manga_con_Coincide_deja_la_siguiente_sin_cantidad()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: false);
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        await b.Vm.CoincideCommand.ExecuteAsync(null);

        Assert.False(b.Vm.HayMeta);
    }

    [Fact]
    public async Task No_se_abre_el_cuadro_de_cantidad_con_otro_cuadro_encima()
    {
        var b = new Banco();
        b.Campo.OnResumen = () => Resultado.Ok(Datos.Resumen(1, "1", "2", "3"));
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);

        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);
    }

    [Fact]
    public async Task La_verificacion_compara_lo_marcado_con_la_cantidad_puesta()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");

        // Faltan: 9 marcadas de 10.
        b.Campo.OnResumen = () => Resultado.Ok(Datos.Resumen(1, "1", "2", "3", "4", "5", "6", "7", "8", "9"));
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.True(b.Vm.HayMetaVerificacion);
        Assert.Equal("Esperadas: 10 · Marcadas: 9 · falta 1", b.Vm.TextoMetaVerificacion);
        await b.Vm.VolverACorregirCommand.ExecuteAsync(null);

        // Sobran: 11 marcadas de 10.
        b.Campo.OnResumen = () => Resultado.Ok(Datos.Resumen(1, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"));
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.Equal("Esperadas: 10 · Marcadas: 11 · sobra 1", b.Vm.TextoMetaVerificacion);
        await b.Vm.VolverACorregirCommand.ExecuteAsync(null);

        // Justas.
        b.Campo.OnResumen = () => Resultado.Ok(Datos.Resumen(1, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"));
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.Equal("Esperadas: 10 · Marcadas: 10 · completa", b.Vm.TextoMetaVerificacion);
    }

    [Fact]
    public async Task Sin_cantidad_la_verificacion_no_muestra_comparacion()
    {
        var b = new Banco();
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        Assert.False(b.Vm.HayMetaVerificacion);
        Assert.Equal("", b.Vm.TextoMetaVerificacion);
    }

    // ------------------------------------------------------------------ control por manga: se prende o se apaga (por establecimiento)

    private static readonly VeterinarioDto OtroVet = new(Guid.Parse("33333333-0000-0000-0000-000000000009"), "Dr. Luis", null);

    private static Guid Establecimiento(Banco b) => b.Sesion.Rodeo!.EstablecimientoId;

    [Fact]
    public async Task Sin_haberlo_elegido_nunca_el_control_por_manga_arranca_apagado()
    {
        var b = new Banco();
        b.Preferencias.ControlPorMangaPorDefecto = null;   // en este establecimiento nunca se eligió

        await b.Vm.IniciarAsync();

        Assert.False(b.Vm.UsaMangas);
        Assert.Equal(Aviss.Application.Puertos.IPreferenciasDispositivo.ControlPorMangaPorDefecto, b.Vm.UsaMangas);
    }

    [Fact]
    public async Task El_control_por_manga_se_carga_de_la_preferencia_del_establecimiento_en_trabajo()
    {
        var b = new Banco();
        b.Preferencias.ControlPorManga[Establecimiento(b)] = false;

        await b.Vm.IniciarAsync();

        Assert.False(b.Vm.UsaMangas);
    }

    [Fact]
    public async Task Cambiar_el_interruptor_guarda_la_preferencia_solo_de_ese_establecimiento()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        Assert.True(b.Vm.UsaMangas);

        b.Vm.UsaMangas = false;

        Assert.Equal([(Establecimiento(b), false)], b.Preferencias.ControlGuardado);
        Assert.Single(b.Preferencias.ControlPorManga);
    }

    [Fact]
    public async Task Cargar_la_preferencia_no_la_vuelve_a_guardar()
    {
        var b = new Banco();
        b.Preferencias.ControlPorManga[Establecimiento(b)] = false;

        await b.Vm.IniciarAsync();

        Assert.Empty(b.Preferencias.ControlGuardado);
    }

    [Fact]
    public async Task Cada_establecimiento_tiene_su_propia_preferencia()
    {
        var b = new Banco();
        var otroEstablecimiento = Guid.NewGuid();
        b.Preferencias.ControlPorManga[Establecimiento(b)] = true;
        b.Preferencias.ControlPorManga[otroEstablecimiento] = false;
        await b.Vm.IniciarAsync();
        Assert.True(b.Vm.UsaMangas);

        b.Sesion.Empezar(Datos.Vet, Datos.Rodeo(establecimientoId: otroEstablecimiento));
        await b.Vm.IniciarAsync();

        Assert.False(b.Vm.UsaMangas);
    }

    [Fact]
    public async Task Cambiar_de_veterinario_no_cambia_la_preferencia_porque_es_del_establecimiento()
    {
        var b = new Banco();
        b.Preferencias.ControlPorManga[Establecimiento(b)] = true;
        await b.Vm.IniciarAsync();

        b.Vm.CambiarVeterinarioCommand.Execute(OtroVet);

        Assert.True(b.Vm.UsaMangas);
        Assert.Empty(b.Preferencias.ControlGuardado);   // cambiar de veterinario no escribe preferencias de mangas
    }

    [Fact]
    public async Task Apagado_se_ocultan_el_conteo_la_cantidad_y_la_comparacion_aunque_hubiera_una_cantidad_puesta()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");
        b.Vm.Contador = 7;
        Assert.Equal("Manga 1 · 7/10", b.Vm.TextoManga);

        b.Vm.UsaMangas = false;

        Assert.False(b.Vm.HayMeta);
        Assert.Null(b.Vm.MetaManga);
        Assert.False(b.Vm.MangaCompleta);
        Assert.Equal("Manga 1 · 7", b.Vm.TextoManga);

        b.Vm.UsaMangas = true;   // al prenderlo de nuevo vuelve la cantidad de esta manga
        Assert.Equal("Manga 1 · 7/10", b.Vm.TextoManga);
    }

    [Fact]
    public async Task Apagado_no_se_abre_el_cuadro_de_cantidad_ni_se_puede_finalizar_la_manga()
    {
        var b = new Banco();
        b.Vm.UsaMangas = false;

        await b.Vm.AbrirCantidadMangaCommand.ExecuteAsync(null);
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Apagado_la_manga_no_se_cierra_sola_al_llegar_a_la_cantidad()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 3, contadorAlMarcar: 3);
        b.Vm.UsaMangas = false;

        await b.LeerAsync("1001");

        Assert.Empty(b.Campo.Finalizados);
        Assert.Equal(0, b.Avisos.MangasCompletas);
    }

    [Fact]
    public async Task Apagado_Terminar_el_tacto_va_directo_a_la_confirmacion_sin_verificar_caravanas()
    {
        var b = new Banco();
        b.Vm.UsaMangas = false;

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.SegundaConfirmacion, b.Vm.Cuadro);
        Assert.Equal("¿Estás seguro que querés terminar?", b.Vm.TextoSegundaConfirmacion);
        Assert.False(b.Vm.HayAdvertenciaMangaAbierta);

        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Terminados);
        Assert.Null(cmd.CantidadReal);
        Assert.Equal(EtapaTrabajo.Terminado, b.Vm.Etapa);
    }

    [Fact]
    public async Task Prendido_Terminar_el_tacto_sigue_pidiendo_la_verificacion_de_la_manga_abierta()
    {
        var b = new Banco();

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);
        Assert.Equal("¿Estás seguro que querés terminar?", b.Vm.TextoSegundaConfirmacion);
    }

    [Fact]
    public void Prendido_y_con_una_manga_abierta_Terminar_advierte_cual_es()
    {
        var b = new Banco();
        b.Vm.MangaAbierta = true;
        b.Vm.Contador = 7;

        Assert.True(b.Vm.HayAdvertenciaMangaAbierta);
        Assert.Equal("Hay una manga abierta (Manga 1 · 7). Al terminar se cierra y no se abre otra.", b.Vm.AdvertenciaMangaAbierta);
    }

    [Fact]
    public async Task La_advertencia_muestra_la_cantidad_puesta_si_la_hay()
    {
        var b = new Banco();
        await PonerCantidadAsync(b, "10");
        b.Vm.MangaAbierta = true;
        b.Vm.Contador = 7;

        Assert.Equal("Hay una manga abierta (Manga 1 · 7/10). Al terminar se cierra y no se abre otra.", b.Vm.AdvertenciaMangaAbierta);
    }

    [Fact]
    public void Prendido_pero_sin_manga_abierta_no_hay_advertencia()
    {
        var b = new Banco();
        b.Vm.MangaAbierta = false;

        Assert.False(b.Vm.HayAdvertenciaMangaAbierta);
        Assert.Equal("", b.Vm.AdvertenciaMangaAbierta);
    }

    [Fact]
    public void Apagado_no_hay_advertencia_de_manga_aunque_haya_una_abierta()
    {
        var b = new Banco();
        b.Vm.MangaAbierta = true;
        b.Vm.Contador = 7;
        b.Vm.UsaMangas = false;

        Assert.False(b.Vm.HayAdvertenciaMangaAbierta);
    }

    [Fact]
    public async Task Con_una_manga_abierta_la_advertencia_se_ve_en_la_confirmacion_final_y_se_puede_terminar()
    {
        var b = new Banco();
        b.Vaca("1001", contador: 3);
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 3);
        await b.LeerAsync("1001");
        b.Vm.CerrarFichaCommand.Execute(null);

        await b.Vm.TerminarElTactoCommand.ExecuteAsync(null);   // verificación de la manga abierta
        await b.Vm.CoincideCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.SegundaConfirmacion, b.Vm.Cuadro);
        Assert.True(b.Vm.HayAdvertenciaMangaAbierta);
        Assert.Contains("Manga 1 · 3", b.Vm.AdvertenciaMangaAbierta);

        await b.Vm.ConfirmarTerminarCommand.ExecuteAsync(null);
        Assert.Single(b.Campo.Terminados);
    }

    [Fact]
    public async Task El_engranaje_abre_y_cierra_la_configuracion_de_un_toque()
    {
        var b = new Banco();

        await b.Vm.AbrirConfiguracionCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Configuracion, b.Vm.Cuadro);
        Assert.True(b.Vm.HayConfiguracion);

        await b.Vm.CerrarConfiguracionCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task La_configuracion_no_se_abre_con_otro_cuadro_encima()
    {
        var b = new Banco();
        await b.Vm.FinalizarMangaCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);

        await b.Vm.AbrirConfiguracionCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Verificacion, b.Vm.Cuadro);
    }

    // ------------------------------------------------------------------ la manga se cierra sola al llegar a su cantidad

    // Deja el banco con una cantidad puesta y la vaca "1001" lista para leer; el servicio devuelve ese contador al marcar.
    private static async Task<Banco> BancoConMangaPorCompletarAsync(int meta, int contadorAlMarcar, bool cvetCero = false)
    {
        var b = new Banco();
        await PonerCantidadAsync(b, meta.ToString());
        if (cvetCero) b.Campo.Lecturas["1001"] = Datos.SinTacto(Guid.NewGuid(), "1001", contador: contadorAlMarcar);
        else b.Vaca("1001", contador: contadorAlMarcar);
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: contadorAlMarcar);
        b.Campo.OnFinalizar = _ =>
        {
            b.Campo.Estado = Datos.EstadoTrabajo(manga: 2, abierta: false);   // la siguiente queda por empezar
            return Resultado.Ok(Datos.ResultadoCierre(null, aviss: contadorAlMarcar));
        };
        return b;
    }

    [Fact]
    public async Task Al_marcar_la_ultima_vaca_de_la_cantidad_la_manga_se_cierra_sola_y_avisa()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 10, contadorAlMarcar: 10);

        await b.LeerAsync("1001");

        var cierre = Assert.Single(b.Campo.Finalizados);
        Assert.Null(cierre.CantidadReal);   // sin verificación: coincide con lo contado
        Assert.Equal(1, b.Avisos.MangasCompletas);
        Assert.Equal("Manga 1 terminada (10/10)", b.Vm.UltimoGuardado);
        var aviso = b.Vm.AvisoDeshacer!;
        Assert.Equal(TipoDeshacer.Cierre, aviso.Tipo);
        Assert.Equal("Manga 1 terminada (10/10)", aviso.Texto);
    }

    [Fact]
    public async Task La_manga_cerrada_sola_deja_la_siguiente_por_empezar_sin_cantidad_y_la_ficha_de_la_ultima_abierta()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 3, contadorAlMarcar: 3);

        await b.LeerAsync("1001");

        Assert.Equal(2, b.Vm.NumeroManga);
        Assert.False(b.Vm.HayMeta);
        Assert.Equal("Manga 2 · 0", b.Vm.TextoManga);
        Assert.NotNull(b.Vm.Ficha);   // la vaca número 10 todavía se puede tactar
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
    }

    [Fact]
    public async Task Antes_de_llegar_a_la_cantidad_la_manga_no_se_cierra()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 10, contadorAlMarcar: 9);

        await b.LeerAsync("1001");

        Assert.Empty(b.Campo.Finalizados);
        Assert.Equal(0, b.Avisos.MangasCompletas);
        Assert.Equal(1, b.Vm.NumeroManga);
    }

    [Fact]
    public async Task Sin_cantidad_puesta_la_manga_no_se_cierra_sola_por_mas_vacas_que_tenga()
    {
        var b = new Banco();
        b.Vaca("1001", contador: 40);
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 40);

        await b.LeerAsync("1001");

        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Una_vaca_con_cvet_cero_que_completa_la_manga_tambien_la_cierra()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 5, contadorAlMarcar: 5, cvetCero: true);

        await b.LeerAsync("1001");

        Assert.Single(b.Campo.Finalizados);
        Assert.Equal(1, b.Avisos.SinTactos);        // sonó la chicharra
        Assert.Equal(1, b.Avisos.MangasCompletas);   // y se avisó que la manga terminó
        Assert.Null(b.Vm.Ficha);
    }

    [Fact]
    public async Task Si_el_cierre_automatico_falla_no_se_bloquea_el_trabajo_y_se_muestra_el_motivo()
    {
        var b = await BancoConMangaPorCompletarAsync(meta: 4, contadorAlMarcar: 4);
        b.Campo.OnFinalizar = _ => Resultado.Error<ResultadoCierreDto>(CodigoError.NoHayMangaAbierta, "No hay una manga abierta.");

        await b.LeerAsync("1001");

        Assert.Equal("No hay una manga abierta.", b.Vm.MensajeInfo);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Equal(0, b.Avisos.MangasCompletas);
    }

    // ------------------------------------------------------------------ cabecera contraíble y horarios de la primera y última vaca

    [Fact]
    public void La_cabecera_arranca_abierta_y_la_flechita_la_contrae_y_la_abre()
    {
        var b = new Banco();
        Assert.True(b.Vm.CabeceraAbierta);
        Assert.Equal("▲", b.Vm.TextoFlecha);

        b.Vm.AlternarCabeceraCommand.Execute(null);
        Assert.False(b.Vm.CabeceraAbierta);
        Assert.Equal("▼", b.Vm.TextoFlecha);

        b.Vm.AlternarCabeceraCommand.Execute(null);
        Assert.True(b.Vm.CabeceraAbierta);
    }

    [Fact]
    public async Task La_barra_de_arriba_muestra_el_rodeo_y_con_una_ficha_el_id_y_el_cvet()
    {
        var b = new Banco();
        var animal = Guid.NewGuid();
        b.Campo.Lecturas["1014"] = Datos.Marcada(animal, "1014") with { Ficha = Datos.Ficha(animal, "1014", cvet: "PRE") };
        Assert.Equal(b.Vm.NombreRodeo, b.Vm.TituloBarra);   // sin ficha: el nombre del rodeo

        await b.LeerAsync("1014");
        Assert.Equal("ID 1014 · CVET PRE", b.Vm.TituloBarra);

        b.Vm.CerrarFichaCommand.Execute(null);
        Assert.Equal(b.Vm.NombreRodeo, b.Vm.TituloBarra);
    }

    [Fact]
    public async Task Los_horarios_muestran_la_primera_y_la_ultima_vaca()
    {
        var b = new Banco();
        var primera = new DateTime(2026, 11, 17, 12, 5, 0, DateTimeKind.Utc);
        var ultima = new DateTime(2026, 11, 17, 14, 40, 0, DateTimeKind.Utc);
        b.Campo.Estado = Datos.EstadoTrabajo(avance: Datos.Avance(primera: primera, fin: ultima));

        await b.Vm.IniciarAsync();

        Assert.Equal($"Primera vaca: {primera.ToLocalTime():HH:mm} · Última vaca: {ultima.ToLocalTime():HH:mm}", b.Vm.TextoHorarios);
    }

    [Fact]
    public async Task Al_terminar_el_tacto_el_horario_final_dice_Termino()
    {
        var b = new Banco();
        var primera = new DateTime(2026, 11, 17, 12, 5, 0, DateTimeKind.Utc);
        var fin = new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);
        b.Campo.Estado = Datos.EstadoTrabajo(avance: Datos.Avance(primera: primera, fin: fin, terminado: true));

        await b.Vm.IniciarAsync();

        Assert.Equal($"Primera vaca: {primera.ToLocalTime():HH:mm} · Terminó: {fin.ToLocalTime():HH:mm}", b.Vm.TextoHorarios);
    }

    [Fact]
    public async Task Sin_vacas_hoy_los_horarios_lo_dicen()
    {
        var b = new Banco();

        await b.Vm.IniciarAsync();

        Assert.Equal("Todavía no hay vacas hoy", b.Vm.TextoHorarios);
    }

    // ------------------------------------------------------------------ refresco (RF-30)

    [Fact]
    public async Task Recalcula_el_avance_cada_60_segundos()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await Esperas.HastaAsync(() => b.Temporizador.Pendientes(TrabajoViewModel.IntervaloRefresco) == 1);
        var antes = b.Campo.ConsultasDeEstado;
        b.Campo.Estado = Datos.EstadoTrabajo(procesadas: 40);

        b.Temporizador.Liberar(TrabajoViewModel.IntervaloRefresco);

        await Esperas.HastaAsync(() => b.Campo.ConsultasDeEstado > antes);
        await Esperas.HastaAsync(() => b.Vm.Avance!.Procesadas == 40);
    }

    [Fact]
    public async Task Recalcula_al_volver_de_segundo_plano_sin_cerrar_ninguna_manga()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        b.Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true, contador: 4, procesadas: 4);

        await b.Vm.RecalcularAsync();

        Assert.Equal("Manga 1 · 4", b.Vm.TextoManga);
        Assert.Empty(b.Campo.Finalizados);
        Assert.Empty(b.Campo.Terminados);
    }

    [Fact]
    public async Task Detener_corta_el_refresco()
    {
        var b = new Banco();
        await b.Vm.IniciarAsync();
        await Esperas.HastaAsync(() => b.Temporizador.Pendientes(TrabajoViewModel.IntervaloRefresco) == 1);

        await b.Vm.DetenerAsync();

        Assert.Equal(0, b.Temporizador.Pendientes(TrabajoViewModel.IntervaloRefresco));
    }

    // ------------------------------------------------------------------ veterinario, simulador, navegación

    [Fact]
    public void Cambiar_de_veterinario_cambia_quien_firma_y_lo_recuerda_sin_cerrar_la_manga()
    {
        var b = new Banco();
        var otro = new Aviss.Application.Campo.VeterinarioDto(Guid.NewGuid(), "Dr. Luis", null);

        b.Vm.CambiarVeterinarioCommand.Execute(otro);

        Assert.Equal("Dr. Luis", b.Vm.NombreVeterinario);
        Assert.Equal(otro.Id, b.Sesion.Veterinario!.Id);
        Assert.Equal(otro.Id, b.Preferencias.Recordado);
        Assert.Empty(b.Campo.Finalizados);
    }

    [Fact]
    public async Task Los_registros_nuevos_llevan_el_veterinario_activo_del_momento()
    {
        var b = new Banco();
        b.Vaca("1001");
        var otro = new Aviss.Application.Campo.VeterinarioDto(Guid.NewGuid(), "Dr. Luis", null);
        b.Vm.CambiarVeterinarioCommand.Execute(otro);

        await b.LeerAsync("1001");

        Assert.Equal(otro.Id, b.Campo.LecturasProcesadas.Single().UsuarioId);
    }

    [Fact]
    public void Sin_simulador_el_panel_no_existe_y_los_comandos_no_hacen_nada()
    {
        var b = new Banco(conSimulador: false);

        Assert.False(b.Vm.HaySimulador);
        b.Vm.SimularInexistenteCommand.Execute(null);
        Assert.Empty(b.Lector.Emitidos);
    }

    [Fact]
    public async Task El_simulador_emite_una_caravana_de_muestra_una_inexistente_y_una_ya_leida()
    {
        var b = new Banco();
        b.Campo.CaravanaMuestra = "777";

        await b.Vm.SimularExistenteCommand.ExecuteAsync(null);
        await b.Vm.SimularYaLeidaCommand.ExecuteAsync(null);
        b.Vm.SimularInexistenteCommand.Execute(null);

        Assert.True(b.Vm.HaySimulador);
        Assert.Equal(["777", "777", TrabajoViewModel.CodigoSimuladoInexistente], b.Lector.Emitidos);
    }

    [Fact]
    public async Task Sin_caravana_de_muestra_el_simulador_avisa()
    {
        var b = new Banco();
        b.Campo.CaravanaMuestra = null;

        await b.Vm.SimularExistenteCommand.ExecuteAsync(null);

        Assert.Equal(TrabajoViewModel.MensajeSinMuestra, b.Vm.MensajeError);
        Assert.Empty(b.Lector.Emitidos);
    }

    [Fact]
    public async Task Mostrar_movimientos_navega_a_la_lista()
    {
        var b = new Banco();

        await b.Vm.MostrarMovimientosCommand.ExecuteAsync(null);

        Assert.Equal(["Movimientos"], b.Navegacion.Destinos);
    }
}
