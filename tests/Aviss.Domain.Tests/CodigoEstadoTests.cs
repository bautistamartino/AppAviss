using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class CodigoEstadoTests
{
    [Theory]
    [InlineData("Aborto", "ABORTO")]
    [InlineData("  aborto  ", "ABORTO")]
    [InlineData("Sin servicio", "SIN_SERVICIO")]
    [InlineData("Preñada 2", "PRENADA_2")]
    [InlineData("Revisión   de  pata", "REVISION_DE_PATA")]
    [InlineData("Vaca (seca)!", "VACA_SECA")]
    public void Arma_un_codigo_con_mayusculas_sin_acentos_y_con_guion_bajo(string nombre, string esperado)
        => Assert.Equal(esperado, CodigoEstado.DesdeNombre(nombre));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("¿?!")]
    public void Sin_caracteres_utiles_devuelve_vacio(string? nombre)
        => Assert.Equal("", CodigoEstado.DesdeNombre(nombre));

    [Fact]
    public void El_codigo_armado_siempre_lo_acepta_el_estado()
    {
        var codigo = CodigoEstado.DesdeNombre("Ñandú raro 9");

        var estado = Aviss.Domain.Entidades.EstadoReproductivo.Crear(codigo, "Ñandú raro 9", "ÑANDU", DateTime.UtcNow);

        Assert.Equal("NANDU_RARO_9", estado.Codigo);
    }
}
