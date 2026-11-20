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
    // Mensajes fijos: nunca llevan caravanas, nombres de tambos ni de veterinarios (RF-33).
    public const string MensajeFalla = "Algo falló y no se guardó nada. Probá de nuevo.";
    public const string MensajeLectorNoConecta = "No se pudo conectar el lector. Podés buscar la caravana a mano.";
    public const string MensajeSinMuestra = "No hay ninguna caravana de ese tipo para simular.";

    /// <summary>Código que el simulador emite para "una caravana que no existe en el rodeo" (solo desarrollo).</summary>
    public const string CodigoSimuladoInexistente = "SIM-NO-EXISTE";

    public static readonly TimeSpan DuracionAvisoDeshacer = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan IntervaloRefresco = TimeSpan.FromSeconds(60);

    private readonly ICampoService _campo;
    private readonly IRfidReader _lector;
    private readonly IPreferenciasDispositivo _preferencias;
    private readonly IAvisosCampo _avisos;
    private readonly IPantalla _pantalla;
    private readonly ITemporizador _temporizador;
    private readonly INavegacion _navegacion;
    private readonly SesionTrabajo _sesion;
    private readonly ISimuladorLecturas? _simulador;   // nulo en la versión que se distribuye (RF-31)

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
        ICampoService campo, IRfidReader lector, IPreferenciasDispositivo preferencias, IAvisosCampo avisos,
        IPantalla pantalla, ITemporizador temporizador, INavegacion navegacion, SesionTrabajo sesion,
        ISimuladorLecturas? simulador = null)
    {
        _campo = campo;
        _lector = lector;
        _preferencias = preferencias;
        _avisos = avisos;
        _pantalla = pantalla;
        _temporizador = temporizador;
        _navegacion = navegacion;
        _sesion = sesion;
        _simulador = simulador;
        _cola = new ColaDeLecturas(ProcesarLecturaAsync);
    }

    // ================================================================== encabezado (RF-3)

    public string NombreRodeo => _sesion.Rodeo?.RodeoNombre ?? "";
    public string NombreEstablecimiento => _sesion.Rodeo?.EstablecimientoNombre ?? "";
    public string NombreVeterinario => _sesion.Veterinario?.Nombre ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoManga), nameof(TextoSegundaConfirmacion))]
    private int _numeroManga = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoManga))]
    private int _contador;

    /// <summary>False = manga por empezar: la primera marca la abre (RF-4).</summary>
    [ObservableProperty]
    private bool _mangaAbierta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAvance))]
    private AvanceRodeoDto? _avance;

    /// <summary>"Manga 2 · 14": el número y el contador, siempre visibles.</summary>
    public string TextoManga => $"Manga {NumeroManga} · {Contador}";

    /// <summary>Avance del día con su fecha (RF-30).</summary>
    public string TextoAvance => Avance is null
        ? ""
        : $"{Avance.Fecha:dd/MM}: {Avance.Procesadas} de {Avance.TotalVacas} ({Avance.Porcentaje} %) · pendientes {Avance.Pendientes}";

    // ================================================================== el centro de la pantalla

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFicha), nameof(HayResumenTerminado), nameof(EstaEsperando))]
    private EtapaTrabajo _etapa = EtapaTrabajo.Esperando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFicha))]
    private FichaAnimalDto? _ficha;

    /// <summary>Estados activos del catálogo: botones para el tacto de un toque (RF-15).</summary>
    public ObservableCollection<EstadoActivoDto> Estados { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEstadosActivos))]
    private bool _estadosCargados;

    /// <summary>Si no hay estados activos no se puede tactar, pero sí leer y marcar (RF-15).</summary>
    public bool HayEstadosActivos => Estados.Count > 0;

    public bool HayFicha => Ficha is not null && Etapa != EtapaTrabajo.Terminado;
    public bool EstaEsperando => Etapa == EtapaTrabajo.Esperando;

    /// <summary>Resumen de lo último guardado, hasta que llegue otra lectura (RF-17).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayUltimoGuardado))]
    private string _ultimoGuardado = "";

    public bool HayUltimoGuardado => UltimoGuardado.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeInfo))]
    private string? _mensajeInfo;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);
    public bool HayMensajeInfo => !string.IsNullOrEmpty(MensajeInfo);

    /// <summary>El aviso de 5 segundos con "Deshacer" (RF-27). Nulo cuando no hay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAvisoDeshacer))]
    private AvisoDeshacerDto? _avisoDeshacer;

    public bool HayAvisoDeshacer => AvisoDeshacer is not null;

    // ================================================================== cuadros (capas)

    [ObservableProperty]
    private CuadroTrabajo _cuadro = CuadroTrabajo.Ninguno;

    public bool HayAviso => Cuadro == CuadroTrabajo.Aviso;
    public bool HayNoEncontrada => Cuadro == CuadroTrabajo.NoEncontrada;
    public bool HayConfirmarCorregir => Cuadro == CuadroTrabajo.ConfirmarCorregir;
    public bool HayVerificacion => Cuadro == CuadroTrabajo.Verificacion;
    public bool HayCantidadReal => Cuadro == CuadroTrabajo.CantidadReal;
    public bool HaySegundaConfirmacion => Cuadro == CuadroTrabajo.SegundaConfirmacion;
    public bool HayCuadro => Cuadro != CuadroTrabajo.Ninguno;

    partial void OnCuadroChanged(CuadroTrabajo value)
    {
        OnPropertyChanged(nameof(HayAviso));
        OnPropertyChanged(nameof(HayNoEncontrada));
        OnPropertyChanged(nameof(HayConfirmarCorregir));
        OnPropertyChanged(nameof(HayVerificacion));
        OnPropertyChanged(nameof(HayCantidadReal));
        OnPropertyChanged(nameof(HaySegundaConfirmacion));
        OnPropertyChanged(nameof(HayCuadro));
        OnPropertyChanged(nameof(TextoSegundaConfirmacion));
    }

    // Los cuadros que piden datos o confirmación frenan las lecturas; "Aviso" y "NoEncontrada" no (RF-19, RF-22b).
    private static bool Bloquea(CuadroTrabajo cuadro) => cuadro is
        CuadroTrabajo.ConfirmarCorregir or CuadroTrabajo.Verificacion
        or CuadroTrabajo.CantidadReal or CuadroTrabajo.SegundaConfirmacion;

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

    // ---- Aviso "Animal repetido" / "Ya pasó" (RF-22)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoTitulo), nameof(AvisoCaravana), nameof(AvisoDetalle), nameof(PuedeCorregir), nameof(TextoCorregir))]
    private ResultadoLecturaDto? _aviso;

    public string AvisoTitulo => Aviso?.Aviso switch
    {
        TipoAviso.Repetido => "Animal repetido",
        TipoAviso.YaPasoSinManga => "Ya pasó, sin manga registrada",
        TipoAviso.YaPasoOtraManga => Aviso.UltimoRegistro?.NumeroManga is { } m ? $"Ya pasó en la manga {m}" : "Ya pasó",
        _ => ""
    };

    /// <summary>La caravana leída, para mostrarla bien grande (quien maneja el bastón nota si leyó la de la vecina).</summary>
    public string AvisoCaravana => Aviso?.Caravana ?? Aviso?.CodigoLeido ?? "";

    /// <summary>Último registro de la vaca: qué, a qué hora y de quién.</summary>
    public string AvisoDetalle => Aviso?.UltimoRegistro is { } u
        ? $"{DescribirRegistro(u)} · {u.FechaHoraUtc.ToLocalTime():HH:mm} · {u.Veterinario}"
        : "";

    /// <summary>"Corregir" solo se ofrece cuando lo último no es la marca de pasada (RF-22).</summary>
    public bool PuedeCorregir => Aviso is { PuedeCorregir: true, UltimoRegistro: not null };

    public string TextoCorregir => Aviso?.UltimoRegistro is { } u ? $"¿Deshacer lo último de esta vaca: {DescribirRegistro(u)}?" : "";

    private static string DescribirRegistro(UltimoRegistroDto u) => u.Tipo == TipoRegistroCampo.Tacto
        ? (string.IsNullOrEmpty(u.EstadoNombre) ? "Tacto" : $"Tacto {u.EstadoNombre}")
        : "Marca de pasada";

    // ---- Caravana no encontrada (RF-24)

    [ObservableProperty]
    private string _codigoNoEncontrado = "";


    // ---- Búsqueda manual (RF-23)

    [ObservableProperty]
    private string _textoBusqueda = "";

    // ---- Cierre de manga (RF-6, RF-7)

    [ObservableProperty]
    private ResumenMangaDto? _resumenManga;

    [ObservableProperty]
    private string _cantidadRealTexto = "";

    /// <summary>Resultado de "Terminar el tacto": se muestra en el resumen con "Deshacer" (RF-7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayResumenTerminado), nameof(PuedeDeshacerTerminado), nameof(TextoResumenTerminado))]
    private ResultadoCierreDto? _resultadoTerminado;

    public bool HayResumenTerminado => Etapa == EtapaTrabajo.Terminado;
    public bool PuedeDeshacerTerminado => Etapa == EtapaTrabajo.Terminado && ResultadoTerminado?.Cierre is not null;

    public string TextoSegundaConfirmacion => $"¿Terminar el tacto? Se cierra la manga {NumeroManga} y no se abre otra.";

    /// <summary>Resumen del final del tacto: la manga cerrada, las dos cantidades y lo procesado hoy (RF-7).</summary>
    public string TextoResumenTerminado
    {
        get
        {
            if (ResultadoTerminado is not { } r) return "";
            var avance = $"Procesadas hoy: {r.Avance.Procesadas} de {r.Avance.TotalVacas}.";
            if (r.Cierre is not { } c) return $"No había una manga abierta. {avance}";
            var cantidades = $"Aviss contó {c.CantidadAviss}" + (c.CantidadConfirmada is { } conf ? $" y se confirmaron {conf}" : "");
            // Si después se desmarcó una vaca de la manga cerrada, las dos cifras se muestran (RF-8).
            var hoy = c.ContadorActual != c.CantidadAviss ? $" (hoy: {c.ContadorActual})" : "";
            return $"Manga {c.NumeroManga} cerrada: {cantidades}{hoy}. {avance}";
        }
    }

    // ================================================================== simulador (RF-31)

    /// <summary>False en la versión que se distribuye: la pantalla no muestra el panel.</summary>
    public bool HaySimulador => _simulador is not null;

    // ================================================================== arranque y ciclo de vida

    /// <summary>
    /// La página lo llama al aparecer. La primera vez empieza el trabajo (retoma la manga abierta o deja la siguiente por
    /// empezar; cierra como automática la de un día anterior) y carga los estados; siempre arranca el lector, el refresco
    /// y mantiene la pantalla encendida. Si no hay veterinario y rodeo elegidos, vuelve a la elección.
    /// </summary>
    public async Task IniciarAsync()
    {
        if (_sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo)
        {
            await _navegacion.IrAEleccionTrabajoAsync();
            return;
        }

        OnPropertyChanged(nameof(NombreRodeo));
        OnPropertyChanged(nameof(NombreEstablecimiento));
        OnPropertyChanged(nameof(NombreVeterinario));
        _pantalla.MantenerEncendida(true);

        try
        {
            // Solo la primera vez: volver de "Últimos movimientos" no debe cerrar la manga si ya cruzó la medianoche (RF-10).
            if (_rodeoIniciado != rodeo.RodeoId)
            {
                var inicio = await EnCampoAsync(() => _campo.IniciarTrabajoAsync(rodeo.RodeoId, vet.Id));
                if (!inicio.EsCorrecto)
                {
                    MensajeError = inicio.Mensaje;
                    return;
                }
                AplicarEstado(inicio.Valor);
                _rodeoIniciado = rodeo.RodeoId;

                var estados = await EnCampoAsync(() => _campo.ListarEstadosActivosAsync());
                Estados.Clear();
                foreach (var e in estados) Estados.Add(e);
                OnPropertyChanged(nameof(HayEstadosActivos));
            }
            else
            {
                await RecalcularAsync();
            }
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
        return _cola.EncolarAsync(codigo.Trim(), esManual);
    }

    private async Task ProcesarLecturaAsync(Lectura lectura)
    {
        if (_sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;
        if (Etapa == EtapaTrabajo.Terminado) return;   // el trabajo ya se terminó: no se marca nada

        try
        {
            // Una lectura nueva cierra el aviso o la "no encontrada" que hubiera abierta y se procesa (RF-22b).
            if (Cuadro is CuadroTrabajo.Aviso or CuadroTrabajo.NoEncontrada) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            MensajeError = null;
            MensajeInfo = null;

            var resultado = await EnCampoAsync(() => _campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(rodeo.RodeoId, lectura.Codigo, vet.Id)));
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
                if (r.MarcaId is { } marcaId)
                    PublicarAvisoDeshacer(new AvisoDeshacerDto(TipoDeshacer.Marca, marcaId, $"Marcada {r.Caravana}"));
                await RecalcularAsync();
                break;

            case TipoResultadoLectura.Repetido or TipoResultadoLectura.YaPaso:
                // La misma vaca de la ficha abierta se ignora (RF-19); otra cosa muestra el aviso.
                if (r.AnimalId is not null && Ficha?.AnimalId == r.AnimalId) return;
                Aviso = r;
                _avisos.Repetido();
                await CambiarCuadroAsync(CuadroTrabajo.Aviso);
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
        Etapa = EtapaTrabajo.Ficha;
    }

    private void CerrarFichaInterno()
    {
        Ficha = null;
        if (Etapa == EtapaTrabajo.Ficha) Etapa = EtapaTrabajo.Esperando;
    }

    /// <summary>Cierra la ficha sin hacer nada (todo lo que importa ya quedó guardado).</summary>
    [RelayCommand]
    private void CerrarFicha() => CerrarFichaInterno();

    // ---- Búsqueda manual (RF-23)

    [RelayCommand]
    private async Task BuscarAsync()
    {
        var texto = TextoBusqueda.Trim();
        if (texto.Length == 0) return;
        TextoBusqueda = "";
        await EncolarLecturaAsync(texto, esManual: true);
    }

    // ================================================================== tacto con un toque (RF-15 a RF-18)

    [RelayCommand]
    private async Task SeleccionarEstadoAsync(EstadoActivoDto? estado)
    {
        // Solo con una ficha abierta y sin ningún cuadro encima.
        if (estado is null || Ficha is not { } ficha || Etapa != EtapaTrabajo.Ficha || Cuadro != CuadroTrabajo.Ninguno) return;
        if (_sesion.Veterinario is not { } vet) return;

        try
        {
            MensajeError = null;
            var resultado = await EnCampoAsync(() => _campo.RegistrarTactoAsync(
                new RegistrarTactoCommand(ficha.AnimalId, estado.Id, null, null, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                // Rechazo: motivo en español, nada cambia y la ficha queda abierta para reintentar (RF-18).
                MensajeError = resultado.Mensaje;
                return;
            }

            // Guardado: vibración, resumen, aviso de deshacer y listo para la siguiente (RF-16, RF-17).
            _avisos.Confirmacion();
            CerrarFichaInterno();
            UltimoGuardado = $"{ficha.Caravana}: {estado.Nombre}";
            PublicarAvisoDeshacer(new AvisoDeshacerDto(TipoDeshacer.Tacto, resultado.Valor, $"{ficha.Caravana}: {estado.Nombre}"));
            await RecalcularAsync();
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    // ================================================================== aviso "Animal repetido" / "Ya pasó" (RF-22)

    /// <summary>"Ver ficha": abre la ficha sin marcar de nuevo.</summary>
    [RelayCommand]
    private async Task VerFichaAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso || Aviso?.AnimalId is not { } animalId) return;
        try
        {
            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(animalId));
            if (!ficha.EsCorrecto) { MensajeError = ficha.Mensaje; return; }
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Seguir": cierra el aviso y espera la siguiente lectura.</summary>
    [RelayCommand]
    private async Task SeguirAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso) return;
        CerrarFichaInterno();
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>"Corregir": pide confirmación antes de deshacer lo último de la vaca.</summary>
    [RelayCommand]
    private async Task CorregirAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso || !PuedeCorregir) return;
        await CambiarCuadroAsync(CuadroTrabajo.ConfirmarCorregir);
    }

    [RelayCommand]
    private async Task ConfirmarCorregirAsync()
    {
        if (Cuadro != CuadroTrabajo.ConfirmarCorregir || Aviso is not { UltimoRegistro: { } ultimo, AnimalId: { } animalId }) return;
        try
        {
            var tipo = ultimo.Tipo == TipoRegistroCampo.Tacto ? TipoDeshacer.Tacto : TipoDeshacer.Marca;
            var resultado = await DeshacerAsync(tipo, ultimo.RegistroId);
            if (!resultado.EsCorrecto)
            {
                // No se pudo: motivo y el aviso queda abierto (RF-22).
                await CambiarCuadroAsync(CuadroTrabajo.Aviso);
                MensajeError = resultado.Mensaje;
                return;
            }

            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(animalId));
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            if (ficha.EsCorrecto) MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"No" en la confirmación de "Corregir": vuelve al aviso.</summary>
    [RelayCommand]
    private Task CancelarCorregirAsync() => Cuadro == CuadroTrabajo.ConfirmarCorregir
        ? CambiarCuadroAsync(CuadroTrabajo.Aviso)
        : Task.CompletedTask;

    // ================================================================== caravana no encontrada (RF-24)

    /// <summary>"Buscar a mano": cierra el cuadro y deja la búsqueda lista. No se guardó nada.</summary>
    [RelayCommand]
    private async Task BuscarAManoAsync()
    {
        if (Cuadro != CuadroTrabajo.NoEncontrada) return;
        TextoBusqueda = "";
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Cierra "Caravana no encontrada" sin hacer nada.</summary>
    [RelayCommand]
    private async Task CerrarNoEncontradaAsync()
    {
        if (Cuadro != CuadroTrabajo.NoEncontrada) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // ================================================================== deshacer (RF-26, RF-27)

    // Deshace y, si salió bien, actualiza lo que se ve: cierra la ficha de una vaca desmarcada o anulada, baja el
    // contador y recalcula. Devuelve el resultado para que quien llama decida qué mensaje mostrar si fue rechazado.
    private async Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(TipoDeshacer tipo, Guid id)
    {
        if (_sesion.Veterinario is not { } vet) return Resultado.Error<ResultadoDeshacerDto>(CodigoError.DatosInvalidos, MensajeFalla);

        var resultado = await EnCampoAsync(() => _campo.DeshacerAsync(new DeshacerCommand(tipo, id, vet.Id)));
        if (!resultado.EsCorrecto) return resultado;

        var deshecho = resultado.Valor;
        // Desmarcar una vaca cierra su ficha (RF-26).
        if (tipo is TipoDeshacer.Marca && deshecho.AnimalId is { } animalId && Ficha?.AnimalId == animalId)
            CerrarFichaInterno();
        if (deshecho.ContadorManga is { } contador) Contador = contador;
        UltimoGuardado = tipo switch
        {
            TipoDeshacer.Marca => "Se desmarcó la vaca.",
            TipoDeshacer.Cierre => "Se reabrió la manga.",
            _ => "Se deshizo el tacto."
        };
        await RecalcularAsync();
        return resultado;
    }

    /// <summary>El botón "Deshacer" del aviso de 5 segundos.</summary>
    [RelayCommand]
    private async Task DeshacerAvisoAsync()
    {
        if (AvisoDeshacer is not { } aviso) return;
        QuitarAvisoDeshacer();
        try
        {
            var resultado = await DeshacerAsync(aviso.Tipo, aviso.Id);
            // Si ya no se puede (por ejemplo ya no es lo último de la vaca): se rechaza con el motivo (RF-27).
            if (!resultado.EsCorrecto) MensajeError = resultado.Mensaje;
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    // Publica un aviso nuevo (reemplaza al anterior) y lo saca solo a los 5 segundos. Sobrevive a las lecturas y a los
    // avisos de repetido: solo lo cambia otro aviso de deshacer, o su propio tiempo.
    private void PublicarAvisoDeshacer(AvisoDeshacerDto aviso)
    {
        _ctsAviso?.Cancel();
        var cts = _ctsAviso = new CancellationTokenSource();
        AvisoDeshacer = aviso;
        _ = ExpirarAvisoAsync(aviso, cts);
    }

    private async Task ExpirarAvisoAsync(AvisoDeshacerDto aviso, CancellationTokenSource cts)
    {
        try
        {
            await _temporizador.EsperarAsync(DuracionAvisoDeshacer, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; }

        if (!cts.IsCancellationRequested && ReferenceEquals(AvisoDeshacer, aviso)) AvisoDeshacer = null;
    }

    private void QuitarAvisoDeshacer()
    {
        _ctsAviso?.Cancel();
        _ctsAviso = null;
        AvisoDeshacer = null;
    }

    // ================================================================== Finalizar manga y Terminar el tacto (RF-6, RF-7)

    [RelayCommand]
    private Task FinalizarMangaAsync() => IniciarCierreAsync(PropositoCierre.Finalizar);

    [RelayCommand]
    private Task TerminarElTactoAsync() => IniciarCierreAsync(PropositoCierre.Terminar);

    private async Task IniciarCierreAsync(PropositoCierre proposito)
    {
        if (Cuadro != CuadroTrabajo.Ninguno || Etapa == EtapaTrabajo.Terminado || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            MensajeError = null;
            MensajeInfo = null;
            MensajeCuadro = null;
            _proposito = proposito;
            _cantidadRealConfirmada = null;
            CantidadRealTexto = "";

            var resumen = await EnCampoAsync(() => _campo.ObtenerResumenMangaAsync(rodeo.RodeoId));
            if (resumen.EsCorrecto)
            {
                ResumenManga = resumen.Valor;
                await CambiarCuadroAsync(CuadroTrabajo.Verificacion);
            }
            else if (resumen.Codigo == CodigoError.NoHayMangaAbierta && proposito == PropositoCierre.Terminar)
            {
                // Sin manga abierta, "Terminar el tacto" solo pide la segunda confirmación y no cierra nada (RF-7).
                ResumenManga = null;
                await CambiarCuadroAsync(CuadroTrabajo.SegundaConfirmacion);
            }
            else
            {
                MensajeError = resumen.Mensaje;   // "No hay una manga abierta: no hay nada para finalizar."
            }
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Coincide": la cantidad real es la que contó Aviss.</summary>
    [RelayCommand]
    private async Task CoincideAsync()
    {
        if (Cuadro != CuadroTrabajo.Verificacion) return;
        _cantidadRealConfirmada = null;
        await ContinuarCierreAsync();
    }

    /// <summary>"No coincide": pide la cantidad real.</summary>
    [RelayCommand]
    private async Task NoCoincideAsync()
    {
        if (Cuadro != CuadroTrabajo.Verificacion) return;
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.CantidadReal);
    }

    /// <summary>Con la cantidad real cargada: sigue el cierre (cerrar igual, dejando la diferencia registrada).</summary>
    [RelayCommand]
    private async Task ConfirmarCantidadRealAsync()
    {
        if (Cuadro != CuadroTrabajo.CantidadReal) return;
        // Solo dígitos: un entero de cero para arriba (el servicio lo vuelve a validar).
        if (!int.TryParse(CantidadRealTexto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cantidad))
        {
            MensajeCuadro = MensajesCampo.Para(CodigoError.CantidadInvalida);
            return;
        }
        MensajeCuadro = null;
        _cantidadRealConfirmada = cantidad;
        await ContinuarCierreAsync();
    }

    /// <summary>"Volver a corregir": cierra el cuadro sin cerrar nada, para marcar a mano o deshacer una de más.</summary>
    [RelayCommand]
    private async Task VolverACorregirAsync()
    {
        if (Cuadro is not (CuadroTrabajo.Verificacion or CuadroTrabajo.CantidadReal)) return;
        MensajeCuadro = null;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // Después de la verificación: "Finalizar" cierra ya; "Terminar" pasa a la segunda confirmación (no se guarda nada todavía).
    private async Task ContinuarCierreAsync()
    {
        if (_proposito == PropositoCierre.Terminar)
            await CambiarCuadroAsync(CuadroTrabajo.SegundaConfirmacion);
        else
            await CerrarMangaAsync();
    }

    private async Task CerrarMangaAsync()
    {
        if (_sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            var resultado = await EnCampoAsync(() => _campo.FinalizarMangaAsync(new FinalizarMangaCommand(rodeo.RodeoId, _cantidadRealConfirmada, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeCuadro = resultado.Mensaje;   // se queda en el cuadro con el motivo
                return;
            }

            var cierre = resultado.Valor.Cierre;
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            CerrarFichaInterno();
            _avisos.Confirmacion();
            if (cierre is not null)
            {
                UltimoGuardado = $"Manga {cierre.NumeroManga} cerrada";
                PublicarAvisoDeshacer(new AvisoDeshacerDto(TipoDeshacer.Cierre, cierre.CierreId, $"Manga {cierre.NumeroManga} cerrada"));
            }
            Avance = resultado.Valor.Avance;
            await RecalcularAsync();   // la siguiente manga queda por empezar
        }
        catch (Exception) { MensajeCuadro = MensajeFalla; }
    }

    /// <summary>"Sí, terminar": solo acá, con las dos confirmaciones, se cierra la manga y se da por terminado el tacto.</summary>
    [RelayCommand]
    private async Task ConfirmarTerminarAsync()
    {
        if (Cuadro != CuadroTrabajo.SegundaConfirmacion || _sesion.Veterinario is not { } vet || _sesion.Rodeo is not { } rodeo) return;
        try
        {
            var resultado = await EnCampoAsync(() => _campo.TerminarElTactoAsync(new TerminarElTactoCommand(rodeo.RodeoId, _cantidadRealConfirmada, vet.Id)));
            if (!resultado.EsCorrecto)
            {
                MensajeCuadro = resultado.Mensaje;
                return;
            }

            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            CerrarFichaInterno();
            QuitarAvisoDeshacer();   // el resumen ya trae su propio "Deshacer"
            ResultadoTerminado = resultado.Valor;
            Etapa = EtapaTrabajo.Terminado;
            _avisos.Confirmacion();
            await RecalcularAsync();
        }
        catch (Exception) { MensajeCuadro = MensajeFalla; }
    }

    /// <summary>"No, seguir trabajando": no se cerró nada.</summary>
    [RelayCommand]
    private async Task NoTerminarAsync()
    {
        if (Cuadro != CuadroTrabajo.SegundaConfirmacion) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>"Deshacer" del resumen de "Terminar el tacto": reabre la manga y se sigue trabajando (RF-9).</summary>
    [RelayCommand]
    private async Task DeshacerTerminadoAsync()
    {
        if (!PuedeDeshacerTerminado || ResultadoTerminado?.Cierre is not { } cierre) return;
        try
        {
            MensajeError = null;
            var resultado = await DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);
            if (!resultado.EsCorrecto) { MensajeError = resultado.Mensaje; return; }

            ResultadoTerminado = null;
            Etapa = EtapaTrabajo.Esperando;
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Salir" del resumen: vuelve a elegir rodeo.</summary>
    [RelayCommand]
    private async Task SalirAsync()
    {
        await DetenerAsync();
        _sesion.SalirDelRodeo();
        _rodeoIniciado = null;
        await _navegacion.IrAEleccionTrabajoAsync();
    }

    // ================================================================== veterinario y navegación

    /// <summary>Los veterinarios activos, para cambiar el activo sin salir del rodeo ni de la manga (RF-1).</summary>
    public async Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosAsync()
        => await EnCampoAsync(() => _campo.ListarVeterinariosActivosAsync());

    /// <summary>El cambio no toca los registros anteriores ni cierra la manga: solo cambia quién firma de acá en más.</summary>
    [RelayCommand]
    private void CambiarVeterinario(VeterinarioDto? veterinario)
    {
        if (veterinario is null) return;
        _sesion.CambiarVeterinario(veterinario);
        _preferencias.GuardarVeterinarioRecordado(veterinario.Id);
        OnPropertyChanged(nameof(NombreVeterinario));
    }

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
