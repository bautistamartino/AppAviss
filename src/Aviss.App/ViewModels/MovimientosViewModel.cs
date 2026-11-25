using System.Collections.ObjectModel;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// Una fila de "Últimos movimientos" lista para mostrar (RF-28): la hora, la caravana con su CVET y qué evento se agregó. No muestra la manga
/// ni quién lo hizo (solo los cierres hablan de su manga, porque son movimientos de manga).
/// </summary>
public sealed class MovimientoItem
{
    public MovimientoItem(MovimientoDto dto) => Dto = dto;

    public MovimientoDto Dto { get; }

    public bool EsCierre => Dto.Tipo == TipoMovimiento.Cierre;
    public bool EsRechazo => Dto.Tipo == TipoMovimiento.Rechazo;

    /// <summary>El nombre del solo evento (OK, Aborto, Rechazo...); el Detalle del movimiento ya lo trae.</summary>
    private string NombreEvento => string.IsNullOrWhiteSpace(Dto.Detalle) ? EventosCampo.NombreRechazo : Dto.Detalle;
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
        ? (string.IsNullOrWhiteSpace(Dto.Observaciones) ? NombreEvento : $"{NombreEvento} · {Dto.Observaciones.Trim()}")
        : Detalle.Length > 0 ? $"{Tipo} · {Detalle}" : Tipo;
}

/// <summary>Lógica de "Últimos movimientos" del rodeo en trabajo, con deshacer y editar (spec 003, RF-28, RF-29).</summary>
public sealed partial class MovimientosViewModel : ObservableObject
{
    public const string MensajeFalla = "Algo falló y no se cambió nada. Probá de nuevo.";

    private readonly ICampoService _campo;
    private readonly SesionTrabajo _sesion;

    public MovimientosViewModel(ICampoService campo, SesionTrabajo sesion)
    {
        (_campo, _sesion) = (campo, sesion);
        // Editar usa el mismo selector que la ficha, pero sin "+ Nuevo evento" (RF-12); elegir solo anota el evento, guardar es aparte.
        Selector = new SelectorEventosViewModel(permiteNuevo: false)
        {
            AlElegir = evento =>
            {
                ElegirEventoEditado(evento);
                return Task.CompletedTask;
            }
        };
    }

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
        if (item is null || !item.PuedeDeshacer || EstaOcupado) return;

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
            var resultado = await _campo.DeshacerAsync(new DeshacerCommand(tipo, item.Dto.RegistroId));
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

    /// <summary>
    /// Los eventos entre los que se puede elegir al editar: el mismo selector de dos niveles de la ficha. Sirve para cualquier registro
    /// (tacto, solo evento o Rechazo): se puede pasar de uno a otro.
    /// </summary>
    public SelectorEventosViewModel Selector { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayEdicion), nameof(TituloEdicion), nameof(PuedeAgregarEvento2Editado))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private MovimientoItem? _editando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoEditado), nameof(PuedeAgregarEvento2Editado))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private EventoCampoDto? _eventoEditado;

    /// <summary>Los datos del evento editado (días y resultado si tiene pasos); viajan en la corrección (spec 005, RF-27, RF-30).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEventoEditado))]
    private EleccionEvento? _eleccionEditada;

    [ObservableProperty]
    private string _observacionEditada = "";

    // ---- Segundo evento (spec 011, RF-16): el mismo registro puede traer un Evento 2 con su propia observación.

    /// <summary>La edición muestra la fila del Evento 2 (el registro ya traía uno o se tocó "+ evento").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAgregarEvento2Editado), nameof(TextoEvento2Editado))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private bool _hayFila2Editada;

    /// <summary>El Evento 2 elegido. Nulo con la fila abierta = falta elegirlo (o el que tenía el registro ya no está activo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEvento2Editado))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private EventoCampoDto? _evento2Editado;

    [ObservableProperty]
    private string _observacion2Editada = "";

    /// <summary>El selector está eligiendo el Evento 2 (y no el Evento 1).</summary>
    [ObservableProperty]
    private bool _eligiendoEvento2;

    /// <summary>Se ve "+ evento": el Evento 1 admite un segundo y todavía no hay fila 2.</summary>
    public bool PuedeAgregarEvento2Editado => Editando is not null && EventoEditado?.AdmiteSegundo == true && !HayFila2Editada;

    public string TextoEvento2Editado => Evento2Editado is { } e ? e.Nombre : "Elegí un evento";

    /// <summary>Un mensaje dentro de la edición (por ejemplo, el rechazo de la corrección).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeEdicion))]
    private string? _mensajeEdicion;

    public bool HayEdicion => Editando is not null;
    public bool HayMensajeEdicion => !string.IsNullOrEmpty(MensajeEdicion);
    public string TituloEdicion => Editando is { } e ? $"Editar el {QueEs(e)} de {e.Titulo}" : "";

    // "tacto", "rechazo" o "evento" (OK, Aborto, Revisar Nuevamente...: un solo evento del catálogo que no cambia el estado).
    private static string QueEs(MovimientoItem e) =>
        !e.EsRechazo ? "tacto"
        : e.Dto.EventoId is null || e.Dto.EventoId == Aviss.Application.Campo.EventosCampo.Rechazo ? "rechazo" : "evento";
    public string TextoEventoEditado => EventoEditado is { } e ? SelectorEventosViewModel.Resumen(e, EleccionEditada) : "Elegí un evento";

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
            // El registro se reconoce por su EventoId, no por el nombre. Si su evento ya no está activo no se elige ninguno (y no se resalta).
            Selector.Actualizar(await _campo.ListarEventosAsync());
            // Se precargan los días y el resultado del registro: si se vuelve a tocar el mismo evento, sus pasos abren con esos valores.
            var dto = item.Dto;
            var eleccion = dto.EventoId is { } id ? new EleccionEvento(id, dto.DiasPreniez, dto.DiasAborto, dto.ResultadoAborto) : null;
            Selector.Abrir(dto.EventoId, eleccion);
            var evento = Selector.Buscar(dto.EventoId);
            // Un Aborto viejo (sin días ni resultado) no está completo: no se deja elegido, hay que volver a elegirlo con sus pasos.
            var completo = evento is not null && (!evento.TienePasos || SelectorEventosViewModel.EstaCompleta(evento, eleccion));
            EventoEditado = completo ? evento : null;
            EleccionEditada = completo ? eleccion : null;
            ObservacionEditada = item.Dto.Observaciones ?? "";
            // El Evento 2 del registro, si tenía. Si ya no está activo no se elige ninguno: la fila queda abierta pidiendo elegir otro
            // o quitarla (como el Evento 1 en la spec 005), y no se puede guardar así.
            EligiendoEvento2 = false;
            HayFila2Editada = dto.Evento2Id is not null;
            Evento2Editado = Selector.Buscar(dto.Evento2Id) is { PuedeSerSegundo: true } e2 ? e2 : null;
            Observacion2Editada = dto.Observaciones2 ?? "";
            Editando = item;
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    [RelayCommand]
    private void ElegirEventoEditado(EventoCampoDto? estado)
    {
        if (estado is null) return;

        // El selector estaba eligiendo el Evento 2: queda en la fila 2 y el selector vuelve a mostrar el Evento 1 (spec 011).
        if (EligiendoEvento2)
        {
            if (EventoEditado is not { } evento1 || !estado.PuedeSerSegundo || estado.Id == evento1.Id) return;
            Evento2Editado = estado;
            MensajeEdicion = null;
            VolverAEvento1();
            return;
        }

        // Los datos de los pasos los armó el selector; un evento con pasos incompletos no se elige.
        var eleccion = Selector.EleccionActual is { } e && e.EventoId == estado.Id ? e : new EleccionEvento(estado.Id);
        if (estado.TienePasos && !SelectorEventosViewModel.EstaCompleta(estado, eleccion)) return;
        EleccionEditada = eleccion;
        EventoEditado = estado;
        MensajeEdicion = null;

        // RF-7: si el nuevo Evento 1 ya no admite un segundo, es igual al Evento 2 o lo deja no combinable, el Evento 2 se descarta con
        // el mensaje fijo. El Evento 1 elegido no se pierde.
        if (HayFila2Editada && (!estado.AdmiteSegundo || estado.Id == Evento2Editado?.Id || Evento2Editado?.PuedeSerSegundo == false))
        {
            var habiaAlgo = Evento2Editado is not null || Observacion2Editada.Length > 0;
            DescartarFila2Editada();
            if (habiaAlgo) MensajeEdicion = TrabajoViewModel.MensajeSeQuitoEvento2;
        }
    }

    /// <summary>"+ evento": abre la fila 2 y deja al selector listo para elegir el Evento 2 (sin el Evento 1, solo combinables).</summary>
    [RelayCommand]
    private void AgregarEvento2Editado()
    {
        if (!PuedeAgregarEvento2Editado) return;
        HayFila2Editada = true;
        EditarEvento2();
    }

    /// <summary>Toca la fila 2: el selector pasa a elegir el Evento 2.</summary>
    [RelayCommand]
    private void EditarEvento2()
    {
        if (!HayFila2Editada || EventoEditado is not { } evento1) return;
        EligiendoEvento2 = true;
        MensajeEdicion = null;
        Selector.AbrirSegundo(Evento2Editado?.Id, evento1.Id);
    }

    /// <summary>Toca la fila 1: el selector vuelve a elegir el Evento 1 (con todas las opciones).</summary>
    [RelayCommand]
    private void EditarEvento1() => VolverAEvento1();

    private void VolverAEvento1()
    {
        EligiendoEvento2 = false;
        Selector.Abrir(EventoEditado?.Id, EleccionEditada);
    }

    /// <summary>"Quitar": descarta el Evento 2 y su observación; el Evento 1 y la suya no se tocan.</summary>
    [RelayCommand]
    private void QuitarEvento2Editado()
    {
        if (!HayFila2Editada) return;
        DescartarFila2Editada();
        MensajeEdicion = null;
    }

    // Cierra la fila 2 y vuelve el selector al Evento 1 si estaba eligiendo el 2.
    private void DescartarFila2Editada()
    {
        HayFila2Editada = false;
        Evento2Editado = null;
        Observacion2Editada = "";
        if (EligiendoEvento2) VolverAEvento1();
    }

    [RelayCommand]
    private void CancelarEdicion()
    {
        Editando = null;
        EventoEditado = null;
        EleccionEditada = null;
        HayFila2Editada = false;
        Evento2Editado = null;
        Observacion2Editada = "";
        EligiendoEvento2 = false;
        Selector.LimpiarEleccion();   // un paso abierto (o el modo segundo) no queda para la próxima edición
        ObservacionEditada = "";
        MensajeEdicion = null;
    }

    // Con la fila 2 abierta hace falta un Evento 2 elegido (o quitarla): no se guarda una fila vacía ni un evento que ya no está activo.
    private bool PuedeGuardarEdicion => Editando is not null && EventoEditado is not null && (!HayFila2Editada || Evento2Editado is not null);

    /// <summary>Guarda la corrección. Si el servicio la rechaza, la edición sigue abierta con el motivo y no se cambió nada.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarEdicion))]
    private async Task GuardarEdicionAsync()
    {
        if (Editando is not { } item || EventoEditado is not { } evento || EstaOcupado) return;

        EstaOcupado = true;
        MensajeEdicion = null;
        try
        {
            var observacion = string.IsNullOrWhiteSpace(ObservacionEditada) ? null : ObservacionEditada.Trim();
            // El Evento 2 viaja dentro de la elección (spec 011). Un evento sin pasos y sin segundo manda la elección vacía, como siempre.
            var eleccion = evento.TienePasos ? EleccionEditada : null;
            if (HayFila2Editada && Evento2Editado is { } evento2)
            {
                var obs2 = string.IsNullOrWhiteSpace(Observacion2Editada) ? null : Observacion2Editada.Trim();
                eleccion = (eleccion ?? new EleccionEvento(evento.Id)) with { Segundo = new SegundoEventoElegido(evento2.Id, obs2) };
            }
            var resultado = await _campo.CorregirRegistroAsync(new CorregirRegistroCommand(
                item.Dto.RegistroId, evento.Id, observacion, eleccion));
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
