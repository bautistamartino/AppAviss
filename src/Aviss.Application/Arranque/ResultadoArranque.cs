namespace Aviss.Application.Puertos;

/// <summary>Cómo terminó el arranque de la app (RF-28).</summary>
public enum TipoResultadoArranque
{
    Correcto,
    /// <summary>La base no existía y se creó completa.</summary>
    BaseCreada,
    /// <summary>Había una estructura vieja: se hizo la copia y se actualizó.</summary>
    BaseActualizada,
    /// <summary>La base es de una versión más nueva que la app: no se abrió ni se tocó.</summary>
    BaseMasNueva,
    /// <summary>La base está dañada: se copió aparte y no se tocó.</summary>
    BaseDanada,
    /// <summary>No hay espacio para la copia previa: no se actualizó.</summary>
    SinEspacioParaCopia,
    /// <summary>Falló la copia previa: no se actualizó.</summary>
    CopiaPreviaFallida,
    /// <summary>La actualización falló y la base quedó como estaba antes.</summary>
    ActualizacionFallidaRestaurada,
    /// <summary>La actualización falló y no se pudo restaurar: la app no abre y se conserva todo.</summary>
    ActualizacionFallidaSinRestaurar,
    /// <summary>La conexión no puede exigir las claves foráneas: la app no abre (RF-16).</summary>
    ClavesForaneasInactivas,
    /// <summary>Falla inesperada al crear la base.</summary>
    ErrorInesperado
}

/// <summary>
/// Resultado del arranque. <see cref="Mensaje"/> está en español y es fijo por tipo: no incluye datos de animales,
/// tambos ni veterinarios, ni rutas, ni textos de excepciones.
/// </summary>
public sealed record ResultadoArranque(TipoResultadoArranque Tipo, string Mensaje)
{
    /// <summary>
    /// Si la app puede trabajar con la base. En todos los demás casos la base quedó intacta (o restaurada) pero con una
    /// estructura que esta versión de la app no entiende, así que la pantalla debe mostrar el mensaje en vez de abrir.
    /// </summary>
    public bool PuedeAbrir => Tipo is TipoResultadoArranque.Correcto
        or TipoResultadoArranque.BaseCreada
        or TipoResultadoArranque.BaseActualizada;
}
