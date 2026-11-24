using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

/// <summary>Elegir establecimiento y rodeo (spec 003; sin veterinario desde la 006). Sin lógica: todo está en el ViewModel.</summary>
public partial class EleccionTrabajoPage : ContentPage
{
	private readonly EleccionTrabajoViewModel _vm;

	public EleccionTrabajoPage(EleccionTrabajoViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			// Vuelve a leer los rodeos cada vez que se muestra (el avance cambia mientras se trabaja).
			await _vm.CargarAsync();
		}
		catch (Exception)
		{
			// Un async void que lanza tira abajo la app; el ViewModel ya muestra el error si algo falla al cargar.
		}
	}
}
