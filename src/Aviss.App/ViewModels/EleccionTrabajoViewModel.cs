using System.Collections.ObjectModel;
using Aviss.Application.Campo;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Un establecimiento para elegir (los rodeos vienen agrupados por establecimiento).</summary>
public sealed record EstablecimientoParaTrabajar(Guid Id, string Nombre);

/// <summary>
/// Lógica de la pantalla "Elegir con qué trabajar": establecimiento y rodeo (spec 003, RF-2; spec 006: sin veterinario).
/// No usa MAUI: habla con la fachada de campo y con la navegación por interfaces.
/// </summary>
public sealed partial class EleccionTrabajoViewModel : ObservableObject
{
    public const string MensajeFalla = "No se pudo completar la operación. Probá de nuevo.";

    private readonly ICampoService _campo;
    private readonly INavegacion _navegacion;
    private readonly SesionTrabajo _sesion;

    private IReadOnlyList<RodeoParaTrabajarDto> _todosLosRodeos = [];

    public EleccionTrabajoViewModel(ICampoService campo, INavegacion navegacion, SesionTrabajo sesion)
        => (_campo, _navegacion, _sesion) = (campo, navegacion, sesion);

    // ------------------------------------------------------------------ lo que se muestra

    public ObservableCollection<EstablecimientoParaTrabajar> Establecimientos { get; } = [];
    /// <summary>Rodeos del establecimiento elegido, con su avance de hoy.</summary>
    public ObservableCollection<RodeoParaTrabajarDto> Rodeos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstablecimientoElegido))]
    [NotifyPropertyChangedFor(nameof(FaltaEstablecimiento))]
    [NotifyPropertyChangedFor(nameof(QueFalta))]
    private EstablecimientoParaTrabajar? _establecimientoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EmpezarCommand))]
    [NotifyPropertyChangedFor(nameof(PuedeEmpezar))]
    [NotifyPropertyChangedFor(nameof(FaltaAlgo))]
    [NotifyPropertyChangedFor(nameof(QueFalta))]
    [NotifyPropertyChangedFor(nameof(TextoRodeoElegido))]
    private RodeoParaTrabajarDto? _rodeoSeleccionado;

    /// <summary>No hay ningún rodeo para trabajar: se ofrece ir a importar (RF-2).</summary>
    [ObservableProperty]
    private bool _noHayRodeos;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);

    /// <summary>Con un rodeo elegido ya se puede empezar (el rodeo siempre es de un establecimiento) (RF-2).</summary>
    public bool PuedeEmpezar => RodeoSeleccionado is not null;

    /// <summary>Falta elegir algo: se muestra la línea "qué falta" sobre el botón de empezar.</summary>
    public bool FaltaAlgo => !PuedeEmpezar;

    /// <summary>Qué falta para poder empezar, en una frase corta ("" si ya está todo). Solo habla del establecimiento y el rodeo.</summary>
    public string QueFalta => (EstablecimientoSeleccionado is null, RodeoSeleccionado is null) switch
    {
        (true, _) => "Falta elegir un establecimiento y un rodeo",
        (false, true) => "Falta elegir un rodeo",
        _ => "",
    };

    /// <summary>"✓ Elegido: X" bajo el título de cada paso; vacío mientras no se eligió nada.</summary>
    public string TextoEstablecimientoElegido => EstablecimientoSeleccionado is null ? "" : $"✓ Elegido: {EstablecimientoSeleccionado.Nombre}";
    public string TextoRodeoElegido => RodeoSeleccionado is null ? "" : $"✓ Elegido: {RodeoSeleccionado.RodeoNombre}";

    /// <summary>El paso 2 depende del 1: mientras no haya establecimiento elegido se avisa.</summary>
    public bool FaltaEstablecimiento => EstablecimientoSeleccionado is null;

    // ------------------------------------------------------------------ cargar

    /// <summary>Se llama cada vez que se muestra la pantalla: lee los rodeos (el avance cambia mientras se trabaja).</summary>
    [RelayCommand]
    public async Task CargarAsync()
    {
        MensajeError = null;
        try
        {
            _todosLosRodeos = await _campo.ListarRodeosParaTrabajarAsync();
            NoHayRodeos = _todosLosRodeos.Count == 0;

            Establecimientos.Clear();
            foreach (var e in _todosLosRodeos.GroupBy(r => r.EstablecimientoId).Select(g => new EstablecimientoParaTrabajar(g.Key, g.First().EstablecimientoNombre)))
                Establecimientos.Add(e);

            // Si hay un solo establecimiento se elige solo: un toque menos.
            EstablecimientoSeleccionado = Establecimientos.Count == 1 ? Establecimientos[0] : null;
            CargarRodeosDelEstablecimiento();
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    partial void OnEstablecimientoSeleccionadoChanged(EstablecimientoParaTrabajar? value) => CargarRodeosDelEstablecimiento();

    private void CargarRodeosDelEstablecimiento()
    {
        Rodeos.Clear();
        RodeoSeleccionado = null;
        if (EstablecimientoSeleccionado is null) return;
        foreach (var r in _todosLosRodeos.Where(r => r.EstablecimientoId == EstablecimientoSeleccionado.Id)) Rodeos.Add(r);
        if (Rodeos.Count == 1) RodeoSeleccionado = Rodeos[0];
    }

    // ------------------------------------------------------------------ resetear un rodeo (para pruebas), con doble verificación

    /// <summary>El rodeo que se está por resetear (vale mientras se muestran las dos preguntas).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayReseteo), nameof(EsPrimerPasoReseteo), nameof(EsSegundoPasoReseteo), nameof(NombreRodeoAResetear))]
    private RodeoParaTrabajarDto? _rodeoAResetear;

    /// <summary>0 = nada; 1 = primera pregunta; 2 = segunda pregunta (la que ejecuta).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPrimerPasoReseteo), nameof(EsSegundoPasoReseteo))]
    private int _pasoReseteo;

    [ObservableProperty]
    private bool _estaReseteando;

    /// <summary>Un aviso (por ejemplo, "Rodeo reseteado").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeInfo))]
    private string? _mensajeInfo;

    public bool HayMensajeInfo => !string.IsNullOrEmpty(MensajeInfo);
    public bool HayReseteo => RodeoAResetear is not null;
    public bool EsPrimerPasoReseteo => HayReseteo && PasoReseteo == 1;
    public bool EsSegundoPasoReseteo => HayReseteo && PasoReseteo == 2;
    public string NombreRodeoAResetear => RodeoAResetear?.RodeoNombre ?? "";

    /// <summary>El botón chico de un rodeo: abre la primera pregunta.</summary>
    [RelayCommand]
    private void PedirReseteo(RodeoParaTrabajarDto? rodeo)
    {
        if (rodeo is null || EstaReseteando) return;
        MensajeError = null;
        MensajeInfo = null;
        RodeoAResetear = rodeo;
        PasoReseteo = 1;
    }

    /// <summary>"Sí" a la primera pregunta: pasa a la segunda. Todavía no se cambia nada.</summary>
    [RelayCommand]
    private void ConfirmarPrimerPasoReseteo()
    {
        if (!EsPrimerPasoReseteo) return;
        PasoReseteo = 2;
    }

    [RelayCommand]
    private void CancelarReseteo()
    {
        RodeoAResetear = null;
        PasoReseteo = 0;
    }

    /// <summary>"Sí" a la segunda pregunta: resetea el rodeo (se vuelve a importar de su Excel original y el viejo se da de baja).</summary>
    [RelayCommand]
    private async Task ConfirmarReseteoAsync()
    {
        if (!EsSegundoPasoReseteo || RodeoAResetear is not { } rodeo || EstaReseteando) return;

        EstaReseteando = true;
        try
        {
            var nombre = rodeo.RodeoNombre;
            var resultado = await _campo.ResetearRodeoAsync(rodeo.RodeoId);
            CancelarReseteo();
            if (!resultado.EsCorrecto)
            {
                MensajeError = resultado.Mensaje;
                return;
            }

            await CargarAsync();   // vuelve a leer los rodeos: el nuevo aparece en 0
            MensajeInfo = $"Rodeo reseteado: {nombre} volvió a como se subió el Excel.";
        }
        catch (Exception)
        {
            CancelarReseteo();
            MensajeError = MensajeFalla;
        }
        finally
        {
            EstaReseteando = false;
        }
    }

    // ------------------------------------------------------------------ editar el nombre de un establecimiento

    /// <summary>El establecimiento cuyo nombre se está editando (la capa está abierta mientras no sea nulo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEdicionEstablecimiento))]
    private EstablecimientoParaTrabajar? _establecimientoAEditar;

    /// <summary>Lo que se escribe en la capa; arranca con el nombre actual.</summary>
    [ObservableProperty]
    private string _nombreEditado = "";

    /// <summary>Por qué no se pudo guardar (nombre vacío, repetido o falla). Se muestra DENTRO de la capa, que queda abierta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeErrorEdicion))]
    private string? _mensajeErrorEdicion;

    [ObservableProperty]
    private bool _estaGuardandoEdicion;

    public bool HayEdicionEstablecimiento => EstablecimientoAEditar is not null;
    public bool HayMensajeErrorEdicion => !string.IsNullOrEmpty(MensajeErrorEdicion);

    /// <summary>El botón "Editar" de un establecimiento: abre la capa con su nombre actual. Todavía no se cambia nada.</summary>
    [RelayCommand]
    private void PedirEditarEstablecimiento(EstablecimientoParaTrabajar? establecimiento)
    {
        if (establecimiento is null || EstaGuardandoEdicion) return;
        MensajeError = null;
        MensajeInfo = null;
        MensajeErrorEdicion = null;
        NombreEditado = establecimiento.Nombre;
        EstablecimientoAEditar = establecimiento;
    }

    [RelayCommand]
    private void CancelarEdicionEstablecimiento()
    {
        if (EstaGuardandoEdicion) return;
        EstablecimientoAEditar = null;
        MensajeErrorEdicion = null;
        NombreEditado = "";
    }

    /// <summary>
    /// "Guardar": renombra y vuelve a leer la lista. Si el servicio rechaza (vacío, repetido) la capa queda abierta con el motivo.
    /// El establecimiento y el rodeo que estaban elegidos se mantienen.
    /// </summary>
    [RelayCommand]
    private async Task GuardarEdicionEstablecimientoAsync()
    {
        if (EstablecimientoAEditar is not { } editado || EstaGuardandoEdicion) return;

        EstaGuardandoEdicion = true;
        MensajeErrorEdicion = null;
        try
        {
            var resultado = await _campo.RenombrarEstablecimientoAsync(editado.Id, NombreEditado);
            if (!resultado.EsCorrecto)
            {
                MensajeErrorEdicion = resultado.Mensaje;
                return;
            }

            // Se recuerda lo elegido ANTES de recargar: CargarAsync vacía las listas y la elección.
            var establecimientoId = EstablecimientoSeleccionado?.Id;
            var rodeoId = RodeoSeleccionado?.RodeoId;

            EstablecimientoAEditar = null;
            NombreEditado = "";
            await CargarAsync();

            if (establecimientoId is { } id && Establecimientos.FirstOrDefault(e => e.Id == id) is { } igual)
            {
                EstablecimientoSeleccionado = igual;   // recarga sus rodeos y deja el rodeo en nulo
                if (rodeoId is { } rid) RodeoSeleccionado = Rodeos.FirstOrDefault(r => r.RodeoId == rid) ?? RodeoSeleccionado;
            }
            MensajeInfo = "Nombre del establecimiento actualizado.";
        }
        catch (Exception)
        {
            MensajeErrorEdicion = MensajeFalla;
        }
        finally
        {
            EstaGuardandoEdicion = false;
        }
    }

    // ------------------------------------------------------------------ empezar

    [RelayCommand(CanExecute = nameof(PuedeEmpezar))]
    private async Task EmpezarAsync()
    {
        // Sin rodeo no se empieza, aunque el botón haya quedado habilitado un instante.
        if (RodeoSeleccionado is not { } rodeo) return;

        _sesion.Empezar(rodeo);
        await _navegacion.IrATrabajoAsync();
    }

    /// <summary>Volver a la pantalla de inicio sin empezar a trabajar.</summary>
    [RelayCommand]
    private Task VolverAlInicioAsync() => _navegacion.IrAInicioAsync();

    /// <summary>Sin rodeos importados se ofrece ir a importar (RF-2).</summary>
    [RelayCommand]
    private Task IrAImportarAsync() => _navegacion.IrAImportarAsync();
}
