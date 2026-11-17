using Aviss.App.Paginas;

namespace Aviss.App;

public partial class AppShell : Shell
{
	public const string RutaInicio = "Inicio";
	public const string RutaImportar = "Importar";

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(RutaImportar, typeof(ImportarPage));
	}
}
