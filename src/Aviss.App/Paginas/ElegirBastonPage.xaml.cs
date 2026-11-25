using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

/// <summary>Elegir el bastón emparejado (spec 008). Sin lógica: todo está en el ViewModel.</summary>
public partial class ElegirBastonPage : ContentPage
{
	private readonly ElegirBastonViewModel _vm;

	public ElegirBastonPage(ElegirBastonViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			// Pide el permiso de Bluetooth si falta (recién acá, no al abrir la app) y arma la lista.
			await _vm.CargarAsync();
		}
		catch (Exception)
		{
			// Un async void que lanza tira abajo la app; sin lista el botón "Actualizar" permite reintentar.
		}
	}
}
