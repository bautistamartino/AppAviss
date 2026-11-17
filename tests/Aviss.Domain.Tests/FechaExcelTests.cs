using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class FechaExcelTests
{
    [Theory]
    [InlineData(" 5/11/26", 2026, 11, 5)]     // como viene en la muestra: espacio de más y año de 2 cifras
    [InlineData("05/11/2026", 2026, 11, 5)]
    [InlineData("  5/11/26  ", 2026, 11, 5)]
    [InlineData("5 / 11 / 26", 2026, 11, 5)]
    [InlineData("1/1/00", 2000, 1, 1)]
    [InlineData("31/12/99", 2099, 12, 31)]     // 2 cifras siempre es 20aa
    [InlineData("29/2/24", 2024, 2, 29)]       // bisiesto
    [InlineData("31/12/1999", 1999, 12, 31)]   // 4 cifras tal cual
    public void Interpreta_dia_mes_anio(string texto, int anio, int mes, int dia)
    {
        Assert.True(FechaExcel.TryParsear(texto, out var f));
        Assert.Equal(new DateOnly(anio, mes, dia), f);
    }

    [Theory]
    [InlineData("31/2/26")]        // día imposible
    [InlineData("29/2/25")]        // no bisiesto
    [InlineData("1/13/26")]        // mes imposible
    [InlineData("0/5/26")]
    [InlineData("5/0/26")]
    [InlineData("hola")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("5-11-26")]        // separador distinto
    [InlineData("2026/11/05")]     // orden año/mes/día
    [InlineData("5/11")]           // faltan partes
    [InlineData("5/11/26/1")]      // sobran partes
    [InlineData("5/11/2")]         // año de 1 cifra
    [InlineData("5/11/226")]       // año de 3 cifras
    [InlineData("a/11/26")]
    [InlineData("-5/11/26")]
    [InlineData("+5/11/26")]
    [InlineData("005/11/26")]      // día de 3 cifras
    public void Lo_que_no_es_una_fecha_falla_sin_excepcion(string? texto)
    {
        Assert.False(FechaExcel.TryParsear(texto, out var f));
        Assert.Equal(default, f);
    }
}
