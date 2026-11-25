using System.Collections.ObjectModel;
using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// El selector de eventos de dos niveles (spec 005, RF-6 a RF-12, RF-14): los eventos principales y, aparte, "Otro evento" con todos los
/// demás. Lo usan la ficha (elegir el evento de la vaca) y "Últimos movimientos" (editar un registro): una sola lógica para las dos
/// pantallas, así la lista y el resaltado se comportan igual.
/// <para>
/// No guarda nada: elegir solo avisa a quien lo usa (<see cref="AlElegir"/>) y deja el evento resaltado. La lista siempre se abre en los
/// principales (<see cref="Abrir"/>); "Otro evento" es una vista dentro del mismo selector, no una pantalla ni un cuadro nuevo.
/// </para>
/// <para>
/// <b>Pasos posteriores (RF-27).</b> Un evento del catálogo puede declarar pasos (<see cref="PasoEvento"/>): Preñada fecha previa pide los
/// días de preñez; Aborto pide los días de aborto y en qué queda la vaca. Al elegirlo el selector no avisa todavía: abre el primer paso
/// (<see cref="PasoActual"/>), valida cada número y recién con todos los pasos completos arma la <see cref="EleccionActual"/> y avisa.
/// Es un único mecanismo para todos los eventos con pasos (también para uno de "Otro evento"): un evento nuevo con pasos no pide código nuevo.
/// Mientras hay un paso abierto no se guarda nada; <see cref="CancelarCommand"/> deja todo como estaba.
/// </para>
/// </summary>
public sealed partial class SelectorEventosViewModel : ObservableObject
{
    /// <param name="permiteNuevo">La ficha deja crear eventos con "+ Agregar evento" (RF-8); la edición de movimientos no.</param>
    public SelectorEventosViewModel(bool permiteNuevo) => PermiteNuevo = permiteNuevo;

    /// <summary>
    /// Se llama al elegir un evento (si tiene pasos, recién cuando están todos completos). Quien usa el selector decide qué hacer (la ficha
    /// lo deja en el casillero; la edición lo anota) y lee los datos del evento en <see cref="EleccionActual"/>.
    /// </summary>
    public Func<EventoCampoDto, Task>? AlElegir { get; set; }

    public bool PermiteNuevo { get; }

    /// <summary>Los principales activos, en el orden del catálogo (RF-6).</summary>
    public ObservableCollection<EventoCampoDto> Principales { get; } = [];

    /// <summary>Los de "Otro evento": el Rechazo primero y el resto alfabético (RF-7).</summary>
    public ObservableCollection<EventoCampoDto> Otros { get; } = [];

    /// <summary>Los resultados que se ofrecen al elegir Aborto: los de los estados activos y Rechazo siempre (RF-28).</summary>
    public ObservableCollection<ResultadoAborto> ResultadosDisponibles { get; } = [];

    // Las listas completas tal como las armó el servicio: las colecciones de arriba son lo que se ve (filtradas en "modo segundo").
    private IReadOnlyList<EventoCampoDto> _todosPrincipales = [];
    private IReadOnlyList<EventoCampoDto> _todosOtros = [];

    /// <summary>Texto fijo cuando, en modo segundo, no queda ningún evento para combinar (spec 011, RF-3).</summary>
    public const string TextoSinCombinables = "No hay otros eventos para combinar";

    /// <summary>
    /// "Modo segundo" (spec 011): el selector elige el Evento 2. Muestra solo los eventos que pueden ser segundo
    /// (<see cref="EventoCampoDto.PuedeSerSegundo"/>) y esconde el Evento 1 (<see cref="IdExcluido"/>). Es la misma clase y la misma lista:
    /// solo cambia el filtro.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListaSegundoVacia), nameof(TextoListaVacia))]
    private bool _modoSegundo;

    /// <summary>En modo segundo, el Evento 1 de la vaca: no se ofrece como Evento 2 (RF-8).</summary>
    public Guid? IdExcluido { get; private set; }

    /// <summary>En modo segundo no hay ningún evento para combinar: la lista dice <see cref="TextoSinCombinables"/> y deja volver.</summary>
    public bool ListaSegundoVacia => ModoSegundo && Principales.Count == 0 && Otros.Count == 0;

    public string TextoListaVacia => ListaSegundoVacia ? TextoSinCombinables : "";

    // Arma lo que se ve a partir de las listas completas y del modo actual.
    private void Refiltrar()
    {
        bool Visible(EventoCampoDto e) => !ModoSegundo || (e.PuedeSerSegundo && e.Id != IdExcluido);
        Principales.Clear();
        foreach (var e in _todosPrincipales.Where(Visible)) Principales.Add(e);
        Otros.Clear();
        foreach (var e in _todosOtros.Where(Visible)) Otros.Add(e);
        OnPropertyChanged(nameof(HayPrincipales));
        OnPropertyChanged(nameof(OtroEventoResaltado));
        OnPropertyChanged(nameof(ListaSegundoVacia));
        OnPropertyChanged(nameof(TextoListaVacia));
    }

    /// <summary>Se está viendo la lista de "Otro evento" (y no los principales).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoPrincipales), nameof(PuedeCrearNuevo))]
    private bool _mostrandoOtros;

    public bool MostrandoPrincipales => !MostrandoOtros;

    /// <summary>El evento marcado en la lista: el estado actual de la vaca al abrir y, después, el que se elija (RF-9).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtroEventoResaltado))]
    private Guid? _resaltado;

    /// <summary>
    /// "Otro evento" se ve resaltado cuando el evento resaltado está dentro de esa lista. Un evento desactivado no está en ninguna lista,
    /// así que no resalta nada (RF-9, RF-13).
    /// </summary>
    public bool OtroEventoResaltado => Resaltado is { } id && Otros.Any(o => o.Id == id);

    /// <summary>Hay al menos un principal activo; si no, la lista muestra solo "Otro evento" (RF-11).</summary>
    public bool HayPrincipales => Principales.Count > 0;

    /// <summary>"+ Agregar evento" se ve solo si el selector lo permite, solo desde la vista de "Otro evento" y sin un paso abierto (RF-8).</summary>
    public bool PuedeCrearNuevo => PermiteNuevo && MostrandoOtros && PasoActual is null;

    /// <summary>La lista (principales u "Otro evento") está a la vista: no hay ningún paso abierto encima.</summary>
    public bool MostrandoLista => PasoActual is null;

    /// <summary>
    /// Pone las listas que armó el servicio. No cambia la vista ni el resaltado: tras crear un evento el selector sigue en
    /// "Otro evento" y el evento nuevo aparece ahí sin quedar elegido (RF-8).
    /// </summary>
    public void Actualizar(ListaEventosDto lista)
    {
        _todosPrincipales = lista.Principales;
        _todosOtros = lista.Otros;
        Refiltrar();   // respeta el modo actual (un evento creado en modo segundo aparece si es combinable)
        ResultadosDisponibles.Clear();
        foreach (var r in lista.ResultadosAborto) ResultadosDisponibles.Add(r);
    }

    /// <summary>
    /// Deja el selector listo para mostrarse: siempre en los principales, con el evento actual resaltado (RF-9) y sin ningún paso abierto.
    /// <paramref name="eleccion"/> son los datos que ya tiene el evento elegido (días y resultado): precargan los pasos si se vuelve a elegir
    /// el mismo evento (RF-27, punto 6) y sirven para el resumen.
    /// </summary>
    public void Abrir(Guid? resaltado, EleccionEvento? eleccion = null)
    {
        DescartarPasos();
        MostrandoOtros = false;
        ModoSegundo = false;
        IdExcluido = null;
        Refiltrar();
        Resaltado = resaltado;
        EleccionActual = eleccion;
    }

    /// <summary>
    /// Deja el selector listo para elegir el <b>Evento 2</b> (spec 011): en los principales, sin el Evento 1 (<paramref name="idEvento1"/>) y solo
    /// con los que pueden ser segundo. <paramref name="resaltado"/> es el Evento 2 ya elegido, si hay.
    /// </summary>
    public void AbrirSegundo(Guid? resaltado, Guid idEvento1)
    {
        DescartarPasos();
        MostrandoOtros = false;
        ModoSegundo = true;
        IdExcluido = idEvento1;
        Refiltrar();
        Resaltado = resaltado;
        EleccionActual = null;
    }

    /// <summary>Olvida la elección (la ficha se cerró o cambió de vaca): nada de una vaca pasa a la siguiente.</summary>
    public void LimpiarEleccion()
    {
        DescartarPasos();
        EleccionActual = null;
        Resaltado = null;
        if (ModoSegundo)
        {
            ModoSegundo = false;
            IdExcluido = null;
            Refiltrar();
        }
    }

    /// <summary>Busca un evento por su id en cualquiera de las dos listas completas (aunque el modo segundo lo esconda).</summary>
    public EventoCampoDto? Buscar(Guid? id) =>
        id is null ? null : _todosPrincipales.Concat(_todosOtros).FirstOrDefault(e => e.Id == id);

    [RelayCommand]
    private void VerOtros() => MostrandoOtros = true;

    [RelayCommand]
    private void Volver() => MostrandoOtros = false;

    // ================================================================== elegir y pasos posteriores (RF-27, RF-28)

    /// <summary>
    /// Lo elegido y completo: el evento y, si tiene pasos, sus días y su resultado. Nulo si todavía no se eligió nada. Lo cambian solo
    /// <see cref="Abrir"/> y la finalización de los pasos: cancelar nunca lo toca.
    /// </summary>
    [ObservableProperty]
    private EleccionEvento? _eleccionActual;

    /// <summary>El paso que está abierto, o nulo si se ve la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayPaso), nameof(MostrandoLista), nameof(PideDias), nameof(PideResultado), nameof(PuedeCrearNuevo))]
    private PasoEvento? _pasoActual;

    public bool HayPaso => PasoActual is not null;
    public bool PideDias => PasoActual is PasoEvento.DiasPreniez or PasoEvento.DiasAborto;
    public bool PideResultado => PasoActual is PasoEvento.ResultadoAborto;

    /// <summary>Lo que se está escribiendo en el campo de días (texto: la validación decide si es un número válido).</summary>
    [ObservableProperty]
    private string _textoDias = "";

    /// <summary>El motivo por el que no se pasa de paso (días mal escritos). El paso queda abierto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);

    /// <summary>El resultado del aborto elegido (se resalta al volver o al reabrir con los valores actuales).</summary>
    [ObservableProperty]
    private ResultadoAborto? _resultadoElegido;

    // El evento cuyos pasos se están recorriendo y los datos ya confirmados de cada paso.
    private EventoCampoDto? _enPasos;
    private int _indicePaso;
    private int? _diasPreniez;
    private int? _diasAborto;

    /// <summary>"Preñada fecha previa", "Aborto"...: el evento que se está completando.</summary>
    public string TituloPaso => _enPasos?.Nombre ?? "";

    /// <summary>"Siguiente" si después de este paso viene otro; "Confirmar" si es el último.</summary>
    public string TextoConfirmar => _enPasos is { } e && _indicePaso + 1 < e.Pasos.Count ? "Siguiente" : "Confirmar";

    /// <summary>Hay un paso anterior al que volver (en el primero solo se puede cancelar).</summary>
    public bool PuedeVolverDePaso => PasoActual is not null && _indicePaso > 0;

    /// <summary>El resumen del casillero: "Aborto, 45 días, Vacía OK", "Preñada fecha previa, 130 días" o solo el nombre.</summary>
    /// <param name="evento2">Spec 011: el segundo evento, si hay; se une con " + " ("OK + PROSTAG").</param>
    public static string Resumen(EventoCampoDto evento, EleccionEvento? eleccion, EventoCampoDto? evento2 = null) =>
        eleccion is { } e && e.EventoId == evento.Id
            ? DescripcionEvento.Armar(evento.Nombre, e.DiasPreniez, e.DiasAborto, e.ResultadoAborto, evento2?.Nombre)
            : DescripcionEvento.Armar(evento.Nombre, nombreEvento2: evento2?.Nombre);

    /// <summary>La elección trae todos los datos que los pasos del evento piden (un Aborto viejo, sin días ni resultado, no la cumple).</summary>
    public static bool EstaCompleta(EventoCampoDto evento, EleccionEvento? eleccion)
    {
        if (eleccion is null || eleccion.EventoId != evento.Id) return false;
        foreach (var paso in evento.Pasos)
        {
            var falta = paso switch
            {
                PasoEvento.DiasPreniez => eleccion.DiasPreniez is null,
                PasoEvento.DiasAborto => eleccion.DiasAborto is null,
                _ => eleccion.ResultadoAborto is null
            };
            if (falta) return false;
        }
        return true;
    }

    /// <summary>
    /// Elegir un evento. Sin pasos: queda resaltado y se avisa (no guarda nada). Con pasos: abre el primero, con los valores actuales
    /// si ya estaba elegido, y no avisa hasta completarlos todos.
    /// </summary>
    [RelayCommand]
    private async Task ElegirAsync(EventoCampoDto? evento)
    {
        if (evento is null || PasoActual is not null) return;

        if (evento.TienePasos)
        {
            IniciarPasos(evento);
            return;
        }

        Resaltado = evento.Id;
        EleccionActual = new EleccionEvento(evento.Id);
        if (AlElegir is { } alElegir) await alElegir(evento);
    }

    private void IniciarPasos(EventoCampoDto evento)
    {
        _enPasos = evento;
        _indicePaso = 0;
        // Si se vuelve a elegir el mismo evento, los pasos arrancan con lo que ya tenía (RF-27, punto 6).
        var previa = EleccionActual is { } e && e.EventoId == evento.Id ? e : null;
        _diasPreniez = previa?.DiasPreniez;
        _diasAborto = previa?.DiasAborto;
        ResultadoElegido = previa?.ResultadoAborto;
        MostrarPaso();
    }

    private void MostrarPaso()
    {
        MensajeError = null;
        var paso = _enPasos!.Pasos[_indicePaso];
        TextoDias = paso switch
        {
            PasoEvento.DiasPreniez => _diasPreniez?.ToString(CultureInfo.InvariantCulture) ?? "",
            PasoEvento.DiasAborto => _diasAborto?.ToString(CultureInfo.InvariantCulture) ?? "",
            _ => ""
        };
        PasoActual = paso;
        AvisarTextosDelPaso();
    }

    /// <summary>"Confirmar" / "Siguiente" del paso de días: valida el número y, si es válido, pasa al próximo paso o completa el evento.</summary>
    [RelayCommand]
    private async Task ConfirmarDiasAsync()
    {
        if (PasoActual is not (PasoEvento.DiasPreniez or PasoEvento.DiasAborto) || _enPasos is null) return;

        var rango = PasoActual == PasoEvento.DiasPreniez ? RangosDias.Preniez : RangosDias.Aborto;
        var resultado = ValidadorDias.Validar(TextoDias, rango);
        if (!resultado.EsValido)
        {
            MensajeError = resultado.Mensaje;   // el paso sigue abierto con lo que se escribió
            return;
        }

        if (PasoActual == PasoEvento.DiasPreniez) _diasPreniez = resultado.Valor;
        else _diasAborto = resultado.Valor;
        await AvanzarAsync();
    }

    /// <summary>Elige en qué queda la vaca tras el aborto (uno de los resultados disponibles) y completa el evento.</summary>
    [RelayCommand]
    private async Task ElegirResultadoAsync(ResultadoAborto resultado)
    {
        if (PasoActual != PasoEvento.ResultadoAborto || _enPasos is null || !ResultadosDisponibles.Contains(resultado)) return;
        ResultadoElegido = resultado;
        await AvanzarAsync();
    }

    private async Task AvanzarAsync()
    {
        var evento = _enPasos!;
        if (_indicePaso + 1 < evento.Pasos.Count)
        {
            _indicePaso++;
            MostrarPaso();
            return;
        }

        // Último paso: se arma lo elegido con los datos que el evento pide (y solo esos) y recién ahora se avisa.
        var pasos = evento.Pasos;
        var eleccion = new EleccionEvento(evento.Id,
            pasos.Contains(PasoEvento.DiasPreniez) ? _diasPreniez : null,
            pasos.Contains(PasoEvento.DiasAborto) ? _diasAborto : null,
            pasos.Contains(PasoEvento.ResultadoAborto) ? ResultadoElegido : null);
        DescartarPasos();
        Resaltado = evento.Id;
        EleccionActual = eleccion;
        if (AlElegir is { } alElegir) await alElegir(evento);
    }

    /// <summary>"Volver" dentro de los pasos: al paso anterior, conservando lo ya escrito.</summary>
    [RelayCommand]
    private void VolverDePaso()
    {
        if (PasoActual is null || _indicePaso == 0) return;
        _indicePaso--;
        MostrarPaso();
    }

    /// <summary>"Cancelar": cierra los pasos y vuelve a la lista sin elegir nada; lo que estaba en el casillero queda igual.</summary>
    [RelayCommand]
    private void Cancelar() => DescartarPasos();

    private void DescartarPasos()
    {
        _enPasos = null;
        _indicePaso = 0;
        _diasPreniez = _diasAborto = null;
        ResultadoElegido = null;
        TextoDias = "";
        MensajeError = null;
        PasoActual = null;
        AvisarTextosDelPaso();
    }

    // El título, el texto del botón y "Volver" dependen del evento y del índice del paso, no solo del paso: se avisan a mano.
    private void AvisarTextosDelPaso()
    {
        OnPropertyChanged(nameof(TituloPaso));
        OnPropertyChanged(nameof(TextoConfirmar));
        OnPropertyChanged(nameof(PuedeVolverDePaso));
    }
}
