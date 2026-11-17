using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Pantalla de entrada. Por ahora un solo botón; las próximas specs agregan los suyos.</summary>
public sealed partial class InicioViewModel : ObservableObject
{
    private readonly INavegacion _navegacion;

    public InicioViewModel(INavegacion navegacion) => _navegacion = navegacion;

    [RelayCommand]
    private Task ImportarRodeoAsync() => _navegacion.IrAImportarAsync();
}
