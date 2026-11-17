using Aviss.App.ViewModels;

namespace Aviss.App.Servicios;

/// <summary>Navegación con el Shell de MAUI. Las rutas se declaran en <see cref="AppShell"/>.</summary>
public sealed class NavegacionShell : INavegacion
{
    public Task IrAInicioAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//" + AppShell.RutaInicio));

    public Task IrAImportarAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaImportar));

    // Flujo de campo (spec 003). Las páginas se registran como rutas en T4 (trabajo) y T5 (elección, movimientos):
    // hasta entonces ningún botón llega acá.
    public Task IrAEleccionTrabajoAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaEleccionTrabajo));

    public Task IrATrabajoAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaTrabajo));

    public Task IrAMovimientosAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaMovimientos));
}
