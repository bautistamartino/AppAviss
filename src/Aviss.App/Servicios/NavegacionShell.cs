using Aviss.App.ViewModels;

namespace Aviss.App.Servicios;

/// <summary>Navegación con el Shell de MAUI. Las rutas se declaran en <see cref="AppShell"/>.</summary>
public sealed class NavegacionShell : INavegacion
{
    public Task IrAInicioAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//" + AppShell.RutaInicio));

    public Task IrAImportarAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaImportar));

    // Flujo de campo (spec 003). La elección se abre con ruta absoluta (//Inicio/EleccionTrabajo): así, al volver desde
    // "Salir" del trabajo, la pila de páginas se rearma en vez de apilar una elección sobre otra.
    public Task IrAEleccionTrabajoAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//" + AppShell.RutaInicio + "/" + AppShell.RutaEleccionTrabajo));

    public Task IrATrabajoAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaTrabajo));

    public Task IrAMovimientosAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaMovimientos));
}
