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

    /// <summary>"Exportar": va a la pantalla del archivo de vuelta para Dairy Comp (spec 007, RF-1).</summary>
    [RelayCommand]
    private Task ExportarAsync() => _navegacion.IrAExportarAsync();

    /// <summary>"Trabajar": va a elegir establecimiento y rodeo (RF-1, RF-2).</summary>
    [RelayCommand]
    private Task TrabajarAsync() => _navegacion.IrAEleccionTrabajoAsync();
}
