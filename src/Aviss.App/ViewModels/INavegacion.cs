namespace Aviss.App.ViewModels;

/// <summary>
/// Navegación entre pantallas. Los ViewModels no pueden usar el Shell de MAUI (se prueban en un proyecto normal),
/// así que piden navegar a través de esta interfaz; la implementación con el Shell vive en Servicios/NavegacionShell.
/// </summary>
public interface INavegacion
{
    Task IrAInicioAsync();
    Task IrAImportarAsync();
}
