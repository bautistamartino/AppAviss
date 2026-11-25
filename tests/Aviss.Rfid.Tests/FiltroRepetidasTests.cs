namespace Aviss.Rfid.Tests;

/// <summary>RF-21: el mismo EID dentro de 3 s se ignora; las ignoradas no renuevan la ventana.</summary>
public class FiltroRepetidasTests
{
    private const string A = "999000000000001";
    private const string B = "999000000000002";
    private static readonly TimeSpan Ventana = TimeSpan.FromSeconds(3);

    private static (FiltroRepetidas Filtro, Action<double> Avanzar) Crear()
    {
        var ahora = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        return (new FiltroRepetidas(Ventana, () => ahora), s => ahora = ahora.AddSeconds(s));
    }

    [Fact]
    public void La_primera_lectura_pasa()
    {
        var (f, _) = Crear();
        Assert.True(f.Aceptar(A));
    }

    [Fact]
    public void El_mismo_EID_a_1_segundo_se_ignora()
    {
        var (f, avanzar) = Crear();
        Assert.True(f.Aceptar(A));
        avanzar(1);
        Assert.False(f.Aceptar(A));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void A_los_3_segundos_o_mas_pasa(double segundos)
    {
        var (f, avanzar) = Crear();
        Assert.True(f.Aceptar(A));
        avanzar(segundos);
        Assert.True(f.Aceptar(A));
    }

    [Fact]
    public void Las_ignoradas_no_renuevan_la_ventana()
    {
        var (f, avanzar) = Crear();
        Assert.True(f.Aceptar(A));    // t=0
        avanzar(2);
        Assert.False(f.Aceptar(A));   // t=2, ignorada: no mueve la ventana
        avanzar(1.5);
        Assert.True(f.Aceptar(A));    // t=3,5: pasaron 3,5 s desde la aceptada
    }

    [Fact]
    public void EID_distintos_seguidos_siempre_pasan()
    {
        var (f, _) = Crear();
        Assert.True(f.Aceptar(A));
        Assert.True(f.Aceptar(B));
    }

    [Fact]
    public void A_B_A_pasa_porque_solo_se_recuerda_el_ultimo()
    {
        var (f, avanzar) = Crear();
        Assert.True(f.Aceptar(A));
        avanzar(0.5);
        Assert.True(f.Aceptar(B));
        avanzar(0.5);
        Assert.True(f.Aceptar(A));
    }

    [Fact]
    public void Reiniciar_olvida_lo_recordado()
    {
        var (f, avanzar) = Crear();
        Assert.True(f.Aceptar(A));
        f.Reiniciar();
        avanzar(0.1);
        Assert.True(f.Aceptar(A));
    }
}
