using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

// Prueban la pantalla de trabajo sin MAUI: la fachada de campo, el lector, el tiempo y la plataforma son falsos.
public class TrabajoViewModelTests
{
    private static readonly EstadoActivoDto Prenada = new(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA");
    private static readonly EstadoActivoDto Vacia = new(Guid.Parse("33333333-0000-0000-0000-000000000002"), "VACIA");

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
        var (animal, marca) = b.Vaca("1001", manga: 1, contador: 1);
        b.Campo.Estado = Datos.EstadoTrabajo(abierta: true, contador: 1, procesadas: 1);

        await b.LeerAsync("1001");

        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal("Manga 1 · 1", b.Vm.TextoManga);
        Assert.Equal("Marcada: 1001", b.Vm.UltimoGuardado);
        Assert.Equal(1, b.Avisos.Confirmaciones);
        // El aviso de deshacer es de la marca (RF-27).
        Assert.Equal(new AvisoDeshacerDto(TipoDeshacer.Marca, marca, "Marcada 1001"), b.Vm.AvisoDeshacer);
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

    // ------------------------------------------------------------------ tacto con un toque

    [Fact]
    public async Task Un_toque_en_un_estado_guarda_el_tacto_y_deja_todo_listo_para_la_siguiente()
    {
        var b = new Banco();
        var (animal, _) = b.Vaca("1001");
        var tactoId = Guid.NewGuid();
        b.Campo.OnRegistrarTacto = _ => Resultado.Ok(tactoId);
        await b.LeerAsync("1001");

        await b.Vm.SeleccionarEstadoCommand.ExecuteAsync(Prenada);

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

        await b.Vm.SeleccionarEstadoCommand.ExecuteAsync(Prenada);

        Assert.Equal("Ese estado está dado de baja.", b.Vm.MensajeError);
        Assert.NotNull(b.Vm.Ficha);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
    }

    [Fact]
    public async Task Sin_ficha_abierta_tocar_un_estado_no_guarda_nada()
    {
        var b = new Banco();

        await b.Vm.SeleccionarEstadoCommand.ExecuteAsync(Prenada);

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

    // ------------------------------------------------------------------ caravana no encontrada, alta y búsqueda manual

    [Fact]
    public async Task Una_caravana_inexistente_ofrece_alta_o_busqueda_sin_guardar_nada()
    {
        var b = new Banco();

        await b.LeerAsync("9999");

        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        Assert.Equal("9999", b.Vm.CodigoNoEncontrado);
        Assert.Empty(b.Campo.Altas);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task Dar_de_alta_precarga_el_codigo_leido_y_guarda_la_caravana_tipeada_con_el_codigo_leido()
    {
        var b = new Banco();
        await b.LeerAsync("9999");

        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);
        Assert.Equal(CuadroTrabajo.Alta, b.Vm.Cuadro);
        Assert.Equal("9999", b.Vm.AltaCaravana);   // precarga editable

        b.Vm.AltaCaravana = "1234";
        await b.Vm.ConfirmarAltaCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Altas);
        Assert.Equal("1234", cmd.NumeroCaravana);
        Assert.Equal("9999", cmd.CodigoLeido);
        Assert.Equal(Datos.Vet.Id, cmd.UsuarioId);
        Assert.Equal("1234", b.Vm.Ficha!.Caravana);
        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Equal(TipoDeshacer.Alta, b.Vm.AvisoDeshacer!.Tipo);
        Assert.True(b.Vm.PuedeAnularAlta);
    }

    [Fact]
    public async Task Un_alta_rechazada_muestra_el_motivo_y_se_puede_corregir_la_caravana()
    {
        var b = new Banco();
        b.Campo.OnAlta = _ => Resultado.Error<ResultadoAltaDto>(CodigoError.CaravanaDuplicada, "Esa caravana ya existe en el rodeo.");
        await b.LeerAsync("9999");
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);

        await b.Vm.ConfirmarAltaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Alta, b.Vm.Cuadro);
        Assert.Equal("Esa caravana ya existe en el rodeo.", b.Vm.MensajeCuadro);
        Assert.Null(b.Vm.Ficha);
        Assert.Null(b.Vm.AvisoDeshacer);
    }

    [Fact]
    public async Task Un_alta_con_codigo_inutil_como_identificador_avisa_que_no_se_guardo()
    {
        var b = new Banco();
        b.Campo.OnAlta = c => Resultado.Ok(Datos.ResultadoAlta(c.NumeroCaravana, idOmitido: true));
        await b.LeerAsync("9999");
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);
        b.Vm.AltaCaravana = "1234";

        await b.Vm.ConfirmarAltaCommand.ExecuteAsync(null);

        Assert.Equal(TrabajoViewModel.MensajeIdOmitido, b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task El_alta_que_viene_de_buscar_a_mano_no_lleva_codigo_leido()
    {
        var b = new Banco();
        await b.Vm.EncolarLecturaAsync("5555", esManual: true);
        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);

        await b.Vm.ConfirmarAltaCommand.ExecuteAsync(null);

        Assert.Null(b.Campo.Altas.Single().CodigoLeido);
        Assert.Equal("5555", b.Campo.Altas.Single().NumeroCaravana);
    }

    [Fact]
    public async Task Buscar_a_mano_desde_no_encontrada_cierra_el_cuadro_sin_guardar()
    {
        var b = new Banco();
        await b.LeerAsync("9999");

        await b.Vm.BuscarAManoCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.Ninguno, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Altas);
    }

    [Fact]
    public async Task Cancelar_el_alta_vuelve_a_no_encontrada()
    {
        var b = new Banco();
        await b.LeerAsync("9999");
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);

        await b.Vm.CancelarAltaCommand.ExecuteAsync(null);

        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        Assert.Empty(b.Campo.Altas);
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

    [Fact]
    public async Task Anular_el_alta_desde_la_ficha_cierra_la_ficha_y_avisa_que_la_caravana_quedo_libre()
    {
        var b = new Banco();
        b.Campo.OnAlta = c => Resultado.Ok(Datos.ResultadoAlta(c.NumeroCaravana, animalId: Guid.Parse("44444444-0000-0000-0000-000000000001")));
        await b.LeerAsync("9999");
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);
        await b.Vm.ConfirmarAltaCommand.ExecuteAsync(null);

        await b.Vm.AnularAltaCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(TipoDeshacer.Alta, cmd.Tipo);
        Assert.Equal(Guid.Parse("44444444-0000-0000-0000-000000000001"), cmd.Id);   // en un alta, el id es el del animal
        Assert.Null(b.Vm.Ficha);
        Assert.Contains("quedó libre", b.Vm.MensajeInfo);
    }

    // ------------------------------------------------------------------ aviso de 5 segundos con "Deshacer"

    [Fact]
    public async Task El_aviso_de_deshacer_se_va_solo_a_los_5_segundos()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("1001");
        Assert.NotNull(b.Vm.AvisoDeshacer);
        Assert.Equal(1, b.Temporizador.Pendientes(TrabajoViewModel.DuracionAvisoDeshacer));

        b.Temporizador.Liberar(TrabajoViewModel.DuracionAvisoDeshacer);

        await Esperas.HastaAsync(() => b.Vm.AvisoDeshacer is null);
    }

    [Fact]
    public async Task Deshacer_desde_el_aviso_desmarca_la_vaca_cierra_la_ficha_y_baja_el_contador()
    {
        var b = new Banco();
        var (animal, marca) = b.Vaca("1001");
        b.Campo.OnDeshacer = c => Resultado.Ok(new ResultadoDeshacerDto(c.Tipo, animal, null, 1, 0));
        await b.LeerAsync("1001");

        await b.Vm.DeshacerAvisoCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(TipoDeshacer.Marca, cmd.Tipo);
        Assert.Equal(marca, cmd.Id);
        Assert.Null(b.Vm.Ficha);                 // la vaca vuelve a pendiente: se cierra su ficha
        Assert.Equal(0, b.Vm.Contador);
        Assert.Null(b.Vm.AvisoDeshacer);
    }

    [Fact]
    public async Task El_aviso_de_deshacer_sobrevive_a_un_aviso_de_repetido()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.Lecturas["2002"] = Datos.Repetido(Guid.NewGuid(), "2002");
        await b.LeerAsync("1001");
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
        var (_, marcaB) = b.Vaca("1002", contador: 2);
        await b.LeerAsync("1001");
        await b.LeerAsync("1002");

        Assert.Equal(marcaB, b.Vm.AvisoDeshacer!.Id);
        Assert.Equal(1, b.Temporizador.Pendientes(TrabajoViewModel.DuracionAvisoDeshacer));   // el del primero se canceló

        b.Temporizador.Liberar(TrabajoViewModel.DuracionAvisoDeshacer);
        await Esperas.HastaAsync(() => b.Vm.AvisoDeshacer is null);
    }

    [Fact]
    public async Task Si_ya_no_se_puede_deshacer_se_rechaza_con_el_motivo()
    {
        var b = new Banco();
        b.Vaca("1001");
        b.Campo.OnDeshacer = _ => Resultado.Error<ResultadoDeshacerDto>(CodigoError.MarcaNoEsLaUltima, "Antes hay que deshacer lo posterior.");
        await b.LeerAsync("1001");

        await b.Vm.DeshacerAvisoCommand.ExecuteAsync(null);

        Assert.Equal("Antes hay que deshacer lo posterior.", b.Vm.MensajeError);
        Assert.NotNull(b.Vm.Ficha);   // nada cambió
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

    [Fact]
    public async Task Una_lectura_con_el_formulario_de_alta_abierto_no_pierde_lo_que_se_estaba_cargando()
    {
        var b = new Banco();
        b.Vaca("1001");
        await b.LeerAsync("9999");
        await b.Vm.DarDeAltaCommand.ExecuteAsync(null);
        b.Vm.AltaCaravana = "1234";

        await b.LeerAsync("1001");

        Assert.Equal("1234", b.Vm.AltaCaravana);
        Assert.Equal(CuadroTrabajo.Alta, b.Vm.Cuadro);
        Assert.Single(b.Campo.LecturasProcesadas);   // solo la de "9999": la otra espera
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
