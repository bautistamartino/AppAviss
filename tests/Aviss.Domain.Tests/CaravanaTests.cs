using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class CaravanaTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("12345678901234567890")] // 20 caracteres: el máximo permitido
    [InlineData("A-12 B")]               // espacios internos permitidos
    public void Acepta_caravanas_dentro_del_limite(string texto)
    {
        Assert.True(Caravana.TryCrear(texto, out var caravana, out var error));
        Assert.Null(error);
        Assert.Equal(texto, caravana!.Valor); // se guarda tal cual
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")] // 21 caracteres
    [InlineData(" 123")]
    [InlineData("123 ")]
    public void Rechaza_caravanas_invalidas(string? texto)
    {
        Assert.False(Caravana.TryCrear(texto, out var caravana, out var error));
        Assert.Null(caravana);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Crear_lanza_si_es_invalida()
    {
        Assert.Throws<DominioException>(() => Caravana.Crear("123456789012345678901"));
    }

    [Fact]
    public void Cero_a_la_izquierda_distingue_caravanas()
    {
        var a = Caravana.Crear("0123");
        var b = Caravana.Crear("123");
        Assert.NotEqual(a.Valor, b.Valor);
        Assert.NotEqual(a.Clave, b.Clave);
        Assert.Equal("0123", a.Valor);
    }

    [Theory]
    [InlineData("abc", "ABC")]
    [InlineData("ñandú", "ÑANDÚ")]
    [InlineData("áéíóúü", "ÁÉÍÓÚÜ")]
    [InlineData("AbC1", "ABC1")]
    [InlineData("ç", "ç")] // fuera de las 7 letras de la regla: queda igual (como en SQLite)
    public void Clave_pasa_a_mayusculas_incluida_la_enie_y_las_tildes(string texto, string esperada)
    {
        Assert.Equal(esperada, Caravana.CalcularClave(texto));
    }

    [Fact]
    public void Enie_minuscula_y_mayuscula_dan_la_misma_clave()
    {
        Assert.Equal(Caravana.Crear("Ñ1").Clave, Caravana.Crear("ñ1").Clave);
    }

    [Fact]
    public void Caravanas_iguales_salvo_mayusculas_comparten_clave()
    {
        Assert.Equal(Caravana.Crear("ab12").Clave, Caravana.Crear("AB12").Clave);
    }
}
