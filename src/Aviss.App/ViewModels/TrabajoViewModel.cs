using System.Collections.ObjectModel;
using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// El corazón de la pantalla de trabajo (spec 003). Decide qué hacer con cada lectura según lo que haya en pantalla
/// (diagrama de estados del plan) y orquesta las acciones de campo: marca automática, tacto con un toque, aviso de
/// repetido, búsqueda manual, deshacer, "Finalizar manga" y "Terminar el tacto".
///
/// No usa tipos de MAUI: todo lo de la tablet (lector, vibración, pantalla encendida, tiempo, navegación, preferencias)
/// entra por interfaces, así que se prueba con xUnit. La lógica de negocio vive en <see cref="ICampoService"/>; acá solo
/// se coordina la pantalla.
/// </summary>
public sealed partial class TrabajoViewModel : ObservableObject
{
    // Mensajes fijos: nunca llevan caravanas ni nombres de tambos (RF-33).
    public const string MensajeFalla = "Algo falló y no se guardó nada. Probá de nuevo.";
    public const string MensajeLectorNoConecta = "No se pudo conectar el lector. Podés buscar la caravana a mano.";
    public const string MensajeSinMuestra = "No hay ninguna caravana de ese tipo para simular.";

    /// <summary>Código que el simulador emite para "una caravana que no existe en el rodeo" (solo desarrollo).</summary>
    public const string CodigoSimuladoInexistente = "SIM-NO-EXISTE";

    public static readonly TimeSpan DuracionAvisoDeshacer = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan IntervaloRefresco = TimeSpan.FromSeconds(60);

    private readonly ICampoService _campo;
    private readonly IRfidReader _lector;
    private readonly IPreferenciasMangas _preferencias;
    private readonly IAvisosCampo _avisos;
    private readonly IPantalla _pantalla;
    private readonly ITemporizador _temporizador;
    private readonly INavegacion _navegacion;
    private readonly SesionTrabajo _sesion;
    private readonly ISimuladorLecturas? _simulador;   // nulo en la versión que se distribuye (RF-31)
    private readonly IRelojService? _reloj;               // para el "hace cuánto" de las esperas; sin reloj usa la hora del sistema

    private readonly ColaDeLecturas _cola;
    // Las operaciones de campo se hacen de a una: dos transacciones en el mismo archivo no deben correr en paralelo.
    private readonly SemaphoreSlim _operacion = new(1, 1);

    private CancellationTokenSource? _ctsLector;
    private CancellationTokenSource? _ctsRefresco;
    private CancellationTokenSource? _ctsAviso;
    private Guid? _rodeoIniciado;

    // Lo que se recuerda entre cuadros.
    private PropositoCierre _proposito;
    private int? _cantidadRealConfirmada;

    public TrabajoViewModel(
        ICampoService campo, IRfidReader lector, IPreferenciasMangas preferencias, IAvisosCampo avisos,
        IPantalla pantalla, ITemporizador temporizador, INavegacion navegacion, SesionTrabajo sesion,
        ISimuladorLecturas? simulador = null, IRelojService? reloj = null,
        IPreferenciasBaston? preferenciasBaston = null, Aviss.Rfid.IBluetoothBaston? bluetooth = null)
    {
        _preferenciasBaston = preferenciasBaston;
        _bluetooth = bluetooth;
        _campo = campo;
        _lector = lector;
        _preferencias = preferencias;
        _avisos = avisos;
        _pantalla = pantalla;
        _temporizador = temporizador;
        _navegacion = navegacion;
        _sesion = sesion;
        _simulador = simulador;
        _reloj = reloj;
        _cola = new ColaDeLecturas(ProcesarLecturaAsync);
        Selector = new SelectorEventosViewModel(permiteNuevo: true) { AlElegir = ElegirEventoAsync };
        CargarPreferenciasDelEstablecimiento();
    }

    // ================================================================== encabezado (RF-3)

    public string NombreRodeo => _sesion.Rodeo?.RodeoNombre ?? "";
    public string NombreEstablecimiento => _sesion.Rodeo?.EstablecimientoNombre ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoManga), nameof(TextoSegundaConfirmacion), nameof(MetaManga), nameof(HayMeta), nameof(MangaCompleta), nameof(MangaExcedida))]
    private int _numeroManga = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoManga), nameof(MangaCompleta), nameof(MangaExcedida), nameof(AdvertenciaMangaAbierta))]
    private int _contador;

    /// <summary>False = manga por empezar: la primera marca la abre (RF-4).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAdvertenciaMangaAbierta), nameof(AdvertenciaMangaAbierta))]
    private bool _mangaAbierta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAvance), nameof(TextoHorarios))]
    private AvanceRodeoDto? _avance;

    /// <summary>"Manga 2 · 14": el número y el contador, siempre visibles.</summary>
    public string TextoManga => MetaManga is { } meta ? $"Manga {NumeroManga} · {Contador}/{meta}" : $"Manga {NumeroManga} · {Contador}";

    /// <summary>Avance del día con su fecha (RF-30).</summary>
    public string TextoAvance => Avance is null
        ? ""
        : $"{Avance.Fecha:dd/MM}: {Avance.Procesadas} de {Avance.TotalVacas} ({Avance.Porcentaje} %) · pendientes {Avance.Pendientes}";

    /// <summary>
    /// Los horarios del día: la hora de la primera vaca y la de la última (o la de "Terminar el tacto" cuando se apretó).
    /// En hora local de la tablet.
    /// </summary>
    public string TextoHorarios => Avance is { PrimeraVacaUtc: { } primera } a
        ? $"Primera vaca: {Hora(primera)} · {(a.Terminado ? "Terminó" : "Última vaca")}: {Hora(a.FinUtc ?? primera)}"
        : "Todavía no hay vacas hoy";

    private static string Hora(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    // ---- Cabecera contraíble: una barra siempre visible con la flechita, y debajo los datos del trabajo

    /// <summary>Los datos de arriba (rodeo, avance, horarios y botones de cierre) están a la vista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoFlecha))]
    private bool _cabeceraAbierta = true;

    public string TextoFlecha => CabeceraAbierta ? "▲" : "▼";

    [RelayCommand]
    private void AlternarCabecera() => CabeceraAbierta = !CabeceraAbierta;

    /// <summary>Lo que muestra la barra de arriba aunque esté contraída: el ID y el CVET de la ficha abierta, o el rodeo si no hay.</summary>
    public string TituloBarra => HayFicha ? TituloFicha : NombreRodeo;

    // ================================================================== el centro de la pantalla

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFicha), nameof(HayResumenTerminado), nameof(EstaEsperando), nameof(TituloBarra), nameof(HayVerFichaSinTacto))]
    [NotifyPropertyChangedFor(nameof(HayMotivoTacto), nameof(MostrarCartelCvetCero), nameof(PuedePonerEnEspera), nameof(EsEsperaAbierta))]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private EtapaTrabajo _etapa = EtapaTrabajo.Esperando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFicha))]
    [NotifyPropertyChangedFor(nameof(HayMotivoTacto), nameof(TextoMotivoTacto), nameof(MostrarCartelCvetCero), nameof(TituloFicha), nameof(FilasDatos), nameof(TituloBarra), nameof(PuedePonerEnEspera), nameof(EsEsperaAbierta), nameof(HayRechazoVigente), nameof(TextoRechazoVigente))]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private FichaAnimalDto? _ficha;

    // ================================================================== cuadros (capas)

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private CuadroTrabajo _cuadro = CuadroTrabajo.Ninguno;

    public bool HayAviso => Cuadro == CuadroTrabajo.Aviso;
    public bool HayNoEncontrada => Cuadro == CuadroTrabajo.NoEncontrada;
    public bool HayConfirmarCorregir => Cuadro == CuadroTrabajo.ConfirmarCorregir;
    public bool HayVerificacion => Cuadro == CuadroTrabajo.Verificacion;
    public bool HayCantidadReal => Cuadro == CuadroTrabajo.CantidadReal;
    public bool HaySegundaConfirmacion => Cuadro == CuadroTrabajo.SegundaConfirmacion;
    public bool HayEventos => Cuadro == CuadroTrabajo.Eventos;
    public bool HayObservacion => Cuadro == CuadroTrabajo.Observacion;
    public bool HayCuadro => Cuadro != CuadroTrabajo.Ninguno;

    partial void OnCuadroChanged(CuadroTrabajo value)
    {
        OnPropertyChanged(nameof(HayAviso));
        OnPropertyChanged(nameof(HayNoEncontrada));
        OnPropertyChanged(nameof(HayConfirmarCorregir));
        OnPropertyChanged(nameof(HayVerificacion));
        OnPropertyChanged(nameof(HayCantidadReal));
        OnPropertyChanged(nameof(HaySegundaConfirmacion));
        OnPropertyChanged(nameof(HayEventos));
        OnPropertyChanged(nameof(HayCantidadManga));
        OnPropertyChanged(nameof(HayConfiguracion));
        OnPropertyChanged(nameof(HayMetaVerificacion));
        OnPropertyChanged(nameof(TextoMetaVerificacion));
        OnPropertyChanged(nameof(HayObservacion));
        OnPropertyChanged(nameof(HayCuadro));
        OnPropertyChanged(nameof(PuedePonerEnEspera));
        OnPropertyChanged(nameof(HayListaEspera));
        OnPropertyChanged(nameof(HayConfirmarReemplazo));
        OnPropertyChanged(nameof(TextoSegundaConfirmacion));
        OnPropertyChanged(nameof(HayAdvertenciaMangaAbierta));
        OnPropertyChanged(nameof(AdvertenciaMangaAbierta));
    }

    // Los cuadros que piden datos o confirmación frenan las lecturas; "Aviso" y "NoEncontrada" no (RF-19, RF-22b).
    private static bool Bloquea(CuadroTrabajo cuadro) => cuadro is
        CuadroTrabajo.ConfirmarCorregir or CuadroTrabajo.Verificacion
        or CuadroTrabajo.CantidadReal or CuadroTrabajo.SegundaConfirmacion
        or CuadroTrabajo.Eventos or CuadroTrabajo.Observacion or CuadroTrabajo.CantidadManga or CuadroTrabajo.Configuracion
        or CuadroTrabajo.ListaEspera or CuadroTrabajo.ConfirmarReemplazo;

    /// <summary>Abre o cierra un cuadro y avisa a la cola de lecturas: al cerrar uno que bloqueaba, se procesa la pendiente.</summary>
    private async Task CambiarCuadroAsync(CuadroTrabajo nuevo)
    {
        Cuadro = nuevo;
        if (Bloquea(nuevo)) _cola.Bloquear();
        else await _cola.DesbloquearAsync();
    }

    /// <summary>Un mensaje dentro del cuadro abierto (por ejemplo, un rechazo al confirmar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeCuadro))]
    private string? _mensajeCuadro;

    public bool HayMensajeCuadro => !string.IsNullOrEmpty(MensajeCuadro);

    // ---- Caravana no encontrada (RF-24)

    [ObservableProperty]
    private string _codigoNoEncontrado = "";


    // ---- Búsqueda manual (RF-23)

    [ObservableProperty]
    private string _textoBusqueda = "";

    // ================================================================== simulador (RF-31)

    /// <summary>False en la versión que se distribuye: la pantalla no muestra el panel.</summary>
    public bool HaySimulador => _simulador is not null;

    // ================================================================== arranque y ciclo de vida

    /// <summary>
    /// La página lo llama al aparecer. La primera vez empieza el trabajo (retoma la manga abierta o deja la siguiente por
    /// empezar; cierra como automática la de un día anterior) y carga los estados; siempre arranca el lector, el refresco
    /// y mantiene la pantalla encendida. Si no hay rodeo elegido, vuelve a la elección.
    /// </summary>
    public async Task IniciarAsync()
    {
        if (_sesion.Rodeo is not { } rodeo)
        {
            await _navegacion.IrAEleccionTrabajoAsync();
            return;
        }

        CargarPreferenciasDelEstablecimiento();
        OnPropertyChanged(nameof(NombreRodeo));
        OnPropertyChanged(nameof(TituloBarra));
        OnPropertyChanged(nameof(NombreEstablecimiento));
        _pantalla.MantenerEncendida(true);

        try
        {
            // Solo la primera vez: volver de "Últimos movimientos" no debe cerrar la manga si ya cruzó la medianoche (RF-10).
            if (_rodeoIniciado != rodeo.RodeoId)
            {
                var inicio = await EnCampoAsync(() => _campo.IniciarTrabajoAsync(rodeo.RodeoId));
                if (!inicio.EsCorrecto)
                {
                    MensajeError = inicio.Mensaje;
                    return;
                }
                AplicarEstado(inicio.Valor);
                _rodeoIniciado = rodeo.RodeoId;

                await CargarEstadosAsync();
            }
            else
            {
                await RecalcularAsync();
            }

            // Las esperas viven en la base: se leen al entrar (también al volver de "Últimos movimientos").
            await CargarEsperasAsync();
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }

        IniciarLector();
        IniciarRefresco();
    }

    /// <summary>La página lo llama al desaparecer: corta el lector y el refresco y deja que la pantalla se apague.</summary>
    public async Task DetenerAsync()
    {
        DesuscribirBaston();   // antes de cortar: el corte no debe mover el cartel de una pantalla que ya se fue
        _ctsLector?.Cancel();
        _ctsLector = null;
        _ctsRefresco?.Cancel();
        _ctsRefresco = null;
        _pantalla.MantenerEncendida(false);
        try { await _lector.DesconectarAsync(); }
        catch (Exception) { /* cortar la escucha nunca debe fallar la pantalla */ }
    }

    /// <summary>
    /// Vuelve a pedir el contador y el avance. Lo llama la página al volver de segundo plano y el refresco de cada 60
    /// segundos: así se nota el cambio de día o de zona horaria sin cerrar ninguna manga (RF-30, RF-10).
    /// </summary>
    public async Task RecalcularAsync()
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        try
        {
            var estado = await EnCampoAsync(() => _campo.ObtenerEstadoTrabajoAsync(rodeo.RodeoId));
            if (estado.EsCorrecto) AplicarEstado(estado.Valor);
        }
        catch (Exception)
        {
            // Un refresco que falla no debe molestar: el próximo lo reintenta.
        }
    }

    private void AplicarEstado(EstadoTrabajoDto estado)
    {
        NumeroManga = estado.NumeroManga;
        MangaAbierta = estado.MangaAbierta;
        Contador = estado.Contador;
        Avance = estado.Avance;
    }

    private void IniciarLector()
    {
        SuscribirBaston();   // el cartel del bastón sigue al lector mientras la pantalla está abierta (spec 008)
        if (_ctsLector is not null) return;
        var cts = _ctsLector = new CancellationTokenSource();
        _ = LeerAsync(cts.Token);
    }

    // Escucha al lector y pone cada código en la cola. No espera a que se procese: la cola decide el orden y qué se
    // conserva cuando hay un cuadro abierto.
    private async Task LeerAsync(CancellationToken ct)
    {
        try
        {
            await _lector.ConectarAsync(ct);
            await foreach (var lectura in _lector.Lecturas(ct))
                _ = EncolarLecturaAsync(lectura.Codigo);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            MensajeError = MensajeLectorNoConecta;
        }
    }

    private void IniciarRefresco()
    {
        if (_ctsRefresco is not null) return;
        var cts = _ctsRefresco = new CancellationTokenSource();
        _ = RefrescarCada60sAsync(cts.Token);
    }

    private async Task RefrescarCada60sAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _temporizador.EsperarAsync(IntervaloRefresco, ct);
                await RecalcularAsync();
                await CargarEsperasAsync();   // actualiza el "hace cuánto" de la botonera
            }
        }
        catch (OperationCanceledException) { }
    }

    // ================================================================== lectura y marca automática (RF-11, RF-12, RF-19)

    /// <summary>
    /// Pone una lectura en la cola (la del lector o la de la búsqueda manual). Devuelve una tarea que termina cuando se
    /// vació la cola, así los tests pueden esperar el resultado sin adivinar tiempos.
    /// </summary>
    public Task EncolarLecturaAsync(string codigo, bool esManual = false)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return Task.CompletedTask;
        // Con el aviso de tres opciones abierto, el mismo código que lo abrió se ignora (el bastón suele repetirlo) y no pisa la
        // lectura guardada (spec 009, RF-9).
        if (Cuadro == CuadroTrabajo.ConfirmarReemplazo && _reemplazoPendiente is { } pendiente && pendiente.Codigo == codigo.Trim())
            return Task.CompletedTask;
        return _cola.EncolarAsync(codigo.Trim(), esManual);
    }

    /// <summary>Mensaje corto cuando el lector lee otra vaca con una ficha abierta (sin datos del animal leído).</summary>
    public const string MensajeLecturaConFichaAbierta = "Terminá con esta vaca antes de leer otra";

    private async Task ProcesarLecturaAsync(Lectura lectura)
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        if (Etapa == EtapaTrabajo.Terminado) return;   // el trabajo ya se terminó: no se marca nada

        // Spec 009 (modificada 2026-11-25): con CUALQUIER ficha abierta (también la de una vaca con CVET 0 abierta con "Ver ficha"),
        // una lectura del bastón o del simulador no hace nada con el animal: no abre otra ficha, no marca, no suena, no cuenta y
        // no pregunta. Solo se avisa. La búsqueda a mano (EsManual) no entra acá: sigue como antes. También cubre la lectura que
        // esperaba con un cuadro abierto: se procesa al cerrarlo y, si la ficha sigue ahí, se ignora igual.
        if (Etapa == EtapaTrabajo.Ficha && !lectura.EsManual)
        {
            // El bastón repite la caravana de la vaca que se está tactando: eso se ignora sin cartel (como antes).
            if (Ficha is { } abierta && string.Equals(abierta.Caravana, lectura.Codigo, StringComparison.OrdinalIgnoreCase)) return;
            MensajeInfo = MensajeLecturaConFichaAbierta;
            return;
        }

        try
        {
            // Una lectura nueva cierra el aviso o la "no encontrada" que hubiera abierta y se procesa (RF-22b).
            if (Cuadro is CuadroTrabajo.Aviso or CuadroTrabajo.NoEncontrada) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            MensajeError = null;
            MensajeInfo = null;

            // Con una ficha abierta, la BÚSQUEDA A MANO sí llega acá: el servicio no marca a una vaca nueva que entra en tacto y
            // devuelve "RequiereDecision" para que la pantalla pregunte antes (spec 009, RF-1). Desde las lecturas del lector ese
            // camino hoy no se dispara (ver arriba); el aviso de tres opciones queda vivo solo para la búsqueda manual.
            var hayFichaAbierta = Etapa == EtapaTrabajo.Ficha;
            var resultado = await EnCampoAsync(() => _campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(rodeo.RodeoId, lectura.Codigo, hayFichaAbierta)));
            if (!resultado.EsCorrecto)
            {
                // Rechazo (por ejemplo reloj mal puesto): no se guardó nada ni se muestra la ficha como procesada (RF-12).
                MensajeError = resultado.Mensaje;
                return;
            }

            await AplicarLecturaAsync(resultado.Valor, lectura);
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    private async Task AplicarLecturaAsync(ResultadoLecturaDto r, Lectura lectura)
    {
        switch (r.Tipo)
        {
            case TipoResultadoLectura.Marcada when r.Ficha is not null:
                // La vaca quedó marcada: ficha nueva (reemplaza a la anterior, que ya estaba marcada), contador y aviso de deshacer.
                MostrarFicha(r.Ficha);
                if (r.NumeroManga is { } manga) NumeroManga = manga;
                if (r.ContadorManga is { } contador) Contador = contador;
                MangaAbierta = true;
                _avisos.Confirmacion();
                UltimoGuardado = $"Marcada: {r.Caravana}";
                // Marcar no muestra el aviso de deshacer de 5 s: desmarcar se puede desde "Últimos movimientos".
                await RecalcularAsync();
                await CerrarMangaSiEstaCompletaAsync();
                break;

            case TipoResultadoLectura.SinTacto:
                // CVET 0: no entra en tacto. Queda marcada (suma al conteo y se puede deshacer) pero no se abre la ficha: suena la
                // chicharra y no hay nada que cerrar, así se puede leer la siguiente enseguida. Una ficha abierta NO se toca (spec 009,
                // RF-8.2): tiene un evento o una observación que se perderían.
                if (r.NumeroManga is { } numeroSinTacto) NumeroManga = numeroSinTacto;
                if (r.ContadorManga is { } contadorSinTacto) Contador = contadorSinTacto;
                MangaAbierta = true;
                _avisos.SinTacto();
                UltimoGuardado = $"{r.Caravana}: no entra en tacto (CVET 0)";
                _ultimaSinTactoId = r.AnimalId;   // se asigna después: cambiar el "último" borra este dato
                _ultimaSinTactoCaravana = r.Caravana ?? r.CodigoLeido;
                OnPropertyChanged(nameof(HayVerFichaSinTacto));
                OnPropertyChanged(nameof(TextoVerFichaSinTacto));
                await RecalcularAsync();
                await CerrarMangaSiEstaCompletaAsync();
                break;

            case TipoResultadoLectura.Repetido or TipoResultadoLectura.YaPaso:
                // La misma vaca de la ficha abierta se ignora (RF-19); otra cosa muestra el aviso.
                if (r.AnimalId is not null && Ficha?.AnimalId == r.AnimalId) return;
                Aviso = r;
                _avisos.Repetido();
                await CambiarCuadroAsync(CuadroTrabajo.Aviso);
                break;

            case TipoResultadoLectura.RequiereDecision when r.AnimalId is not null:
                // Spec 009: había una ficha abierta y la vaca nueva entra en tacto: NO se marcó. Se pregunta qué hacer.
                if (Ficha?.AnimalId == r.AnimalId) return;
                await AbrirConfirmarReemplazoAsync(r, lectura, r.Ficha);
                break;

            case TipoResultadoLectura.EnEspera:
                // Vaca en espera (RF-5): se abre su ficha para ponerle el evento, sin aviso de repetida, sin marcar y sin sumar.
                if (r.AnimalId is null || Ficha?.AnimalId == r.AnimalId) return;
                var fichaEspera = r.Ficha;
                if (fichaEspera is null)
                {
                    var pedida = await EnCampoAsync(() => _campo.ObtenerFichaAsync(r.AnimalId.Value));
                    if (!pedida.EsCorrecto) { MensajeError = pedida.Mensaje; return; }
                    fichaEspera = pedida.Valor;
                }
                // Con una ficha abierta se pregunta antes de reemplazarla (spec 009, RF-6); sin ficha, se abre directo.
                if (Etapa == EtapaTrabajo.Ficha)
                    await AbrirConfirmarReemplazoAsync(r, lectura, fichaEspera);
                else
                    MostrarFicha(fichaEspera);
                break;

            default:   // NoEncontrada
                CodigoNoEncontrado = r.CodigoLeido;
                MensajeCuadro = null;
                await CambiarCuadroAsync(CuadroTrabajo.NoEncontrada);
                break;
        }
    }

    private void MostrarFicha(FichaAnimalDto ficha)
    {
        Ficha = ficha;
        ObservacionPendiente = "";   // la observación y el evento elegido son de la ficha anterior
        EventoElegido = null;
        EleccionElegida = null;
        LimpiarFila2();   // la segunda fila (spec 011) arranca cerrada en cada ficha y se descarta con ella
        Selector.LimpiarEleccion();
        MostrarHistorial = false;
        Etapa = EtapaTrabajo.Ficha;
    }

    private void CerrarFichaInterno()
    {
        Ficha = null;
        ObservacionPendiente = "";
        EventoElegido = null;
        EleccionElegida = null;
        LimpiarFila2();   // la segunda fila (spec 011) arranca cerrada en cada ficha y se descarta con ella
        // El aviso "se quitó el evento 2" es de la ficha que se cierra (Saltar, Entrar, En espera): no tiene que quedar en pantalla.
        if (MensajeInfo == MensajeSeQuitoEvento2) MensajeInfo = null;
        Selector.LimpiarEleccion();
        if (Etapa == EtapaTrabajo.Ficha) Etapa = EtapaTrabajo.Esperando;
    }

    // "Saltar" (CerrarFichaCommand) está en TrabajoViewModel.Espera.cs: en una ficha de espera la saltea.

    // ---- Búsqueda manual (RF-23)

    [RelayCommand]
    private async Task BuscarAsync()
    {
        var texto = TextoBusqueda.Trim();
        if (texto.Length == 0) return;
        TextoBusqueda = "";
        await EncolarLecturaAsync(texto, esManual: true);
    }

    // ================================================================== navegación
    [RelayCommand]
    private Task MostrarMovimientosAsync() => _navegacion.IrAMovimientosAsync();

    // ================================================================== simulador (solo desarrollo, RF-31)

    [RelayCommand]
    private Task SimularExistenteAsync() => SimularMuestraAsync(MuestraCaravana.Existente);

    [RelayCommand]
    private Task SimularYaLeidaAsync() => SimularMuestraAsync(MuestraCaravana.YaLeida);

    [RelayCommand]
    private void SimularInexistente() => _simulador?.Emitir(CodigoSimuladoInexistente);

    private async Task SimularMuestraAsync(MuestraCaravana tipo)
    {
        if (_simulador is null || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            var codigo = await EnCampoAsync(() => _campo.CaravanaDeMuestraAsync(rodeo.RodeoId, tipo));
            if (codigo is null) { MensajeError = MensajeSinMuestra; return; }
            _simulador.Emitir(codigo);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    // ================================================================== apoyo

    // Corre una operación de campo de a una por vez.
    private async Task<T> EnCampoAsync<T>(Func<Task<T>> operacion)
    {
        await _operacion.WaitAsync();
        try { return await operacion(); }
        finally { _operacion.Release(); }
    }
}
