using Aviss.Domain.Entidades;

namespace Aviss.Domain.Tests;

public class MarcaPasadaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static MarcaPasada Nueva()
        => MarcaPasada.Registrar(Guid.NewGuid(), Guid.NewGuid(), Ahora, 7, Ahora);

    [Fact]
    public void Registrar_guarda_los_datos_y_nace_vigente()
    {
        var animal = Guid.NewGuid(); var manga = Guid.NewGuid();
        var m = MarcaPasada.Registrar(animal, manga, Ahora, 7, Ahora);
        Assert.Equal(animal, m.AnimalId);
        Assert.Equal(manga, m.MangaId);
        Assert.Equal(7, m.OrdenInsercion);
        Assert.Equal(Ahora, m.FechaHoraUtc);
        Assert.False(m.Anulado);
    }

    [Fact]
    public void Rechaza_ids_vacios_y_fecha_no_utc()
    {
        Assert.Throws<DominioException>(() => MarcaPasada.Registrar(Guid.Empty, Guid.NewGuid(), Ahora, 1, Ahora));
        Assert.Throws<DominioException>(() => MarcaPasada.Registrar(Guid.NewGuid(), Guid.Empty, Ahora, 1, Ahora));
        Assert.Throws<DominioException>(() => MarcaPasada.Registrar(Guid.NewGuid(), Guid.NewGuid(), DateTime.SpecifyKind(Ahora, DateTimeKind.Unspecified), 1, Ahora));
    }

    [Fact]
    public void Se_anula_una_sola_vez()
    {
        var m = Nueva();
        m.MarcarAnulada(Ahora.AddSeconds(5));
        Assert.True(m.Anulado);
        Assert.Equal(Ahora.AddSeconds(5), m.ActualizadoEnUtc);
        Assert.Throws<DominioException>(() => m.MarcarAnulada(Ahora));
    }
}
