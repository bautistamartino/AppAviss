using Aviss.Domain.Catalogo;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

/// <summary>Spec 005, RF-23 y RF-28: los días escritos en pantalla (y los que llegan a los servicios).</summary>
public class ValidadorDiasTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("130", 130)]
    [InlineData("400", 400)]
    [InlineData(" 45 ", 45)]
    public void Preniez_acepta_de_1_a_400(string texto, int esperado)
    {
        var r = ValidadorDias.Validar(texto, RangosDias.Preniez);
        Assert.True(r.EsValido);
        Assert.Equal(esperado, r.Valor);
        Assert.Null(r.Mensaje);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("130,5")]
    [InlineData("130.5")]
    [InlineData("401")]
    [InlineData("1e2")]
    [InlineData("1 2")]
    [InlineData("abc")]
    [InlineData("+5")]
    [InlineData("99999999999999999999")]
    public void Preniez_rechaza_con_mensaje(string? texto)
    {
        var r = ValidadorDias.Validar(texto, RangosDias.Preniez);
        Assert.False(r.EsValido);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensaje));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("400", 400)]
    public void Aborto_acepta_0_y_400(string texto, int esperado)
        => Assert.Equal(esperado, ValidadorDias.Validar(texto, RangosDias.Aborto).Valor);

    [Theory]
    [InlineData("401")]
    [InlineData("-1")]
    [InlineData("")]
    public void Aborto_rechaza_fuera_de_rango(string texto)
        => Assert.False(ValidadorDias.Validar(texto, RangosDias.Aborto).EsValido);

    [Fact]
    public void Los_rangos_son_los_de_la_spec()
    {
        Assert.Equal((1, 400), (RangosDias.Preniez.Minimo, RangosDias.Preniez.Maximo));
        Assert.Equal((0, 400), (RangosDias.Aborto.Minimo, RangosDias.Aborto.Maximo));
    }

    [Fact]
    public void Resultados_del_aborto_mapean_a_su_estado_y_etiqueta()
    {
        Assert.Equal(["Vacía OK", "Inseminada", "Seca", "Fresca", "Rechazo"],
            ReglasResultadoAborto.Todos.Select(ReglasResultadoAborto.Etiqueta));
        Assert.Equal(["VACIA", "INSEMIN", "SECA", "FRESCA", null],
            ReglasResultadoAborto.Todos.Select(ReglasResultadoAborto.ClaveEstado));
        Assert.Equal(ResultadoAborto.Seca, ReglasResultadoAborto.DesdeClaveEstado("SECA"));
        Assert.Null(ReglasResultadoAborto.DesdeClaveEstado("PRENADA"));
    }
}
