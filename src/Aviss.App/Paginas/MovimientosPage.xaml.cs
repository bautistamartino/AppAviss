using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

/// <summary>Lista de últimos movimientos (spec 003). La lógica (deshacer) está en <see cref="MovimientosViewModel"/>.</summary>
public partial class MovimientosPage : ContentPage
{
	private readonly MovimientosViewModel _vm;

	public MovimientosPage(MovimientosViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try { await _vm.CargarAsync(); }
		catch (Exception) { /* el ViewModel ya muestra su mensaje fijo si algo falla */ }
	}

	// Volver a la pantalla de trabajo (la que está debajo en la pila): es navegación de la página, sin lógica de negocio.
	private async void OnVolverClicked(object? sender, EventArgs e)
	{
		try { await Shell.Current.GoToAsync(".."); }
		catch (Exception) { }
	}
}
