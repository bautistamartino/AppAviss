using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Arranque;

/// <summary>
/// Borra los restos que una versión anterior dejó fuera de la base (spec 006, RF-21): el archivo <c>dispositivo.id</c>
/// (el identificador de la tablet) y su temporal, y la preferencia del veterinario recordado.
/// El arranque lo llama SOLO cuando la app queda abierta: así una actualización fallida no borra nada.
/// Nunca lanza: si algo no se puede borrar (archivo bloqueado, preferencia inaccesible) se ignora y se reintenta en el
/// próximo arranque. No toca ninguna otra preferencia.
/// </summary>
public sealed class LimpiezaDeRastros
{
    private readonly RutasAviss _rutas;
    private readonly ILimpiezaPreferencias _preferencias;

    public LimpiezaDeRastros(RutasAviss rutas, ILimpiezaPreferencias preferencias)
    {
        _rutas = rutas;
        _preferencias = preferencias;
    }

    public void Ejecutar()
    {
        BorrarSiExiste(_rutas.ArchivoDispositivoObsoleto);
        BorrarSiExiste(_rutas.ArchivoDispositivoObsoleto + ".tmp");

        try
        {
            _preferencias.EliminarClavesObsoletas();
        }
        catch (Exception)
        {
            // La limpieza es opcional: un fallo en las preferencias no puede impedir que la app abra.
        }
    }

    private static void BorrarSiExiste(string ruta)
    {
        try
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bloqueado o sin permiso: se reintenta la próxima vez que se abra la app.
        }
    }
}

/// <summary>Versión que no hace nada de <see cref="ILimpiezaPreferencias"/> (tests y plataformas sin preferencias). La app Android registra la real.</summary>
public sealed class LimpiezaPreferenciasNula : ILimpiezaPreferencias
{
    public void EliminarClavesObsoletas() { }
}
