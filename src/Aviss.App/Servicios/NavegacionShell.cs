using Aviss.App.ViewModels;

namespace Aviss.App.Servicios;

/// <summary>Navegación con el Shell de MAUI. Las rutas se declaran en <see cref="AppShell"/>.</summary>
public sealed class NavegacionShell : INavegacion
{
    public Task IrAInicioAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//" + AppShell.RutaInicio));

    public Task IrAImportarAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.RutaImportar));
}
