namespace Aviss.App.ViewModels;

/// <summary>
/// Navegación entre pantallas. Los ViewModels no pueden usar el Shell de MAUI (se prueban en un proyecto normal),
/// así que piden navegar a través de esta interfaz; la implementación con el Shell vive en Servicios/NavegacionShell.
/// </summary>
public interface INavegacion
{
    Task IrAInicioAsync();
    Task IrAImportarAsync();

    /// <summary>Pantalla "Exportar" (spec 007).</summary>
    Task IrAExportarAsync();

    // Flujo de campo (spec 003). Las rutas existen desde T3; las páginas se registran en T4 y T5.
    Task IrAEleccionTrabajoAsync();
    Task IrATrabajoAsync();
    Task IrAMovimientosAsync();
}
