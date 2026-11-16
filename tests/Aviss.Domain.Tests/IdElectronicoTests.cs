using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class IdElectronicoTests
{
    [Fact]
    public void Acepta_32_caracteres()
    {
        var texto = new string('9', 32);
        Assert.True(IdElectronico.TryCrear(texto, out var id, out _));
        Assert.Equal(texto, id!.Valor);
    }

    [Fact]
    public void Rechaza_33_caracteres()
    {
        Assert.False(IdElectronico.TryCrear(new string('9', 33), out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("982 000123456789", "982000123456789")] // los espacios se quitan (RF-4b: "sin espacios")
    [InlineData("  982000123456789 ", "982000123456789")]
    public void Guarda_sin_espacios(string texto, string esperado)
    {
        Assert.True(IdElectronico.TryCrear(texto, out var id, out _));
        Assert.Equal(esperado, id!.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rechaza_vacio(string? texto)
    {
        Assert.False(IdElectronico.TryCrear(texto, out _, out _));
    }

    [Fact]
    public void Crear_lanza_si_es_invalido()
    {
        Assert.Throws<DominioException>(() => IdElectronico.Crear(new string('1', 40)));
    }
}
