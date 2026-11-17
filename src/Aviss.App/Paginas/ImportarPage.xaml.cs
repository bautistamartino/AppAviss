using Aviss.App.ViewModels;

namespace Aviss.App.Paginas;

public partial class ImportarPage : ContentPage
{
	public ImportarPage(ImportarViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
