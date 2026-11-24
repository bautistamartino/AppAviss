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
        propertyChanged: (b, viejo, nuevo) => ((SelectorEventosView)b).AlCambiarSelector(viejo as SelectorEventosViewModel, nuevo as SelectorEventosViewModel));

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

    private void AlCambiarSelector(SelectorEventosViewModel? viejo, SelectorEventosViewModel? nuevo)
    {
        Raiz.BindingContext = nuevo;
        if (viejo is not null) viejo.PropertyChanged -= AlCambiarPropiedad;
        if (nuevo is not null) nuevo.PropertyChanged += AlCambiarPropiedad;
    }

    // Al cambiar de paso (días -> resultado, confirmar días, cancelar) el teclado numérico se baja solo: si no, queda tapando
    // "Rechazo", "Volver" y "Cancelar". Es solo de pantalla; la lógica de los pasos está en el ViewModel.
    private async void AlCambiarPropiedad(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectorEventosViewModel.PasoActual)) return;
        try
        {
            CampoDias.Unfocus();
            await CampoDias.HideSoftInputAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Si el teclado no se puede bajar no pasa nada: el paso ya cambió.
        }
    }
}
