using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasMangaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.CreateCustomTimeZone("ar", TimeSpan.FromHours(-3), "ar", "ar");

    // Se puede reabrir: último cierre vigente, manual, de hoy, sin otra manga abierta.
    private static DatosReapertura Reabrible() => new(
        CierreAnulado: false, EsElUltimoCierreVigenteDelRodeo: true, TipoDeCierre: TipoCierreManga.Manual,
        FechaCierreUtc: Ahora.AddMinutes(-2), AhoraUtc: Ahora, Zona: Zona, HayOtraMangaAbierta: false);

    [Fact]
    public void Un_cierre_reciente_manual_y_sin_otra_manga_se_puede_reabrir()
        => Assert.Null(ReglasManga.MotivoParaNoReabrir(Reabrible()));

    [Fact]
    public void Terminar_el_tacto_tambien_se_puede_reabrir()
        => Assert.Null(ReglasManga.MotivoParaNoReabrir(Reabrible() with { TipoDeCierre = TipoCierreManga.TerminoElTacto }));

    [Fact]
    public void Cierre_ya_anulado()
        => Assert.Equal(MotivoNoReabrir.CierreYaAnulado, ReglasManga.MotivoParaNoReabrir(Reabrible() with { CierreAnulado = true }));

    [Fact]
    public void Cierre_que_no_es_el_ultimo_del_rodeo()
        => Assert.Equal(MotivoNoReabrir.CierreNoEsElUltimo, ReglasManga.MotivoParaNoReabrir(Reabrible() with { EsElUltimoCierreVigenteDelRodeo = false }));

    [Fact]
    public void Cierre_automatico_no_se_deshace()
        => Assert.Equal(MotivoNoReabrir.CierreAutomatico, ReglasManga.MotivoParaNoReabrir(Reabrible() with { TipoDeCierre = TipoCierreManga.Automatico }));

    [Fact]
    public void Cierre_de_otro_dia_local()
    {
        // 02:30 UTC del 16 son las 23:30 del 15 en UTC-3: para la zona el cierre fue ayer, aunque en UTC sea el mismo día.
        var d = Reabrible() with { FechaCierreUtc = new(2026, 11, 16, 2, 30, 0, DateTimeKind.Utc) };
        Assert.Equal(MotivoNoReabrir.CierreDeOtroDia, ReglasManga.MotivoParaNoReabrir(d));
    }

    [Fact]
    public void Cierre_de_hoy_en_la_zona_aunque_ayer_en_utc()
    {
        // Ahora: 01:00 UTC del 17 = 22:00 del 16 local. Cierre: 20:00 UTC del 16 = 17:00 local del 16. Mismo día local.
        var d = Reabrible() with { AhoraUtc = new(2026, 11, 17, 1, 0, 0, DateTimeKind.Utc), FechaCierreUtc = new(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc) };
        Assert.Null(ReglasManga.MotivoParaNoReabrir(d));
    }

    [Fact]
    public void Hay_otra_manga_abierta()
        => Assert.Equal(MotivoNoReabrir.HayOtraMangaAbierta, ReglasManga.MotivoParaNoReabrir(Reabrible() with { HayOtraMangaAbierta = true }));

    [Fact]
    public void Con_varios_motivos_gana_el_primero_del_orden()
    {
        var d = Reabrible() with { CierreAnulado = true, EsElUltimoCierreVigenteDelRodeo = false, HayOtraMangaAbierta = true };
        Assert.Equal(MotivoNoReabrir.CierreYaAnulado, ReglasManga.MotivoParaNoReabrir(d));
        Assert.Equal(MotivoNoReabrir.CierreNoEsElUltimo, ReglasManga.MotivoParaNoReabrir(d with { CierreAnulado = false }));
        Assert.Equal(MotivoNoReabrir.HayOtraMangaAbierta, ReglasManga.MotivoParaNoReabrir(d with { CierreAnulado = false, EsElUltimoCierreVigenteDelRodeo = true }));
    }
}
