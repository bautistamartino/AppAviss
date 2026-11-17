using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-21: un único orden de guardado compartido por todos los tipos de registro de campo.</summary>
public sealed class OrdenCompartidoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private long Orden(string tabla, Guid id) => long.Parse(_e.Col(tabla, id, "OrdenInsercion")!);

    [Fact]
    public async Task Marca_y_tacto_de_la_misma_vaca_en_el_mismo_instante_gana_el_guardado_despues()
    {
        // Reloj quieto: marca y tacto tienen exactamente la misma fecha y hora.
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        Assert.Equal(_e.Col("MarcaPasada", lectura.MarcaId!.Value, "FechaHoraUtc"), _e.Col("Tacto", tacto, "FechaHoraUtc"));
        Assert.True(Orden("Tacto", tacto) > Orden("MarcaPasada", lectura.MarcaId.Value));
    }

    [Fact]
    public async Task El_ultimo_registro_vigente_es_el_guardado_despues_sin_importar_el_tipo()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        // El tacto se guardó después: es el último; la marca ya no se puede deshacer, el tacto sí.
        Assert.Equal(CodigoError.MarcaNoEsLaUltima, (await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value)).Codigo);
        Assert.True((await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto)).EsCorrecto);
    }

    [Fact]
    public async Task El_orden_es_uno_solo_entre_tablas_y_nunca_se_repite()
    {
        var a = await _e.VacaMarcadaAsync("1");
        var tacto = await _e.TactoAsync(a.AnimalId, _e.EstadoSeca);
        var b = await _e.VacaMarcadaAsync("2");
        var anulacion = await _e.AnularTactoAsync(tacto);

        var ordenes = new[]
        {
            Orden("MarcaPasada", a.Lectura.MarcaId!.Value), Orden("Tacto", tacto),
            Orden("MarcaPasada", b.Lectura.MarcaId!.Value), Orden("Tacto", anulacion)
        };
        Assert.Equal(ordenes.OrderBy(o => o), ordenes);   // crecen en el orden en que se guardaron
        Assert.Equal(ordenes.Length, ordenes.Distinct().Count());
    }

    [Fact]
    public async Task Un_tacto_toma_el_orden_que_sigue_al_mayor_de_las_marcas_y_viceversa()
    {
        var animalMarca = _e.AnimalImportado("1");
        var animalTacto = _e.AnimalImportado("2", _e.EstadoSeca);
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId);
        _e.Base.MarcaPasada(animalMarca, manga, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddDays(-3), orden: 500);

        var tacto = await _e.TactoAsync(animalTacto, _e.EstadoPrenada);
        Assert.Equal(501, Orden("Tacto", tacto));

        _e.Base.Tacto(animalMarca, _e.UsuarioId, _e.EstadoSeca, fechaHora: _e.Reloj.UtcAhora.AddDays(-2), orden: 900);
        _e.AnimalImportado("3");
        var lectura = await _e.LeerAsync("3");
        Assert.Equal(901, Orden("MarcaPasada", lectura.MarcaId!.Value));
    }

    [Fact]
    public async Task Los_tactos_de_anulacion_tambien_toman_el_orden_compartido()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        var anulacion = await _e.AnularTactoAsync(tacto);

        Assert.True(Orden("Tacto", anulacion) > Orden("Tacto", tacto));
        Assert.True(Orden("Tacto", tacto) > Orden("MarcaPasada", lectura.MarcaId!.Value));
    }

    [Fact]
    public async Task Un_tacto_de_ayer_no_impide_marcar_hoy()
    {
        // El tacto de ayer es anterior a la marca de hoy: la vaca no cuenta como procesada hoy y la marca se acepta.
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        _e.Base.Tacto(animal, _e.UsuarioId, _e.EstadoSeca, fechaHora: _e.Reloj.UtcAhora.AddDays(-1));
        var lectura = await _e.LeerAsync("100");
        Assert.Equal(TipoResultadoLectura.Marcada, lectura.Tipo);
    }
}
