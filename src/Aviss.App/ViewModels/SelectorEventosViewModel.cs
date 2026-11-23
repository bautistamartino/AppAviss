using System.Collections.ObjectModel;
using Aviss.Application.Campo;
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
/// </summary>
public sealed partial class SelectorEventosViewModel : ObservableObject
{
    /// <param name="permiteNuevo">La ficha deja crear eventos con "+ Nuevo evento" (RF-8); la edición de movimientos no.</param>
    public SelectorEventosViewModel(bool permiteNuevo) => PermiteNuevo = permiteNuevo;

    /// <summary>Se llama al elegir un evento. Quien usa el selector decide qué hacer (la ficha lo deja en el casillero; la edición lo anota).</summary>
    public Func<EventoCampoDto, Task>? AlElegir { get; set; }

    public bool PermiteNuevo { get; }

    /// <summary>Los principales activos, en el orden del catálogo (RF-6).</summary>
    public ObservableCollection<EventoCampoDto> Principales { get; } = [];

    /// <summary>Los de "Otro evento": el Rechazo primero y el resto alfabético (RF-7).</summary>
    public ObservableCollection<EventoCampoDto> Otros { get; } = [];

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

    /// <summary>"+ Nuevo evento" se ve solo si el selector lo permite y solo desde la vista de "Otro evento" (RF-8).</summary>
    public bool PuedeCrearNuevo => PermiteNuevo && MostrandoOtros;

    /// <summary>
    /// Pone las listas que armó el servicio. No cambia la vista ni el resaltado: tras crear un evento el selector sigue en
    /// "Otro evento" y el evento nuevo aparece ahí sin quedar elegido (RF-8).
    /// </summary>
    public void Actualizar(ListaEventosDto lista)
    {
        Principales.Clear();
        foreach (var e in lista.Principales) Principales.Add(e);
        Otros.Clear();
        foreach (var e in lista.Otros) Otros.Add(e);
        OnPropertyChanged(nameof(HayPrincipales));
        OnPropertyChanged(nameof(OtroEventoResaltado));
    }

    /// <summary>Deja el selector listo para mostrarse: siempre en los principales y con el evento actual resaltado (RF-9).</summary>
    public void Abrir(Guid? resaltado)
    {
        MostrandoOtros = false;
        Resaltado = resaltado;
    }

    /// <summary>Busca un evento por su id en cualquiera de las dos listas.</summary>
    public EventoCampoDto? Buscar(Guid? id) =>
        id is null ? null : Principales.Concat(Otros).FirstOrDefault(e => e.Id == id);

    [RelayCommand]
    private void VerOtros() => MostrandoOtros = true;

    [RelayCommand]
    private void Volver() => MostrandoOtros = false;

    /// <summary>Elegir un evento: queda resaltado y se avisa a quien usa el selector. No guarda nada.</summary>
    [RelayCommand]
    private async Task ElegirAsync(EventoCampoDto? evento)
    {
        if (evento is null) return;
        Resaltado = evento.Id;
        if (AlElegir is { } alElegir) await alElegir(evento);
    }
}
