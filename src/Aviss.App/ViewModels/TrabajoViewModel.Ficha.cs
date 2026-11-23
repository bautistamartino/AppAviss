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


    /// <summary>La ficha abierta es de una vaca con CVET 0 (se abrió con "Ver ficha"): no se puede tactar.</summary>
    public bool NoEntraEnTacto => HayFicha && !Ficha!.EntraEnTacto;

    /// <summary>Solo una vaca que entra en tacto muestra "Agregar evento" y "Agregar observación".</summary>
    public bool PuedeAgregarEventos => HayFicha && Ficha!.EntraEnTacto;

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

    /// <summary>Abre la ficha de esa vaca solo para mirar: no la vuelve a marcar y, como no entra en tacto, no deja agregar eventos.</summary>
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

    private bool PuedeAbrirCuadroDeFicha => Ficha is { EntraEnTacto: true } && Etapa == EtapaTrabajo.Ficha && Cuadro == CuadroTrabajo.Ninguno;

    /// <summary>El casillero "Evento": abre todas las opciones; elegir una la deja en el casillero (se guarda con "Entrar").</summary>
    [RelayCommand]
    private async Task AbrirEventosAsync()
    {
        if (!PuedeAbrirCuadroDeFicha) return;
        MensajeCuadro = null;
        HayFormularioEvento = false;
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
        if (Cuadro != CuadroTrabajo.Eventos) return;
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
        var estados = await EnCampoAsync(() => _campo.ListarEstadosActivosAsync());
        Estados.Clear();
        foreach (var e in estados) Estados.Add(e);
        OnPropertyChanged(nameof(HayEstadosActivos));
        ReconstruirEventos();   // la lista de eventos de la ficha suma el "Rechazo" fijo
    }


    /// <summary>"Agregar observación": abre el texto con lo ya escrito, si había algo.</summary>
    [RelayCommand]
    private async Task AbrirObservacionAsync()
    {
        if (!PuedeAbrirCuadroDeFicha) return;
        TextoObservacion = ObservacionPendiente;
        await CambiarCuadroAsync(CuadroTrabajo.Observacion);
    }

    /// <summary>Guarda el texto como observación pendiente (vacío = sin observación). No guarda ningún tacto todavía.</summary>
    [RelayCommand]
    private async Task GuardarObservacionAsync()
    {
        if (Cuadro != CuadroTrabajo.Observacion) return;
        ObservacionPendiente = TextoObservacion.Trim();
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    [RelayCommand]
    private void QuitarObservacion() => ObservacionPendiente = "";

    /// <summary>Cierra "Agregar evento" o "Agregar observación" sin cambiar nada y vuelve a la ficha.</summary>
    [RelayCommand]
    private async Task CerrarCuadroAgregarAsync()
    {
        if (Cuadro is not (CuadroTrabajo.Eventos or CuadroTrabajo.Observacion)) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // ---- El evento elegido y "Entrar": elegir no guarda, Entrar sí (evita guardar por un toque erróneo)

    /// <summary>El evento que se eligió para esta vaca. Queda en el casillero "Evento" hasta que se toca "Entrar".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoElegido), nameof(HayEventoElegido))]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private EstadoActivoDto? _eventoElegido;

    public bool HayEventoElegido => EventoElegido is not null;

    /// <summary>Lo que muestra el casillero "Evento".</summary>
    public string TextoEventoElegido => EventoElegido?.Nombre ?? "Tocá para elegir";

    /// <summary>Lo que muestra el casillero "Observación".</summary>
    public string TextoCasilleroObservacion => HayObservacionPendiente ? ObservacionPendiente : "Opcional";

    /// <summary>"Elegir" un evento de la lista: queda en el casillero y se vuelve a la ficha. No guarda nada todavía.</summary>
    [RelayCommand]
    private async Task ElegirEventoAsync(EstadoActivoDto? estado)
    {
        // Solo con una ficha de una vaca que entra en tacto y sin ningún cuadro encima (salvo la lista, desde donde se elige).
        if (estado is null || Ficha is not { EntraEnTacto: true } || Etapa != EtapaTrabajo.Ficha
            || Cuadro is not (CuadroTrabajo.Ninguno or CuadroTrabajo.Eventos)) return;

        EventoElegido = estado;
        MensajeError = null;
        if (Cuadro == CuadroTrabajo.Eventos) await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Se puede tocar "Entrar": ficha de una vaca que entra en tacto, sin cuadros encima y con un evento elegido.</summary>
    private bool PuedeEntrar => Ficha is { EntraEnTacto: true } && Etapa == EtapaTrabajo.Ficha && Cuadro == CuadroTrabajo.Ninguno && EventoElegido is not null;

    /// <summary>"Entrar": guarda el tacto con el evento elegido y la observación (si hay).</summary>
    [RelayCommand(CanExecute = nameof(PuedeEntrar))]
    private async Task EntrarAsync()
    {
        if (Ficha is not { EntraEnTacto: true } ficha || EventoElegido is not { } estado || Etapa != EtapaTrabajo.Ficha || Cuadro != CuadroTrabajo.Ninguno) return;

        try
        {
            MensajeError = null;
            var observacion = HayObservacionPendiente ? ObservacionPendiente : null;
            // El evento elegido decide qué se guarda: "Rechazo" es otro tipo de registro (el motivo va en la observación); un estado
            // del catálogo es un tacto. Si la vaca estaba en espera, el servicio la resuelve en la misma transacción.
            var esRechazo = estado.Id == EventosCampo.Rechazo;
            var resultado = esRechazo
                ? await EnCampoAsync(() => _campo.RegistrarRechazoAsync(new RegistrarRechazoCommand(ficha.AnimalId, observacion, _sesion.UsuarioId)))
                : await EnCampoAsync(() => _campo.RegistrarTactoAsync(
                    new RegistrarTactoCommand(ficha.AnimalId, estado.Id, null, observacion, _sesion.UsuarioId)));
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
            UltimoGuardado = $"{ficha.Caravana}: {estado.Nombre}";
            PublicarAvisoDeshacer(new AvisoDeshacerDto(esRechazo ? TipoDeshacer.Rechazo : TipoDeshacer.Tacto, resultado.Valor, $"{ficha.Caravana}: {estado.Nombre}"));
            await RecalcularAsync();
            if (ficha.EnEspera) await CargarEsperasAsync();   // la espera quedó resuelta: sale de la botonera
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

}
