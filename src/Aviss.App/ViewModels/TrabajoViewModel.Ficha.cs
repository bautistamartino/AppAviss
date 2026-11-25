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

/// <summary>Pantalla de trabajo: la ficha de la vaca (CVET, pestañas Datos e Historial), el evento elegido con "Entrar", la observación y los eventos nuevos.</summary>
public sealed partial class TrabajoViewModel
{
    // ---- CVET: solo entran en tacto las vacas con CVET distinto de 0; el valor es el motivo del tacto

    /// <summary>La ficha abierta entra en tacto y el Excel trae su CVET: se muestra arriba del estado.</summary>
    public bool HayMotivoTacto => HayFicha && Ficha!.EntraEnTacto && !string.IsNullOrWhiteSpace(Ficha.Cvet);

    public string TextoMotivoTacto => Ficha is { } f && !string.IsNullOrWhiteSpace(f.Cvet) ? $"Motivo del tacto · CVET: {f.Cvet}" : "";

    // ---- Franja de rechazo: se calcula en el ViewModel (y no con un DataTrigger en el XAML, que con bindings compilados no oculta)

    /// <summary>La vaca de la ficha tiene un rechazo vigente: se muestra la franja roja.</summary>
    public bool HayRechazoVigente => Ficha?.RechazoVigente is not null;

    /// <summary>"Rechazo: motivo", o solo "Rechazo" si no se anotó motivo. Vacío si no hay rechazo vigente.</summary>
    public string TextoRechazoVigente => Ficha?.RechazoVigente is { } r
        ? string.IsNullOrWhiteSpace(r.Observaciones) ? "Rechazo" : $"Rechazo: {r.Observaciones.Trim()}"
        : "";

    // ---- Pestañas de la ficha (Datos | Historial), título y tabla de datos, como en el Daily

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDatos))]
    private bool _mostrarHistorial;

    public bool HayDatos => !MostrarHistorial;

    [RelayCommand]
    private void VerDatos() => MostrarHistorial = false;

    [RelayCommand]
    private void VerHistorial() => MostrarHistorial = true;

    /// <summary>Título de la ficha: "ID 1014" y, si entra en tacto y el Excel lo trae, "· CVET PRE" (el motivo del tacto).</summary>
    public string TituloFicha => Ficha is null ? "" : HayMotivoTacto ? $"ID {Ficha.Caravana} · CVET {Ficha.Cvet}" : $"ID {Ficha.Caravana}";

    /// <summary>
    /// La tabla "dato → valor" de la pestaña Datos: el estado, las columnas conocidas del Excel y todas las demás que haya
    /// traído (DCC, DDUC, LECHE...). Solo las que tienen valor.
    /// </summary>
    public IReadOnlyList<CampoFichaDto> FilasDatos
    {
        get
        {
            if (Ficha is not { } f) return [];
            var filas = new List<CampoFichaDto> { new("Estado", string.IsNullOrWhiteSpace(f.EstadoNombre) ? "Sin estado" : f.EstadoNombre) };
            if (f.FechaUltimoTactoUtc is { } ultimo)
                filas.Add(new("Último tacto", ultimo.ToLocalTime().ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)));
            filas.AddRange(f.Campos.Where(c => !string.IsNullOrWhiteSpace(c.Valor)));
            if (!string.IsNullOrWhiteSpace(f.Inseminaciones)) filas.Add(new("VINS", f.Inseminaciones));
            if (!string.IsNullOrWhiteSpace(f.UltimoToro)) filas.Add(new("UTORO", f.UltimoToro));
            if (!string.IsNullOrWhiteSpace(f.IdElectronico)) filas.Add(new("EID", f.IdElectronico));
            if (f.Extras is { } extras) filas.AddRange(extras);
            return filas;
        }
    }


    /// <summary>
    /// La ficha abierta es de una vaca con CVET 0 (se abrió con "Ver ficha"): se muestra un cartel solo informativo (spec 009, RF-15).
    /// No bloquea nada: la ficha es completa (Evento, Observación, Saltar, En espera y Entrar), porque en el campo el veterinario puede
    /// decidir tactarla igual.
    /// </summary>
    public bool MostrarCartelCvetCero => HayFicha && !Ficha!.EntraEnTacto;

    public string TextoCartelCvetCero => "CVET 0: no estaba en la lista de tacto";

    /// <summary>
    /// El selector de eventos de dos niveles (spec 005): principales y "Otro evento". Lo muestra la capa <see cref="CuadroTrabajo.Eventos"/>.
    /// Si no hay eventos activos no se puede tactar, pero sí leer y marcar.
    /// </summary>
    public SelectorEventosViewModel Selector { get; }

    public bool HayFicha => Ficha is not null && Etapa != EtapaTrabajo.Terminado;
    public bool EstaEsperando => Etapa == EtapaTrabajo.Esperando;

    /// <summary>Resumen de lo último guardado, hasta que llegue otra lectura (RF-17).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayUltimoGuardado))]
    private string _ultimoGuardado = "";

    public bool HayUltimoGuardado => UltimoGuardado.Length > 0;

    // ---- "Ver ficha" de la última vaca que no entró en tacto (CVET 0): el veterinario a veces quiere mirar sus datos por curiosidad

    private Guid? _ultimaSinTactoId;
    private string _ultimaSinTactoCaravana = "";

    // Cualquier otro "último" (una marca, un tacto, un cierre de manga) deja de ser la vaca sin tacto.
    partial void OnUltimoGuardadoChanged(string value)
    {
        _ultimaSinTactoId = null;
        OnPropertyChanged(nameof(HayVerFichaSinTacto));
        OnPropertyChanged(nameof(TextoVerFichaSinTacto));
    }

    /// <summary>Se ve el botón "Ver ficha de ..." bajo el cartel de "Último", mientras se espera la próxima lectura.</summary>
    public bool HayVerFichaSinTacto => EstaEsperando && _ultimaSinTactoId is not null;

    public string TextoVerFichaSinTacto => _ultimaSinTactoId is null ? "" : $"Ver ficha de {_ultimaSinTactoCaravana}";

    /// <summary>Abre la ficha completa de esa vaca (spec 009, RF-15): no la vuelve a marcar, pero se puede tactar o poner en espera igual.</summary>
    [RelayCommand]
    private async Task VerFichaSinTactoAsync()
    {
        if (_ultimaSinTactoId is not { } animalId || !EstaEsperando || Cuadro != CuadroTrabajo.Ninguno) return;
        try
        {
            MensajeError = null;
            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(animalId));
            if (!ficha.EsCorrecto) { MensajeError = ficha.Mensaje; return; }
            MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

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


    // ================================================================== tacto con un toque (RF-15 a RF-18)

    // ---- "Agregar evento" y "Agregar observación"

    /// <summary>Texto opcional que viaja con el próximo evento de la ficha abierta. Se descarta al cerrar la ficha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayObservacionPendiente), nameof(TextoCasilleroObservacion))]
    private string _observacionPendiente = "";

    public bool HayObservacionPendiente => ObservacionPendiente.Length > 0;

    /// <summary>Lo que se está escribiendo en el cuadro de observación (se confirma con "Guardar").</summary>
    [ObservableProperty]
    private string _textoObservacion = "";

    private bool PuedeAbrirCuadroDeFicha => Ficha is not null && Etapa == EtapaTrabajo.Ficha && Cuadro == CuadroTrabajo.Ninguno;

    /// <summary>El casillero "Evento": abre todas las opciones; elegir una la deja en el casillero (se guarda con "Entrar").</summary>
    [RelayCommand]
    private async Task AbrirEventosAsync()
    {
        if (!PuedeAbrirCuadroDeFicha) return;
        MensajeCuadro = null;
        HayFormularioEvento = false;
        FilaActiva = 1;
        // Siempre se abre en los principales, con resaltado: el evento ya elegido o, si no hay, el estado actual de la vaca (RF-9).
        // Con un evento ya elegido se le pasan sus datos (días, resultado): si se lo vuelve a tocar, sus pasos abren con los valores actuales.
        Selector.Abrir(EventoElegido?.Id ?? Ficha?.EstadoId, EleccionElegida);
        await CambiarCuadroAsync(CuadroTrabajo.Eventos);
    }

    // ---- Crear un evento (estado) nuevo desde "Agregar evento", por si no existe el que se busca

    /// <summary>El formulario de evento nuevo está abierto dentro de "Agregar evento".</summary>
    [ObservableProperty]
    private bool _hayFormularioEvento;

    [ObservableProperty]
    private string _nuevoEventoNombre = "";

    /// <summary>El código con el que Dairy Comp conoce el evento. Si queda vacío se usa el nombre en mayúsculas.</summary>
    [ObservableProperty]
    private string _nuevoEventoCodigoDc = "";

    [RelayCommand]
    private void AbrirNuevoEvento()
    {
        // "+ Agregar evento" solo se ofrece desde la vista de "Otro evento" (RF-8).
        if (Cuadro != CuadroTrabajo.Eventos || !Selector.PuedeCrearNuevo) return;
        MensajeCuadro = null;
        NuevoEventoNombre = "";
        NuevoEventoCodigoDc = "";
        HayFormularioEvento = true;
    }

    [RelayCommand]
    private void CancelarNuevoEvento()
    {
        MensajeCuadro = null;
        HayFormularioEvento = false;
    }

    /// <summary>Crea el estado, vuelve a leer la lista y deja el formulario cerrado. No guarda ningún tacto: se elige después.</summary>
    [RelayCommand]
    private async Task CrearEventoAsync()
    {
        if (Cuadro != CuadroTrabajo.Eventos) return;
        var nombre = NuevoEventoNombre.Trim();
        if (nombre.Length == 0)
        {
            MensajeCuadro = "Escribí el nombre del evento.";
            return;
        }

        try
        {
            MensajeCuadro = null;
            var codigoDc = string.IsNullOrWhiteSpace(NuevoEventoCodigoDc) ? nombre.ToUpperInvariant() : NuevoEventoCodigoDc.Trim();
            var resultado = await EnCampoAsync(() => _campo.CrearEstadoAsync(nombre, codigoDc));
            if (!resultado.EsCorrecto)
            {
                MensajeCuadro = resultado.Mensaje;   // por ejemplo, ya existe un evento con ese nombre
                return;
            }

            await CargarEstadosAsync();
            NuevoEventoNombre = "";
            NuevoEventoCodigoDc = "";
            HayFormularioEvento = false;
        }
        catch (Exception) { MensajeCuadro = MensajeFalla; }
    }

    private async Task CargarEstadosAsync()
    {
        // El servicio arma las dos listas (principales y "Otro evento" con el Rechazo fijo primero); el selector solo las muestra.
        // No cambia la vista: tras crear un evento el selector sigue en "Otro evento" (RF-8).
        Selector.Actualizar(await EnCampoAsync(() => _campo.ListarEventosAsync()));
    }


    /// <summary>"Agregar observación": abre el texto con lo ya escrito, si había algo.</summary>
    [RelayCommand]
    private async Task AbrirObservacionAsync()
    {
        if (!PuedeAbrirCuadroDeFicha) return;
        FilaActiva = 1;
        TextoObservacion = ObservacionPendiente;
        await CambiarCuadroAsync(CuadroTrabajo.Observacion);
    }

    /// <summary>
    /// Guarda el texto como observación pendiente de la fila que lo abrió (vacío = sin observación). No guarda ningún tacto todavía.
    /// </summary>
    [RelayCommand]
    private async Task GuardarObservacionAsync()
    {
        if (Cuadro != CuadroTrabajo.Observacion) return;
        if (FilaActiva == 2) Observacion2Pendiente = TextoObservacion.Trim();
        else ObservacionPendiente = TextoObservacion.Trim();
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    [RelayCommand]
    private void QuitarObservacion() => ObservacionPendiente = "";

    /// <summary>Cierra "Agregar evento" o "Agregar observación" sin cambiar nada y vuelve a la ficha.</summary>
    [RelayCommand]
    private async Task CerrarCuadroAgregarAsync()
    {
        if (Cuadro is not (CuadroTrabajo.Eventos or CuadroTrabajo.Observacion)) return;
        Selector.CancelarCommand.Execute(null);   // un paso abierto (días, resultado) se descarta: no deja nada elegido
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // ---- El evento elegido y "Entrar": elegir no guarda, Entrar sí (evita guardar por un toque erróneo)

    /// <summary>El evento que se eligió para esta vaca. Queda en el casillero "Evento" hasta que se toca "Entrar".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoElegido), nameof(HayEventoElegido))]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private EventoCampoDto? _eventoElegido;

    /// <summary>
    /// Lo que completó el selector del evento elegido: los días y el resultado si tiene pasos (spec 005, RF-27). Es lo que viaja al guardar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoElegido))]
    private EleccionEvento? _eleccionElegida;

    public bool HayEventoElegido => EventoElegido is not null;

    /// <summary>Lo que muestra el casillero "Evento": el resumen ("Aborto, 45 días, Vacía OK") o, si no hay, la invitación a elegir.</summary>
    public string TextoEventoElegido => EventoElegido is { } e ? SelectorEventosViewModel.Resumen(e, EleccionElegida) : "Tocá para elegir";

    /// <summary>Lo que muestra el casillero "Observación".</summary>
    public string TextoCasilleroObservacion => HayObservacionPendiente ? ObservacionPendiente : "Opcional";

    /// <summary>
    /// El selector avisa que el evento quedó elegido (con todos sus pasos completos si los tiene): queda en el casillero y se vuelve a la
    /// ficha. No guarda nada todavía.
    /// </summary>
    [RelayCommand]
    private async Task ElegirEventoAsync(EventoCampoDto? estado)
    {
        // Solo con una ficha abierta (también la de CVET 0, spec 009 RF-15) y sin ningún cuadro encima (salvo la lista, desde donde se elige).
        if (estado is null || Ficha is null || Etapa != EtapaTrabajo.Ficha
            || Cuadro is not (CuadroTrabajo.Ninguno or CuadroTrabajo.Eventos)) return;

        // La lista se abrió desde la fila 2: lo elegido es el Evento 2 (spec 011).
        if (Cuadro == CuadroTrabajo.Eventos && FilaActiva == 2)
        {
            await ElegirEvento2Async(estado);
            return;
        }

        // Los datos los armó el selector; un evento sin pasos no necesita ninguno. Un evento con pasos sin sus datos completos no se elige.
        var eleccion = Selector.EleccionActual is { } e && e.EventoId == estado.Id ? e : new EleccionEvento(estado.Id);
        if (estado.TienePasos && !SelectorEventosViewModel.EstaCompleta(estado, eleccion)) return;

        EleccionElegida = eleccion;
        EventoElegido = estado;
        MensajeError = null;
        if (MensajeInfo == MensajeSeQuitoEvento2) MensajeInfo = null;   // el aviso de un cambio anterior ya cumplió

        // RF-7: si el nuevo Evento 1 ya no admite un segundo, es igual al Evento 2 o lo deja no combinable, el Evento 2 y su observación
        // se descartan con un aviso fijo. El Evento 1 elegido no se pierde. Una fila 2 vacía se cierra sin avisar: no se perdió nada.
        if (HayFila2 && (!estado.AdmiteSegundo || estado.Id == Evento2Elegido?.Id || Evento2Elegido?.PuedeSerSegundo == false))
        {
            var habiaAlgo = Evento2Elegido is not null || Observacion2Pendiente.Length > 0;
            LimpiarFila2();
            if (habiaAlgo) MensajeInfo = MensajeSeQuitoEvento2;
        }

        if (Cuadro == CuadroTrabajo.Eventos) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Se puede tocar "Entrar": con una ficha abierta (también la de CVET 0), sin cuadros encima y con un evento elegido.</summary>
    private bool PuedeEntrar => Ficha is not null && Etapa == EtapaTrabajo.Ficha && Cuadro == CuadroTrabajo.Ninguno && EventoElegido is not null;

    /// <summary>"Entrar": guarda el tacto con el evento elegido y la observación (si hay).</summary>
    [RelayCommand(CanExecute = nameof(PuedeEntrar))]
    private async Task EntrarAsync()
    {
        if (Ficha is not { } ficha || EventoElegido is not { } estado || Etapa != EtapaTrabajo.Ficha || Cuadro != CuadroTrabajo.Ninguno) return;

        try
        {
            MensajeError = null;
            var observacion = HayObservacionPendiente ? ObservacionPendiente : null;

            // Con la fila 2 abierta hace falta elegir el Evento 2 (o tocar "Quitar"): no se guarda nada a medias.
            if (HayFila2 && Evento2Elegido is null)
            {
                MensajeError = MensajeFalta2;
                return;
            }
            // El evento elegido decide qué se guarda: "Rechazo" es otro tipo de registro (el motivo va en la observación); un estado
            // del catálogo es un tacto. Si la vaca estaba en espera, el servicio la resuelve en la misma transacción.
            // Un solo evento (OK, Revisar Nuevamente, Rechazo) y un Aborto que termina en Rechazo se guardan como registro de rechazo: no
            // cambian el estado. El resto (un estado, Preñada fecha previa, un Aborto con estado) es un tacto. Los datos de los pasos
            // (días, resultado) viajan en la elección (spec 005, RF-24, RF-29, RF-30).
            var eleccion = EleccionElegida ?? new EleccionEvento(estado.Id);
            // El Evento 2 (spec 011) viaja dentro de la elección; el servicio lo revalida y guarda todo en una sola operación.
            var evento2 = HayFila2 ? Evento2Elegido : null;
            if (evento2 is not null)
                eleccion = eleccion with { Segundo = new SegundoEventoElegido(evento2.Id, Observacion2Pendiente.Length > 0 ? Observacion2Pendiente : null) };
            var esRechazo = eleccion.RegistraComoRechazo || !estado.CambiaEstado;
            var resultado = esRechazo
                ? await EnCampoAsync(() => _campo.RegistrarSoloEventoAsync(
                    new RegistrarSoloEventoCommand(ficha.AnimalId, eleccion.EventoId, observacion, eleccion)))
                : await EnCampoAsync(() => _campo.RegistrarTactoAsync(
                    new RegistrarTactoCommand(ficha.AnimalId, eleccion.EventoId, null, observacion, null, eleccion)));
            var texto = SelectorEventosViewModel.Resumen(estado, eleccion, evento2);
            if (!resultado.EsCorrecto)
            {
                // Rechazo: motivo en español, nada cambia (el evento y la observación siguen elegidos), la ficha queda abierta
                // (RF-18) y, si venía de una espera, esta sigue pendiente.
                MensajeError = resultado.Mensaje;
                return;
            }

            // Guardado: vibración, resumen, aviso de deshacer y listo para la siguiente (RF-16, RF-17).
            _avisos.Confirmacion();
            CerrarFichaInterno();   // también descarta el evento elegido y la observación
            UltimoGuardado = $"{ficha.Caravana}: {texto}";
            PublicarAvisoDeshacer(new AvisoDeshacerDto(esRechazo ? TipoDeshacer.Rechazo : TipoDeshacer.Tacto, resultado.Valor, $"{ficha.Caravana}: {texto}"));
            await RecalcularAsync();
            if (ficha.EnEspera) await CargarEsperasAsync();   // la espera quedó resuelta: sale de la botonera
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    // ================================================================== segundo evento (spec 011)
    // Un tacto puede llevar dos eventos (por ejemplo OK + PROSTAG), cada uno con su observación. La fila 2 arranca cerrada en cada ficha;
    // el Evento 2 nunca cambia el estado ni lleva pasos: eso lo decide el servicio y lo anticipan las banderas del DTO.

    /// <summary>Aviso fijo al descartar el Evento 2 porque el Evento 1 cambió a uno que no se combina (RF-7).</summary>
    public const string MensajeSeQuitoEvento2 = "Ese evento no se combina con otro: se quitó el evento 2";

    /// <summary>Entrar con la fila 2 abierta pero sin Evento 2 elegido: no se guarda nada.</summary>
    public const string MensajeFalta2 = "Elegí el evento 2 o tocá Quitar.";

    /// <summary>Los textos fijos de los dos botones chicos de la ficha (spec 011, RF-2): el XAML los muestra tal cual.</summary>
    public string TextoMasEvento => "+ evento";
    public string TextoQuitarEvento2 => "Quitar";

    /// <summary>La segunda fila está abierta (se tocó "+ evento"). Arranca cerrada en cada ficha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAgregarEvento2))]
    private bool _hayFila2;

    /// <summary>El Evento 2 elegido (nulo = fila abierta, todavía sin elegir).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEvento2Elegido), nameof(HayEvento2Elegido))]
    private EventoCampoDto? _evento2Elegido;

    /// <summary>La observación propia del Evento 2: se descarta con la fila.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayObservacion2Pendiente), nameof(TextoCasilleroObservacion2))]
    private string _observacion2Pendiente = "";

    /// <summary>
    /// Qué fila abrió el cuadro de eventos o el de observación que está a la vista: 1 o 2. Las capas del XAML son las mismas para las dos
    /// filas; esto decide a quién se le asigna lo que se elige o se escribe.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsFila2Activa))]
    private int _filaActiva = 1;

    public bool EsFila2Activa => FilaActiva == 2;

    public bool HayEvento2Elegido => Evento2Elegido is not null;
    public bool HayObservacion2Pendiente => Observacion2Pendiente.Length > 0;

    public string TextoEvento2Elegido => Evento2Elegido is { } e ? e.Nombre : "Tocá para elegir";
    public string TextoCasilleroObservacion2 => HayObservacion2Pendiente ? Observacion2Pendiente : "Opcional";

    /// <summary>El tope de caracteres de cada observación, para que el XAML lo ponga en el cuadro (la pantalla no deja escribir más).</summary>
    public const int MaximoObservacion = 300;

    /// <summary>Se ve "+ evento": hay una ficha con un Evento 1 que admite un segundo y todavía no hay fila 2 (como máximo dos eventos, RF-6).</summary>
    public bool PuedeAgregarEvento2 => HayFicha && EventoElegido?.AdmiteSegundo == true && !HayFila2;

    // El Evento 1 cambia o se limpia: "+ evento" aparece o desaparece.
    partial void OnEventoElegidoChanged(EventoCampoDto? value) => OnPropertyChanged(nameof(PuedeAgregarEvento2));

    /// <summary>"+ evento": abre la fila 2 vacía. No guarda nada ni abre la lista.</summary>
    [RelayCommand]
    private void AgregarEvento2()
    {
        if (!PuedeAgregarEvento2 || Etapa != EtapaTrabajo.Ficha || Cuadro != CuadroTrabajo.Ninguno) return;
        MensajeError = null;
        HayFila2 = true;
    }

    /// <summary>"Quitar": descarta el Evento 2 y su observación; el Evento 1 y la suya no se tocan (RF-5).</summary>
    [RelayCommand]
    private void QuitarEvento2()
    {
        if (!HayFila2 || Cuadro != CuadroTrabajo.Ninguno) return;
        MensajeError = null;
        LimpiarFila2();
    }

    /// <summary>El casillero "Evento" de la fila 2: abre la lista con solo los eventos combinables y sin el Evento 1 (RF-3).</summary>
    [RelayCommand]
    private async Task AbrirEventos2Async()
    {
        if (!PuedeAbrirCuadroDeFicha || !HayFila2 || EventoElegido is not { } evento1) return;
        MensajeCuadro = null;
        HayFormularioEvento = false;
        FilaActiva = 2;
        Selector.AbrirSegundo(Evento2Elegido?.Id, evento1.Id);
        await CambiarCuadroAsync(CuadroTrabajo.Eventos);
    }

    /// <summary>El casillero "Observación" de la fila 2: el mismo cuadro de texto, con lo ya escrito para el Evento 2.</summary>
    [RelayCommand]
    private async Task AbrirObservacion2Async()
    {
        if (!PuedeAbrirCuadroDeFicha || !HayFila2) return;
        FilaActiva = 2;
        TextoObservacion = Observacion2Pendiente;
        await CambiarCuadroAsync(CuadroTrabajo.Observacion);
    }

    [RelayCommand]
    private void QuitarObservacion2() => Observacion2Pendiente = "";

    // Lo elegido en la lista abierta desde la fila 2: queda en la fila y se vuelve a la ficha. No guarda nada.
    private async Task ElegirEvento2Async(EventoCampoDto estado)
    {
        // Solo un evento que puede ser segundo y distinto del Evento 1: la lista ya lo filtra, esto es el cinturón de seguridad.
        if (!estado.PuedeSerSegundo || estado.Id == EventoElegido?.Id) return;
        Evento2Elegido = estado;
        MensajeError = null;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Cierra la fila 2 y descarta el Evento 2 y su observación. Se usa al cerrar la ficha, en En espera, en Saltar y al cambiar el Evento 1.</summary>
    private void LimpiarFila2()
    {
        HayFila2 = false;
        Evento2Elegido = null;
        Observacion2Pendiente = "";
        FilaActiva = 1;
    }

}
