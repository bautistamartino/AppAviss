using System.Collections.ObjectModel;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Los tres pasos de la pantalla de importación. Es una sola página: así la vista previa sobrevive a rotar o a pasar a segundo plano.</summary>
public enum PasoImportacion { Elegir, VistaPrevia, Resultado }

/// <summary>
/// Lógica de la pantalla "Importar rodeo" (spec 002, RF-13 a RF-16, RF-19, RF-21, RF-22).
/// No usa tipos de MAUI: habla con el servicio de importación, con el selector de archivos y con la navegación a través de interfaces.
/// </summary>
public sealed partial class ImportarViewModel : ObservableObject
{
    // Mensajes fijos para las fallas reales (excepciones): nunca llevan datos de animales ni de tambos (RF-24).
    public const string MensajeFallaLectura = "No se pudo leer el archivo. Probá de nuevo o elegí otro.";
    public const string MensajeFallaConfirmar = "No se pudo guardar la importación y no se guardó nada. Revisá el espacio libre de la tablet y probá de nuevo.";

    private readonly IImportacionService _servicio;
    private readonly ISelectorArchivo _selector;
    private readonly INavegacion _navegacion;

    public ImportarViewModel(IImportacionService servicio, ISelectorArchivo selector, INavegacion navegacion)
        => (_servicio, _selector, _navegacion) = (servicio, selector, navegacion);

    // ------------------------------------------------------------------ estado

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPasoElegir), nameof(EsPasoVistaPrevia), nameof(EsPasoResultado))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand), nameof(CancelarCommand))]
    private PasoImportacion _paso = PasoImportacion.Elegir;

    /// <summary>Leyendo y validando el archivo elegido.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ElegirArchivoCommand))]
    private bool _estaOcupado;

    /// <summary>Guardando la importación. Mientras dura no se puede elegir otro archivo ni confirmar de nuevo (RF-19).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ElegirArchivoCommand), nameof(ConfirmarCommand), nameof(CancelarCommand))]
    private bool _estaConfirmando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilasLeidas), nameof(FilasAImportar), nameof(FilasExcluidas), nameof(HayExcluidas),
        nameof(HayAdvertencias), nameof(AdvertenciasTexto), nameof(HayConteoEstados), nameof(EsArchivoRepetido), nameof(PuedeConfirmar), nameof(RodeoRepetidoTexto),
        nameof(HayResumenEid), nameof(ResumenEidTexto))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private VistaPreviaImportacion? _vista;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayResumenEid), nameof(ResumenEidTexto))]
    private ResultadoImportacion? _resultado;

    // Datos que completa el veterinario en la vista previa.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private string _nombreRodeo = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private string _nuevoEstablecimientoNombre = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private EstablecimientoResumen? _establecimientoSeleccionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeElegirExistente))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private bool _usarEstablecimientoNuevo = true;

    /// <summary>"Importar igual": aceptar de forma explícita un archivo que ya se importó antes (RF-16).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private bool _confirmaImportarIgual;

    /// <summary>Establecimientos activos para elegir. Si no hay ninguno, solo se puede escribir uno nuevo.</summary>
    public ObservableCollection<EstablecimientoResumen> Establecimientos { get; } = [];

    // ------------------------------------------------------------------ derivados (para la pantalla)

    public bool EsPasoElegir => Paso == PasoImportacion.Elegir;
    public bool EsPasoVistaPrevia => Paso == PasoImportacion.VistaPrevia;
    public bool EsPasoResultado => Paso == PasoImportacion.Resultado;
    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);

    public int FilasLeidas => Vista?.FilasLeidas ?? 0;
    public int FilasAImportar => Vista?.Validas.Count ?? 0;
    public int FilasExcluidas => Vista?.Excluidas.Count ?? 0;
    public bool HayExcluidas => FilasExcluidas > 0;
    public bool HayAdvertencias => Vista?.Advertencias.Count > 0;
    /// <summary>Cada advertencia en una línea, con las filas de ejemplo (RF-11). Son datos de pantalla: no se escriben en logs.</summary>
    public IReadOnlyList<string> AdvertenciasTexto =>
        Vista?.Advertencias
            .Select(a => a.FilasEjemplo.Count == 0
                ? a.Mensaje
                : $"{a.Mensaje} Filas de ejemplo: {string.Join(", ", a.FilasEjemplo)}.")
            .ToList() ?? [];
    public bool HayConteoEstados => Vista?.ConteoPorEstado.Count > 0;
    public bool EsArchivoRepetido => Vista?.EsArchivoRepetido ?? false;
    /// <summary>Spec 010 (RF-8): solo si el archivo trae columna EID. En la vista previa sale de la vista; en el resultado, del resultado (la vista ya se liberó).</summary>
    public bool HayResumenEid => Vista?.HayColumnaEid ?? Resultado?.HayColumnaEid ?? false;
    /// <summary>Texto fijo con solo cantidades, nunca un EID (RF-19).</summary>
    public string ResumenEidTexto =>
        Vista is { HayColumnaEid: true } v ? $"Con EID: {v.ConEid} · Sin EID: {v.SinEid}"
        : Resultado is { HayColumnaEid: true } r ? $"Con EID: {r.ConEid} · Sin EID: {r.SinEid}"
        : "";
    public bool HayEstablecimientos => Establecimientos.Count > 0;
    public bool PuedeElegirExistente => HayEstablecimientos && !UsarEstablecimientoNuevo;

    /// <summary>Nombre y fecha del rodeo ya importado con el mismo archivo (se muestra solo en pantalla, nunca en logs).</summary>
    public string RodeoRepetidoTexto
    {
        get
        {
            var anterior = Vista?.ArchivoRepetido.FirstOrDefault();
            return anterior is null
                ? ""
                : $"Este archivo ya se importó como \"{anterior.Nombre}\" el {anterior.FechaImportacionUtc.ToLocalTime():dd/MM/yyyy HH:mm}.";
        }
    }

    public bool PuedeConfirmar =>
        Paso == PasoImportacion.VistaPrevia
        && !EstaConfirmando
        && Vista is { PuedeConfirmar: true }
        && !string.IsNullOrWhiteSpace(NombreRodeo)
        && (!EsArchivoRepetido || ConfirmaImportarIgual)
        && (UsarEstablecimientoNuevo
            ? !string.IsNullOrWhiteSpace(NuevoEstablecimientoNombre)
            : EstablecimientoSeleccionado is not null);

    // Cuando cambia algo que influye en "Confirmar" se avisa a la pantalla.
    partial void OnPasoChanged(PasoImportacion value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnEstaConfirmandoChanged(bool value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnNombreRodeoChanged(string value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnNuevoEstablecimientoNombreChanged(string value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnEstablecimientoSeleccionadoChanged(EstablecimientoResumen? value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnUsarEstablecimientoNuevoChanged(bool value) => OnPropertyChanged(nameof(PuedeConfirmar));
    partial void OnConfirmaImportarIgualChanged(bool value) => OnPropertyChanged(nameof(PuedeConfirmar));

    // ------------------------------------------------------------------ paso 1: elegir

    private bool PuedeElegir() => !EstaOcupado && !EstaConfirmando;

    [RelayCommand(CanExecute = nameof(PuedeElegir))]
    private async Task ElegirArchivoAsync()
    {
        if (!PuedeElegir()) return;
        EstaOcupado = true;
        try
        {
            ArchivoElegido? archivo;
            try
            {
                archivo = await _selector.ElegirAsync();
            }
            catch (Exception)
            {
                // Falla del selector (permisos, archivo en la nube sin bajar...). Mensaje fijo, sin la ruta.
                MensajeError = MensajeFallaLectura;
                return;
            }

            if (archivo is null) return;   // canceló: no cambia nada

            MensajeError = null;
            // El flujo se cierra apenas se leyó: el servicio ya se quedó con los bytes.
            await using var flujo = archivo.Contenido;
            Resultado<VistaPreviaImportacion> preparado;
            try
            {
                preparado = await _servicio.PrepararAsync(archivo.Nombre, flujo);
            }
            catch (Exception)
            {
                MensajeError = MensajeFallaLectura;
                return;
            }

            if (!preparado.EsCorrecto)
            {
                MensajeError = preparado.Mensaje;   // archivo rechazado: se puede elegir otro
                return;
            }

            await MostrarVistaPreviaAsync(preparado.Valor);
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private async Task MostrarVistaPreviaAsync(VistaPreviaImportacion vista)
    {
        // Lista de establecimientos activos para elegir; si la lectura falla, se puede escribir uno nuevo igual.
        IReadOnlyList<EstablecimientoResumen> lista;
        try { lista = await _servicio.ListarEstablecimientosActivosAsync(); }
        catch (Exception) { lista = []; }

        Establecimientos.Clear();
        foreach (var e in lista) Establecimientos.Add(e);
        OnPropertyChanged(nameof(HayEstablecimientos));

        EstablecimientoSeleccionado = null;
        NuevoEstablecimientoNombre = "";
        ConfirmaImportarIgual = false;
        UsarEstablecimientoNuevo = !HayEstablecimientos;   // sin establecimientos solo se puede crear uno
        OnPropertyChanged(nameof(PuedeElegirExistente));

        // Nombre de rodeo propuesto: el del archivo sin extensión (editable).
        var propuesto = Path.GetFileNameWithoutExtension(vista.NombreArchivo).Trim();
        if (propuesto.Length > LimitesImportacion.MaxLargoNombre) propuesto = propuesto[..LimitesImportacion.MaxLargoNombre].TrimEnd();
        NombreRodeo = propuesto;

        Vista = vista;
        Paso = PasoImportacion.VistaPrevia;
    }

    // ------------------------------------------------------------------ paso 2: confirmar o cancelar

    private bool PuedeConfirmarComando() => PuedeConfirmar;

    [RelayCommand(CanExecute = nameof(PuedeConfirmarComando))]
    private async Task ConfirmarAsync()
    {
        // Doble toque: aunque el botón esté habilitado un instante, acá se corta (y el servicio también protege, RF-19).
        if (!PuedeConfirmar || Vista is null) return;

        var comando = new ConfirmarImportacionCommand(
            Vista.Id,
            UsarEstablecimientoNuevo ? null : EstablecimientoSeleccionado?.Id,
            UsarEstablecimientoNuevo ? NuevoEstablecimientoNombre.Trim() : null,
            NombreRodeo.Trim(),
            EsArchivoRepetido && ConfirmaImportarIgual);

        EstaConfirmando = true;
        MensajeError = null;
        try
        {
            var respuesta = await _servicio.ConfirmarAsync(comando);
            if (respuesta.EsCorrecto)
            {
                Resultado = respuesta.Valor;
                Vista = null;   // libera la vista previa: ya está guardada
                Paso = PasoImportacion.Resultado;
            }
            else if (respuesta.Codigo is CodigoError.VistaPreviaNoDisponible or CodigoError.ImportacionYaConfirmada)
            {
                // La vista previa ya no existe: no hay nada que reintentar, hay que elegir el archivo de nuevo.
                MensajeError = respuesta.Mensaje;
                Vista = null;
                Paso = PasoImportacion.Elegir;
            }
            else
            {
                // Rechazo de negocio (nombre repetido, sin espacio...): se queda en la vista previa para corregir y reintentar.
                MensajeError = respuesta.Mensaje;
            }
        }
        catch (Exception)
        {
            // Falla real (disco lleno, error de base). El servicio ya devolvió la vista a Pendiente: se puede reintentar.
            MensajeError = MensajeFallaConfirmar;
        }
        finally
        {
            EstaConfirmando = false;
        }
    }

    private bool PuedeCancelar() => Paso == PasoImportacion.VistaPrevia && !EstaConfirmando;

    [RelayCommand(CanExecute = nameof(PuedeCancelar))]
    private Task CancelarAsync()
    {
        if (!PuedeCancelar()) return Task.CompletedTask;
        if (Vista is not null) _servicio.Descartar(Vista.Id);   // RF-22: no queda nada guardado
        Reiniciar();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ paso 3: resultado

    [RelayCommand]
    private Task ImportarOtroAsync()
    {
        Reiniciar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task VolverAlInicioAsync()
    {
        Reiniciar();
        return _navegacion.IrAInicioAsync();
    }

    private void Reiniciar()
    {
        Vista = null;
        Resultado = null;
        MensajeError = null;
        NombreRodeo = "";
        NuevoEstablecimientoNombre = "";
        EstablecimientoSeleccionado = null;
        ConfirmaImportarIgual = false;
        Establecimientos.Clear();
        OnPropertyChanged(nameof(HayEstablecimientos));
        Paso = PasoImportacion.Elegir;
    }
}
