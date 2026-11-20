using Aviss.Application.Puertos;

namespace Aviss.App.Servicios;

/// <summary>
/// Selector de archivos de Android (Storage Access Framework). El archivo llega como un content:// sin ruta real:
/// por eso se devuelve el FLUJO de lectura y nunca una ruta (RF-1, el original no se toca).
/// </summary>
public sealed class SelectorArchivoMaui : ISelectorArchivo
{
    // Android filtra por tipo MIME, no por extensión. Se aceptan el MIME de .xls, el de .xlsx (para poder mostrar el
    // mensaje claro de "todavía no se importa"), los de .csv y .txt, y octet-stream, que es como algunos exploradores
    // marcan un .xls o un .csv.
    private static readonly FilePickerFileType TiposExcel = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = new[]
        {
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "text/csv",
            "text/comma-separated-values",
            "text/plain",
            "application/octet-stream"
        }
    });

    public async Task<ArchivoElegido?> ElegirAsync(CancellationToken ct = default)
    {
        // PickAsync devuelve nulo si el veterinario cancela. Debe correr en el hilo de la interfaz.
        var resultado = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Elegí el listado de Dairy Comp (.xls o .csv)",
            FileTypes = TiposExcel
        }));
        if (resultado is null) return null;

        var flujo = await resultado.OpenReadAsync();
        return new ArchivoElegido(resultado.FileName, flujo);
    }
}
