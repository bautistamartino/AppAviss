using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasCvetTests
{
    [Theory]
    [InlineData("0")]
    [InlineData(" 0 ")]
    [InlineData("00")]
    [InlineData("0.0")]
    [InlineData("0,0")]
    public void Un_cvet_en_cero_no_entra_en_tacto(string cvet) => Assert.False(ReglasCvet.EntraEnTacto(cvet));

    [Theory]
    [InlineData("PRE")]
    [InlineData("FRES")]
    [InlineData("DLSC")]
    [InlineData("ABT")]
    [InlineData("SEVC")]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("0X")]
    [InlineData("-1")]
    public void Un_cvet_distinto_de_cero_entra_en_tacto(string cvet) => Assert.True(ReglasCvet.EntraEnTacto(cvet));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_dato_de_cvet_no_se_bloquea_el_trabajo(string? cvet) => Assert.True(ReglasCvet.EntraEnTacto(cvet));
}
