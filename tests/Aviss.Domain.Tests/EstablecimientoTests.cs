using Aviss.Domain;
using Aviss.Domain.Entidades;

namespace Aviss.Domain.Tests;

public class EstablecimientoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Despues = Ahora.AddHours(2);

    [Fact]
    public void Renombrar_cambia_el_nombre_recortado_y_actualiza_la_fecha()
    {
        var e = Establecimiento.Crear("Tambo Norte", "notas", Ahora);

        e.Renombrar("  Tambo Nuevo  ", Despues);

        Assert.Equal("Tambo Nuevo", e.Nombre);
        Assert.Equal(Despues, e.ActualizadoEnUtc);
        Assert.Equal(Ahora, e.CreadoEnUtc);
        Assert.Equal("notas", e.Notas);   // no toca nada más
        Assert.True(e.Activo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Renombrar_rechaza_un_nombre_vacio_y_no_cambia_nada(string? nombre)
    {
        var e = Establecimiento.Crear("Tambo Norte", null, Ahora);

        Assert.Throws<DominioException>(() => e.Renombrar(nombre, Despues));

        Assert.Equal("Tambo Norte", e.Nombre);
        Assert.Equal(Ahora, e.ActualizadoEnUtc);
    }
}
