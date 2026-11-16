using Microsoft.Extensions.DependencyInjection;

namespace Aviss.App;

// Nombre completo: dentro de Aviss.* "Application" se confunde con el espacio de nombres Aviss.Application.
public partial class App : Microsoft.Maui.Controls.Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}