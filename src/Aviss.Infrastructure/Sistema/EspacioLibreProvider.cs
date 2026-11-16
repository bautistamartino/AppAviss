namespace Aviss.Infrastructure.Sistema;

/// <summary>Cuánto espacio libre hay en el disco que contiene un directorio. Detrás de una interfaz para poder simularlo en los tests.</summary>
public interface IEspacioLibreProvider
{
    long BytesLibres(string directorio);
}

public sealed class EspacioLibreProvider : IEspacioLibreProvider
{
    public long BytesLibres(string directorio)
    {
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(directorio));
            return new DriveInfo(raiz!).AvailableFreeSpace;
        }
        catch (Exception)
        {
            // DriveInfo puede comportarse distinto en Android (riesgo anotado en el plan). Si no se puede medir se sigue
            // adelante: si de verdad no hay lugar, la copia falla sola y la actualización no se hace (la base no se toca).
            return long.MaxValue;
        }
    }
}
