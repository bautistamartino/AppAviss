using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasAltaCampoTests
{
    [Fact]
    public void Si_tipea_otro_numero_el_codigo_leido_es_el_identificador()
    {
        var r = ReglasAltaCampo.IdElectronicoDeLectura("1234", "982000123456789");
        Assert.Equal("982000123456789", r.IdElectronico);
        Assert.False(r.OmitidoPorFormato);
    }

    [Fact]
    public void Si_no_difieren_no_hay_identificador()
    {
        var r = ReglasAltaCampo.IdElectronicoDeLectura("1234", "1234");
        Assert.Null(r.IdElectronico);
        Assert.False(r.OmitidoPorFormato);
    }

    [Theory]
    [InlineData("ab12", "AB12")]
    [InlineData(" 1234 ", "1234")]
    public void Igual_sin_distinguir_mayusculas_ni_espacios_de_los_extremos_no_hay_identificador(string tipeada, string leido)
        => Assert.Null(ReglasAltaCampo.IdElectronicoDeLectura(tipeada, leido).IdElectronico);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_codigo_leido_alta_desde_buscar_a_mano_no_hay_identificador(string? leido)
    {
        var r = ReglasAltaCampo.IdElectronicoDeLectura("1234", leido);
        Assert.Null(r.IdElectronico);
        Assert.False(r.OmitidoPorFormato);
    }

    [Fact]
    public void Un_codigo_demasiado_largo_no_sirve_como_identificador_y_se_avisa()
    {
        var r = ReglasAltaCampo.IdElectronicoDeLectura("1234", new string('9', IdElectronico.LargoMaximo + 1));
        Assert.Null(r.IdElectronico);
        Assert.True(r.OmitidoPorFormato);
    }

    [Fact]
    public void Un_codigo_de_32_caracteres_si_sirve()
    {
        var codigo = new string('9', IdElectronico.LargoMaximo);
        Assert.Equal(codigo, ReglasAltaCampo.IdElectronicoDeLectura("1234", codigo).IdElectronico);
    }

    [Fact]
    public void Los_espacios_del_codigo_leido_se_quitan_como_en_el_identificador_electronico()
        => Assert.Equal("982000123", ReglasAltaCampo.IdElectronicoDeLectura("1234", "982 000 123").IdElectronico);
}
