using Aviss.App.Paginas;

namespace Aviss.App;

public partial class AppShell : Shell
{
	public const string RutaInicio = "Inicio";
	public const string RutaImportar = "Importar";
	public const string RutaExportar = "Exportar";

	// Flujo de campo (spec 003). Nombres de las rutas (se registran abajo).
	public const string RutaEleccionTrabajo = "EleccionTrabajo";
	public const string RutaTrabajo = "Trabajo";
	public const string RutaMovimientos = "Movimientos";

	// Spike del bastón (spec 008): la ruta solo se registra en la compilación DEBUG.
	public const string RutaDiagnosticoBaston = "DiagnosticoBaston";

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(RutaImportar, typeof(ImportarPage));
		Routing.RegisterRoute(RutaExportar, typeof(ExportarPage));
		Routing.RegisterRoute(RutaEleccionTrabajo, typeof(EleccionTrabajoPage));
		Routing.RegisterRoute(RutaTrabajo, typeof(TrabajoPage));
		Routing.RegisterRoute(RutaMovimientos, typeof(MovimientosPage));
#if DEBUG
		Routing.RegisterRoute(RutaDiagnosticoBaston, typeof(Aviss.App.Diagnostico.DiagnosticoBastonPage));
#endif
	}
}
