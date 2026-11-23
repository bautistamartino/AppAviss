using System.Windows.Input;
using Aviss.App.ViewModels;

namespace Aviss.App.Controles;

/// <summary>
/// Control del selector de eventos (spec 005). No decide nada: muestra lo que le dice el <see cref="SelectorEventosViewModel"/>.
/// La página le pasa el ViewModel por <see cref="Selector"/> (y no por BindingContext) para poder enlazar también
/// <see cref="NuevoEventoCommand"/> contra el ViewModel de la página.
/// </summary>
public partial class SelectorEventosView : ContentView
{
    public static readonly BindableProperty SelectorProperty = BindableProperty.Create(
        nameof(Selector), typeof(SelectorEventosViewModel), typeof(SelectorEventosView),
        propertyChanged: (b, _, nuevo) => ((SelectorEventosView)b).Raiz.BindingContext = nuevo);

    public static readonly BindableProperty NuevoEventoCommandProperty = BindableProperty.Create(
        nameof(NuevoEventoCommand), typeof(ICommand), typeof(SelectorEventosView));

    public SelectorEventosView() => InitializeComponent();

    public SelectorEventosViewModel? Selector
    {
        get => (SelectorEventosViewModel?)GetValue(SelectorProperty);
        set => SetValue(SelectorProperty, value);
    }

    /// <summary>Lo que hace "+ Nuevo evento" (la ficha abre el formulario; la edición de movimientos no lo muestra).</summary>
    public ICommand? NuevoEventoCommand
    {
        get => (ICommand?)GetValue(NuevoEventoCommandProperty);
        set => SetValue(NuevoEventoCommandProperty, value);
    }
}
