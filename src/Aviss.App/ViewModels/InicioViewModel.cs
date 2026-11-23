using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Pantalla de entrada: importar un rodeo o ponerse a trabajar (spec 003).</summary>
public sealed partial class InicioViewModel : ObservableObject
{
    private readonly INavegacion _navegacion;

    public InicioViewModel(INavegacion navegacion) => _navegacion = navegacion;

    [RelayCommand]
    private Task ImportarRodeoAsync() => _navegacion.IrAImportarAsync();

    /// <summary>"Trabajar": va a elegir establecimiento y rodeo (RF-1, RF-2).</summary>
    [RelayCommand]
    private Task TrabajarAsync() => _navegacion.IrAEleccionTrabajoAsync();
}
