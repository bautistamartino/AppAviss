using Aviss.App.Paginas;

namespace Aviss.App;

public partial class AppShell : Shell
{
	public const string RutaInicio = "Inicio";
	public const string RutaImportar = "Importar";

	// Flujo de campo (spec 003). Solo los nombres: cada página registra su ruta cuando exista (T4 y T5).
	public const string RutaEleccionTrabajo = "EleccionTrabajo";
	public const string RutaTrabajo = "Trabajo";
	public const string RutaMovimientos = "Movimientos";

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(RutaImportar, typeof(ImportarPage));
		Routing.RegisterRoute(RutaTrabajo, typeof(TrabajoPage));   // T4; las de elección y movimientos se suman en T5
	}
}
