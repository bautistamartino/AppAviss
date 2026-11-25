namespace Aviss.App.Diagnostico;

/// <summary>Pantalla "Diagnóstico del bastón" (spike, solo DEBUG). Sin lógica: todo está en <see cref="DiagnosticoBastonViewModel"/>.</summary>
public partial class DiagnosticoBastonPage : ContentPage
{
	private readonly DiagnosticoBastonViewModel _vm;

	public DiagnosticoBastonPage(DiagnosticoBastonViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_vm.Iniciar();
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();
		try
		{
			// Al salir se corta la conexión y se cierra el archivo (queda guardado en la carpeta privada de la app).
			await _vm.CerrarAsync();
		}
		catch (Exception)
		{
			// Un async void que lanza tira abajo la app; cerrar es lo último que se hace.
		}
	}
}
