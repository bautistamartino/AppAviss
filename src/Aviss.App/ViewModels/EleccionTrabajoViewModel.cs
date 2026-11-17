using System.Collections.ObjectModel;
using Aviss.Application.Campo;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain.Entidades;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Un establecimiento para elegir (los rodeos vienen agrupados por establecimiento).</summary>
public sealed record EstablecimientoParaTrabajar(Guid Id, string Nombre);

/// <summary>
/// Lógica de la pantalla "Elegir veterinario, establecimiento y rodeo" (spec 003, RF-1 y RF-2).
/// No usa MAUI: habla con la fachada de campo, con las preferencias de la tablet y con la navegación por interfaces.
/// </summary>
public sealed partial class EleccionTrabajoViewModel : ObservableObject
{
    public const string MensajeFalla = "No se pudo completar la operación. Probá de nuevo.";

    private readonly ICampoService _campo;
    private readonly IPreferenciasDispositivo _preferencias;
    private readonly INavegacion _navegacion;
    private readonly SesionTrabajo _sesion;

    private IReadOnlyList<RodeoParaTrabajarDto> _todosLosRodeos = [];

    public EleccionTrabajoViewModel(ICampoService campo, IPreferenciasDispositivo preferencias, INavegacion navegacion, SesionTrabajo sesion)
        => (_campo, _preferencias, _navegacion, _sesion) = (campo, preferencias, navegacion, sesion);

    // ------------------------------------------------------------------ lo que se muestra

    /// <summary>Veterinarios activos (nunca "Sin asignar": no se trabaja como nadie).</summary>
    public ObservableCollection<VeterinarioDto> Veterinarios { get; } = [];
    public ObservableCollection<EstablecimientoParaTrabajar> Establecimientos { get; } = [];
    /// <summary>Rodeos del establecimiento elegido, con su avance de hoy.</summary>
    public ObservableCollection<RodeoParaTrabajarDto> Rodeos { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EmpezarCommand))]
    [NotifyPropertyChangedFor(nameof(PuedeEmpezar))]
    private VeterinarioDto? _veterinarioSeleccionado;

    [ObservableProperty]
    private EstablecimientoParaTrabajar? _establecimientoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EmpezarCommand))]
    [NotifyPropertyChangedFor(nameof(PuedeEmpezar))]
    private RodeoParaTrabajarDto? _rodeoSeleccionado;

    /// <summary>El veterinario recordado ya no está activo: se le pide elegir otro (RF-1).</summary>
    [ObservableProperty]
    private bool _recordadoNoDisponible;

    /// <summary>No hay ningún rodeo para trabajar: se ofrece ir a importar (RF-2).</summary>
    [ObservableProperty]
    private bool _noHayRodeos;

    // Alta de un veterinario nuevo (nombre obligatorio, matrícula opcional).
    [ObservableProperty]
    private string _nuevoNombre = "";

    [ObservableProperty]
    private string _nuevaMatricula = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);

    /// <summary>Sin veterinario o sin rodeo no se empieza (RF-1).</summary>
    public bool PuedeEmpezar => VeterinarioSeleccionado is not null && RodeoSeleccionado is not null;

    // ------------------------------------------------------------------ cargar

    /// <summary>Se llama cada vez que se muestra la pantalla: lee veterinarios y rodeos y preselecciona al recordado.</summary>
    [RelayCommand]
    public async Task CargarAsync()
    {
        MensajeError = null;
        try
        {
            var veterinarios = (await _campo.ListarVeterinariosActivosAsync())
                // Defensa: la fachada ya lo excluye, pero "Sin asignar" nunca debe poder elegirse para trabajar.
                .Where(v => v.Id != Usuario.SinAsignarId)
                .ToList();
            Veterinarios.Clear();
            foreach (var v in veterinarios) Veterinarios.Add(v);

            // Preselección: el último veterinario usado, si sigue activo. Si no, se pide elegir otro.
            var recordado = _preferencias.ObtenerVeterinarioRecordado();
            VeterinarioSeleccionado = recordado is null ? null : veterinarios.FirstOrDefault(v => v.Id == recordado);
            RecordadoNoDisponible = recordado is not null && VeterinarioSeleccionado is null;

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

    // ------------------------------------------------------------------ veterinario nuevo

    [RelayCommand]
    private async Task CrearVeterinarioAsync()
    {
        MensajeError = null;
        var nombre = NuevoNombre.Trim();
        if (nombre.Length == 0)
        {
            MensajeError = "Escribí el nombre del veterinario.";
            return;
        }

        try
        {
            var matricula = string.IsNullOrWhiteSpace(NuevaMatricula) ? null : NuevaMatricula.Trim();
            var resultado = await _campo.CrearVeterinarioAsync(nombre, matricula);
            if (!resultado.EsCorrecto)
            {
                // Por ejemplo, nombre repetido: el mensaje viene del servicio, sin datos de nadie.
                MensajeError = resultado.Mensaje;
                return;
            }

            NuevoNombre = "";
            NuevaMatricula = "";
            var idNuevo = resultado.Valor;
            await CargarVeterinariosAsync();
            VeterinarioSeleccionado = Veterinarios.FirstOrDefault(v => v.Id == idNuevo);
            RecordadoNoDisponible = false;
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    // Recarga solo la lista de veterinarios (después de crear uno), sin tocar la elección de rodeo.
    private async Task CargarVeterinariosAsync()
    {
        var lista = (await _campo.ListarVeterinariosActivosAsync()).Where(v => v.Id != Usuario.SinAsignarId).ToList();
        Veterinarios.Clear();
        foreach (var v in lista) Veterinarios.Add(v);
    }

    // ------------------------------------------------------------------ empezar

    [RelayCommand(CanExecute = nameof(PuedeEmpezar))]
    private async Task EmpezarAsync()
    {
        // Sin veterinario o sin rodeo no se empieza, aunque el botón haya quedado habilitado un instante.
        if (VeterinarioSeleccionado is not { } veterinario || RodeoSeleccionado is not { } rodeo) return;

        _preferencias.GuardarVeterinarioRecordado(veterinario.Id);
        _sesion.Empezar(veterinario, rodeo);
        await _navegacion.IrATrabajoAsync();
    }

    /// <summary>Volver a la pantalla de inicio sin empezar a trabajar.</summary>
    [RelayCommand]
    private Task VolverAlInicioAsync() => _navegacion.IrAInicioAsync();

    /// <summary>Sin rodeos importados se ofrece ir a importar (RF-2).</summary>
    [RelayCommand]
    private Task IrAImportarAsync() => _navegacion.IrAImportarAsync();
}
