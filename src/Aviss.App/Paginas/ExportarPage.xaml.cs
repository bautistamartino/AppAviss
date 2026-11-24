using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

/// <summary>Pantalla "Exportar" (spec 007). Sin lógica: todo está en <see cref="ExportarViewModel"/>.</summary>
public partial class ExportarPage : ContentPage
{
	private readonly ExportarViewModel _vm;

	public ExportarPage(ExportarViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			// Lee los rodeos cada vez que se muestra (no hace nada si ya se eligió uno o se está generando).
			await _vm.CargarAsync();
		}
		catch (Exception)
		{
			// Un async void que lanza tira abajo la app; el ViewModel ya muestra el error si algo falla al cargar.
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		// Si se sale (botón Atrás de Android) mientras se genera, se cancela y no se guarda nada (RF-28).
		_vm.AlSalir();
	}
}
