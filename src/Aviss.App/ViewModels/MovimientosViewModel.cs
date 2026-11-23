using System.Collections.ObjectModel;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// Una fila de "Últimos movimientos" lista para mostrar (RF-28): la hora, la caravana con su CVET y qué evento se agregó. No muestra la manga
/// ni el veterinario (solo los cierres hablan de su manga, porque son movimientos de manga).
/// </summary>
public sealed class MovimientoItem
{
    public MovimientoItem(MovimientoDto dto) => Dto = dto;

    public MovimientoDto Dto { get; }

    public bool EsCierre => Dto.Tipo == TipoMovimiento.Cierre;
    public bool EsRechazo => Dto.Tipo == TipoMovimiento.Rechazo;
    public bool Anulado => Dto.Anulado;
    public bool PuedeDeshacer => Dto.PuedeDeshacer;

    /// <summary>
    /// Solo un tacto que todavía se puede deshacer (el último registro vigente de la vaca) se puede editar: editar es anularlo y
    /// registrar uno nuevo, y eso vale en las mismas condiciones.
    /// </summary>
    public bool PuedeEditar => Dto.Tipo is TipoMovimiento.Tacto or TipoMovimiento.Rechazo && !Dto.Anulado && Dto.PuedeDeshacer;

    /// <summary>Qué se hizo: "Marca", "Tacto" o "Cierre de manga".</summary>
    public string Tipo => Dto.Tipo switch
    {
        TipoMovimiento.Tacto => "Tacto",
        TipoMovimiento.Marca => "Marca",
        TipoMovimiento.Rechazo => "Rechazo",
        _ => "Cierre de manga"
    };

    /// <summary>La caravana (en un cierre, la manga). Son datos de pantalla: nunca se escriben en logs.</summary>
    public string Titulo => EsCierre ? $"Manga {Dto.NumeroManga}" : Dto.Caravana ?? "";

    /// <summary>Hora local del movimiento (RF-28).</summary>
    public string Hora => Dto.FechaHoraUtc.ToLocalTime().ToString("HH:mm");

    /// <summary>"CVET PRE": por qué la vaca estaba en la lista de tacto. Vacío si el Excel no lo traía o es un cierre.</summary>
    public string TextoCvet => !EsCierre && !string.IsNullOrWhiteSpace(Dto.Cvet) ? $"CVET {Dto.Cvet}" : "";

    public bool HayCvet => TextoCvet.Length > 0;

    /// <summary>El evento que se agregó (el estado del tacto) o, en un cierre, su tipo.</summary>
    public string Detalle => EsRechazo ? "" : Dto.Detalle ?? "";

    /// <summary>La observación del tacto. En un rechazo es el motivo y ya va en <see cref="TextoTipoDetalle"/> ("Rechazo · pata"): no se repite.</summary>
    public string Observacion => EsRechazo ? "" : Dto.Observaciones ?? "";
    public bool HayObservacion => Observacion.Length > 0;

    /// <summary>
    /// En un cierre, las dos cifras cuando difieren: lo que se guardó al cerrar y el contador de hoy
    /// ("Al cerrar: 14 · Hoy: 13", RF-8). Vacío si no hace falta.
    /// </summary>
    public string TextoCierre =>
        EsCierre && Dto.CantidadAviss is { } alCerrar && Dto.ContadorActual is { } hoy && alCerrar != hoy
            ? $"Al cerrar: {alCerrar} · Hoy: {hoy}"
            : "";

    public bool HayTextoCierre => TextoCierre.Length > 0;

    /// <summary>Hay algo para mostrar bajo el título (evento, observación o cifras del cierre): una marca sola no lo tiene y la fila queda más baja.</summary>
    public bool HayDetalle => Detalle.Length > 0 || HayObservacion || HayTextoCierre;

    /// <summary>El segundo renglón de la fila, junto: "Tacto · PREÑADA", "Marca" o "Cierre de manga · Manual".</summary>
    public string TextoTipoDetalle => EsRechazo
        ? (string.IsNullOrWhiteSpace(Dto.Observaciones) ? "Rechazo" : $"Rechazo · {Dto.Observaciones.Trim()}")
        : Detalle.Length > 0 ? $"{Tipo} · {Detalle}" : Tipo;
}

/// <summary>Lógica de "Últimos movimientos" del rodeo en trabajo, con deshacer y editar (spec 003, RF-28, RF-29).</summary>
public sealed partial class MovimientosViewModel : ObservableObject
{
    public const string MensajeFalla = "Algo falló y no se cambió nada. Probá de nuevo.";

    private readonly ICampoService _campo;
    private readonly SesionTrabajo _sesion;

    public MovimientosViewModel(ICampoService campo, SesionTrabajo sesion) => (_campo, _sesion) = (campo, sesion);

    public ObservableCollection<MovimientoItem> Movimientos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    /// <summary>"Se deshizo" y similares (también el aviso de que una caravana quedó libre).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeInfo))]
    private string? _mensajeInfo;

    [ObservableProperty]
    private bool _estaOcupado;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);
    public bool HayMensajeInfo => !string.IsNullOrEmpty(MensajeInfo);
    public bool HayMovimientos => Movimientos.Count > 0;

    /// <summary>Los 50 movimientos más recientes del rodeo en trabajo.</summary>
    [RelayCommand]
    public async Task CargarAsync()
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        try
        {
            var lista = await _campo.ListarMovimientosAsync(rodeo.RodeoId);
            Movimientos.Clear();
            foreach (var m in lista) Movimientos.Add(new MovimientoItem(m));
            OnPropertyChanged(nameof(HayMovimientos));
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    /// <summary>
    /// Deshace un movimiento. Si el servicio lo rechaza (ya no es el último, otro día...) muestra el motivo y no cambia
    /// nada; si lo acepta, vuelve a leer la lista.
    /// </summary>
    [RelayCommand]
    private async Task DeshacerAsync(MovimientoItem? item)
    {
        if (item is null || !item.PuedeDeshacer || EstaOcupado || _sesion.Veterinario is not { } vet) return;

        EstaOcupado = true;
        MensajeError = null;
        MensajeInfo = null;
        try
        {
            var tipo = item.Dto.Tipo switch
            {
                TipoMovimiento.Tacto => TipoDeshacer.Tacto,
                TipoMovimiento.Marca => TipoDeshacer.Marca,
                TipoMovimiento.Rechazo => TipoDeshacer.Rechazo,
                _ => TipoDeshacer.Cierre
            };
            var resultado = await _campo.DeshacerAsync(new DeshacerCommand(tipo, item.Dto.RegistroId, vet.Id));
            if (!resultado.EsCorrecto)
            {
                MensajeError = resultado.Mensaje;
                return;
            }

            MensajeInfo = "Se deshizo.";
            await CargarAsync();
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ================================================================== editar un tacto o un rechazo
    // Los registros solo se agregan: editar es anular el tacto o el rechazo y registrar uno nuevo con el evento y la observación elegidos, todo junto
    // (si el nuevo se rechaza, el original sigue como estaba).

    /// <summary>Los eventos entre los que se puede elegir al editar: los estados activos y "Rechazo" al final (se puede pasar de tacto a rechazo y al revés).</summary>
    public ObservableCollection<EstadoActivoDto> EstadosParaEditar { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEdicion), nameof(TituloEdicion))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private MovimientoItem? _editando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoEditado))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private EstadoActivoDto? _eventoEditado;

    [ObservableProperty]
    private string _observacionEditada = "";

    /// <summary>Un mensaje dentro de la edición (por ejemplo, el rechazo de la corrección).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeEdicion))]
    private string? _mensajeEdicion;

    public bool HayEdicion => Editando is not null;
    public bool HayMensajeEdicion => !string.IsNullOrEmpty(MensajeEdicion);
    public string TituloEdicion => Editando is { } e ? $"Editar el {(e.EsRechazo ? "rechazo" : "tacto")} de {e.Titulo}" : "";
    public string TextoEventoEditado => EventoEditado?.Nombre ?? "Elegí un evento";

    /// <summary>Abre la edición con el evento y la observación actuales ya cargados.</summary>
    [RelayCommand]
    private async Task EditarAsync(MovimientoItem? item)
    {
        if (item is null || !item.PuedeEditar || EstaOcupado) return;
        try
        {
            MensajeError = null;
            MensajeInfo = null;
            MensajeEdicion = null;
            var estados = await _campo.ListarEstadosActivosAsync();
            EstadosParaEditar.Clear();
            foreach (var e in estados) EstadosParaEditar.Add(e);
            EstadosParaEditar.Add(TrabajoViewModel.EventoRechazo);

            EventoEditado = item.EsRechazo
                ? TrabajoViewModel.EventoRechazo
                : estados.FirstOrDefault(e => string.Equals(e.Nombre, item.Dto.Detalle, StringComparison.OrdinalIgnoreCase));
            ObservacionEditada = item.Dto.Observaciones ?? "";
            Editando = item;
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    [RelayCommand]
    private void ElegirEventoEditado(EstadoActivoDto? estado)
    {
        if (estado is null) return;
        EventoEditado = estado;
        MensajeEdicion = null;
    }

    [RelayCommand]
    private void CancelarEdicion()
    {
        Editando = null;
        EventoEditado = null;
        ObservacionEditada = "";
        MensajeEdicion = null;
    }

    private bool PuedeGuardarEdicion => Editando is not null && EventoEditado is not null;

    /// <summary>Guarda la corrección. Si el servicio la rechaza, la edición sigue abierta con el motivo y no se cambió nada.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarEdicion))]
    private async Task GuardarEdicionAsync()
    {
        if (Editando is not { } item || EventoEditado is not { } evento || EstaOcupado || _sesion.Veterinario is not { } vet) return;

        EstaOcupado = true;
        MensajeEdicion = null;
        try
        {
            var observacion = string.IsNullOrWhiteSpace(ObservacionEditada) ? null : ObservacionEditada.Trim();
            var resultado = await _campo.CorregirRegistroAsync(new CorregirRegistroCommand(item.Dto.RegistroId, evento.Id, observacion, vet.Id));
            if (!resultado.EsCorrecto)
            {
                MensajeEdicion = resultado.Mensaje;
                return;
            }

            CancelarEdicion();
            MensajeInfo = "Se corrigió el registro.";
            await CargarAsync();
        }
        catch (Exception)
        {
            MensajeEdicion = MensajeFalla;
        }
        finally
        {
            EstaOcupado = false;
        }
    }
}
