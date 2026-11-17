namespace Aviss.Application.Campo;

/// <summary>Reglas puras del aviso de una vaca ya procesada hoy (spec 003, RF-22).</summary>
public static class ReglasAviso
{
    /// <summary>
    /// <paramref name="mangaDeLaMarcaVigenteDeHoy"/>: la manga de la marca vigente más reciente de la vaca hoy, o nulo si
    /// no tiene marca hoy (solo un tacto de datos anteriores a la spec 003). <paramref name="mangaActualId"/>: la manga
    /// abierta del rodeo, o nulo si hay una "por empezar".
    /// </summary>
    public static TipoAviso Clasificar(Guid? mangaDeLaMarcaVigenteDeHoy, Guid? mangaActualId)
    {
        if (mangaDeLaMarcaVigenteDeHoy is null) return TipoAviso.YaPasoSinManga;
        return mangaDeLaMarcaVigenteDeHoy == mangaActualId ? TipoAviso.Repetido : TipoAviso.YaPasoOtraManga;
    }
}
