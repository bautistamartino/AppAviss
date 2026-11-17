using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Tests;

public class CierreMangaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static CierreManga Cerrar(TipoCierreManga tipo = TipoCierreManga.Manual, int aviss = 40, int? confirmada = 42)
        => CierreManga.Cerrar(Guid.NewGuid(), tipo, Ahora, aviss, confirmada, Guid.NewGuid(), Guid.NewGuid(), Ahora);

    [Fact]
    public void Calcula_la_diferencia_como_confirmada_menos_aviss()
    {
        var c = Cerrar(aviss: 40, confirmada: 42);
        Assert.Equal(40, c.CantidadAviss);
        Assert.Equal(42, c.CantidadConfirmada);
        Assert.Equal(2, c.Diferencia);
        Assert.False(c.Anulado);
        Assert.Equal(Ahora, c.ActualizadoEnUtc);
    }

    [Fact]
    public void Coincide_da_diferencia_cero_y_menos_da_negativa()
    {
        Assert.Equal(0, Cerrar(aviss: 40, confirmada: 40).Diferencia);
        Assert.Equal(-3, Cerrar(aviss: 40, confirmada: 37).Diferencia);
    }

    [Fact]
    public void El_cierre_automatico_no_tiene_cantidad_confirmada_ni_diferencia()
    {
        var c = Cerrar(TipoCierreManga.Automatico, 12, null);
        Assert.Null(c.CantidadConfirmada);
        Assert.Null(c.Diferencia);
    }

    [Fact]
    public void El_cierre_automatico_rechaza_una_cantidad_confirmada()
        => Assert.Throws<DominioException>(() => Cerrar(TipoCierreManga.Automatico, 12, 12));

    [Fact]
    public void Un_cierre_manual_o_de_fin_de_tacto_puede_no_tener_confirmada()
    {
        Assert.Null(Cerrar(TipoCierreManga.Manual, 5, null).Diferencia);
        Assert.Null(Cerrar(TipoCierreManga.TerminoElTacto, 5, null).Diferencia);
    }

    [Fact]
    public void Rechaza_cantidades_negativas_y_tipos_inventados()
    {
        Assert.Throws<DominioException>(() => Cerrar(aviss: -1));
        Assert.Throws<DominioException>(() => Cerrar(confirmada: -1));
        Assert.Throws<DominioException>(() => Cerrar((TipoCierreManga)99));
    }

    [Fact]
    public void Rechaza_ids_vacios_y_fecha_no_utc()
    {
        Assert.Throws<DominioException>(() => CierreManga.Cerrar(Guid.Empty, TipoCierreManga.Manual, Ahora, 1, 1, Guid.NewGuid(), Guid.NewGuid(), Ahora));
        Assert.Throws<DominioException>(() => CierreManga.Cerrar(Guid.NewGuid(), TipoCierreManga.Manual, DateTime.SpecifyKind(Ahora, DateTimeKind.Local), 1, 1, Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Se_anula_una_sola_vez_y_actualiza_la_fecha()
    {
        var c = Cerrar();
        var luego = Ahora.AddMinutes(3);
        c.MarcarAnulado(luego);
        Assert.True(c.Anulado);
        Assert.Equal(luego, c.ActualizadoEnUtc);
        Assert.Throws<DominioException>(() => c.MarcarAnulado(luego));
    }
}
