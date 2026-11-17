using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

public partial class InicioPage : ContentPage
{
	public InicioPage(InicioViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
