namespace Aviss.Rfid.Tests;

/// <summary>RF-8 y RF-10: esperas 1, 2, 4, 8, 15 y 15 s; vuelve a 1 s al conectar.</summary>
public class PoliticaReintentosTests
{
    [Fact]
    public void Las_esperas_crecen_y_se_quedan_en_el_tope()
    {
        var p = new PoliticaReintentos(new OpcionesLectorBaston());
        var esperas = Enumerable.Range(0, 7).Select(_ => p.SiguienteEspera().TotalSeconds).ToArray();
        Assert.Equal([1, 2, 4, 8, 15, 15, 15], esperas);
    }

    [Fact]
    public void Reiniciar_vuelve_a_1_segundo()
    {
        var p = new PoliticaReintentos(new OpcionesLectorBaston());
        p.SiguienteEspera();
        p.SiguienteEspera();
        p.SiguienteEspera();
        p.Reiniciar();
        Assert.Equal(TimeSpan.FromSeconds(1), p.SiguienteEspera());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 15)]
    [InlineData(40, 15)]
    public void Por_cantidad_de_ciclos_fallidos(int ciclos, double segundos)
    {
        var p = new PoliticaReintentos(new OpcionesLectorBaston());
        Assert.Equal(TimeSpan.FromSeconds(segundos), p.SiguienteEspera(ciclos));
    }

    [Fact]
    public void Los_valores_provisorios_viven_en_las_opciones()
    {
        var o = new OpcionesLectorBaston();
        Assert.Equal(TimeSpan.FromSeconds(3), o.VentanaRepetidas);
        Assert.Equal(TimeSpan.FromSeconds(8), o.TimeoutIntento);
    }
}
