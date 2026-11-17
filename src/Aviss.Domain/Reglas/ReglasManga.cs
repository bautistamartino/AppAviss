using Aviss.Domain.Enums;

namespace Aviss.Domain.Reglas;

/// <summary>Datos para decidir si se puede reabrir una manga (deshacer su cierre); los arma el servicio.</summary>
/// <param name="EsElUltimoCierreVigenteDelRodeo">Ningún otro cierre vigente del rodeo es posterior a este.</param>
/// <param name="HayOtraMangaAbierta">
/// Hay otra manga del rodeo abierta. Como la manga se crea con su primera marca, equivale a "ya se marcó
/// alguna vaca en la manga siguiente".
/// </param>
public sealed record DatosReapertura(
    bool CierreAnulado,
    bool EsElUltimoCierreVigenteDelRodeo,
    TipoCierreManga TipoDeCierre,
    DateTime FechaCierreUtc,
    DateTime AhoraUtc,
    TimeZoneInfo Zona,
    bool HayOtraMangaAbierta);

/// <summary>Reglas puras de las mangas (spec 003, RF-9).</summary>
public static class ReglasManga
{
    /// <summary>
    /// Devuelve por qué NO se puede deshacer el cierre, o null si se puede. Solo se deshace el último cierre vigente
    /// del rodeo, hecho por un veterinario (no el automático), de hoy (día local), y mientras no haya otra manga abierta.
    /// </summary>
    public static MotivoNoReabrir? MotivoParaNoReabrir(DatosReapertura d)
    {
        if (d.CierreAnulado) return MotivoNoReabrir.CierreYaAnulado;
        if (!d.EsElUltimoCierreVigenteDelRodeo) return MotivoNoReabrir.CierreNoEsElUltimo;
        if (d.TipoDeCierre == TipoCierreManga.Automatico) return MotivoNoReabrir.CierreAutomatico;
        if (!DiaLocal.EsDelMismoDia(d.FechaCierreUtc, d.AhoraUtc, d.Zona)) return MotivoNoReabrir.CierreDeOtroDia;
        if (d.HayOtraMangaAbierta) return MotivoNoReabrir.HayOtraMangaAbierta;
        return null;
    }
}
