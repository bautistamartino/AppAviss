using Aviss.Application.Campo;

namespace Aviss.Application.Tests.Campo;

/// <summary>RF-22: qué aviso corresponde a una vaca ya procesada hoy.</summary>
public class ReglasAvisoTests
{
    [Fact]
    public void Marca_de_hoy_en_la_manga_actual_es_animal_repetido()
    {
        var manga = Guid.NewGuid();
        Assert.Equal(TipoAviso.Repetido, ReglasAviso.Clasificar(manga, manga));
    }

    [Fact]
    public void Marca_de_hoy_en_otra_manga_es_ya_paso()
    {
        Assert.Equal(TipoAviso.YaPasoOtraManga, ReglasAviso.Clasificar(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Marca_de_hoy_cuando_la_manga_actual_esta_por_empezar_es_ya_paso()
    {
        Assert.Equal(TipoAviso.YaPasoOtraManga, ReglasAviso.Clasificar(Guid.NewGuid(), null));
    }

    [Fact]
    public void Tacto_de_hoy_sin_marca_es_ya_paso_sin_manga_registrada()
    {
        Assert.Equal(TipoAviso.YaPasoSinManga, ReglasAviso.Clasificar(null, Guid.NewGuid()));
        Assert.Equal(TipoAviso.YaPasoSinManga, ReglasAviso.Clasificar(null, null));
    }
}
