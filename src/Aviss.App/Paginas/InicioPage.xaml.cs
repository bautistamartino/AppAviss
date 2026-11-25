using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

public partial class InicioPage : ContentPage
{
	public InicioPage(InicioViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
#if DEBUG
		// Spike del bastón (spec 008): el botón solo existe en la compilación de desarrollo, como el simulador de lecturas.
		BotonDiagnostico.IsVisible = true;
		BotonDiagnostico.Clicked += async (_, _) =>
		{
			try { await Shell.Current.GoToAsync(AppShell.RutaDiagnosticoBaston); }
			catch (Exception) { /* sin ruta no hay pantalla: no se rompe el inicio */ }
		};
#endif
	}
}
