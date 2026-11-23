using Aviss.Domain.Entidades;

namespace Aviss.Domain.Tests;

public class MangaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Abrir_guarda_los_datos_y_la_apertura_es_la_fecha_de_creacion()
    {
        var rodeo = Guid.NewGuid();
        var m = Manga.Abrir(rodeo, 3, Ahora);
        Assert.NotEqual(Guid.Empty, m.Id);
        Assert.Equal(rodeo, m.RodeoId);
        Assert.Equal(3, m.Numero);
        Assert.Equal(Ahora, m.CreadoEnUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_numero_debe_ser_1_o_mas(int numero)
        => Assert.Throws<DominioException>(() => Manga.Abrir(Guid.NewGuid(), numero, Ahora));

    [Fact]
    public void Rechaza_ids_vacios()
    {
        Assert.Throws<DominioException>(() => Manga.Abrir(Guid.Empty, 1, Ahora));
    }

    [Fact]
    public void Rechaza_una_fecha_que_no_es_utc()
        => Assert.Throws<DominioException>(() => Manga.Abrir(Guid.NewGuid(), 1, DateTime.SpecifyKind(Ahora, DateTimeKind.Local)));
}
