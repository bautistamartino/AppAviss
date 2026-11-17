using Android.OS;
using Aviss.Infrastructure.Sistema;

namespace Aviss.App;

/// <summary>
/// Espacio libre en Android con StatFs sobre el directorio pedido. DriveInfo no sirve acá: la raíz "/" es la partición
/// del sistema (casi llena) y no el almacenamiento de datos donde vive la base.
/// </summary>
public sealed class EspacioLibreAndroid : IEspacioLibreProvider
{
    public long BytesLibres(string directorio)
    {
        try
        {
            // StatFs necesita un directorio que exista; si todavía no se creó, se mide el primer ancestro existente
            // (está en la misma partición).
            var ruta = Path.GetFullPath(directorio);
            while (!Directory.Exists(ruta))
            {
                var padre = Path.GetDirectoryName(ruta);
                if (string.IsNullOrEmpty(padre)) return long.MaxValue;
                ruta = padre;
            }

            using var stat = new StatFs(ruta);
            return stat.AvailableBytes;
        }
        catch (Exception)
        {
            // Mismo criterio que el de Infrastructure: si no se puede medir se sigue; si de verdad no hay lugar, la copia falla sola.
            return long.MaxValue;
        }
    }
}
