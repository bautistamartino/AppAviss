using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-26 y RF-27: se deshace solo el último registro vigente de la vaca, de cualquier tipo.</summary>
public sealed class DeshacerTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    // ---------- Desmarcar ----------

    [Fact]
    public async Task Desmarcar_anula_la_marca_baja_el_contador_y_la_vaca_vuelve_a_pendiente()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100");
        _e.AnimalImportado("101");
        Assert.Equal(1, (await _e.EstadoAsync()).Avance.Procesadas);

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(animal, r.Valor.AnimalId);
        Assert.Equal(0, r.Valor.ContadorManga);
        Assert.Equal("1", _e.Col("MarcaPasada", lectura.MarcaId.Value, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionMarca'"));

        var estado = await _e.EstadoAsync();
        Assert.Equal(0, estado.Contador);
        Assert.Equal(0, estado.Avance.Procesadas); // vuelve a pendiente
        Assert.Equal(2, estado.Avance.Pendientes);
    }

    [Fact]
    public async Task Una_vaca_desmarcada_se_puede_volver_a_marcar_leyendola()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        var otra = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Marcada, otra.Tipo);
        Assert.NotEqual(lectura.MarcaId, otra.MarcaId);
        Assert.Equal(1, otra.ContadorManga);
        Assert.Equal(1, _e.Cuenta("MarcaPasada", "Anulado = 0"));
    }

    [Fact]
    public async Task Una_marca_ya_anulada_no_se_deshace_dos_veces()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId.Value);

        Assert.Equal(CodigoError.MarcaYaAnulada, r.Codigo);
    }

    [Fact]
    public async Task Deshacer_una_marca_que_no_existe_se_rechaza()
    {
        Assert.Equal(CodigoError.NoEncontrado, (await _e.DeshacerAsync(TipoDeshacer.Marca, Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Cualquiera_puede_deshacer_lo_de_otro()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        Assert.True(r.EsCorrecto, r.Mensaje);
    }

    // ---------- "El último registro de cualquier tipo" ----------

    [Fact]
    public async Task Con_un_tacto_despues_de_la_marca_la_marca_no_se_deshace_hasta_deshacer_el_tacto()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(10));
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        var antes = _e.FotoCampo();

        var rechazo = await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);
        Assert.Equal(CodigoError.MarcaNoEsLaUltima, rechazo.Codigo);
        Assert.Equal(antes, _e.FotoCampo());

        // Primero el tacto (que devuelve a la vaca a su estado anterior), después la marca.
        var deshacerTacto = await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto);
        Assert.True(deshacerTacto.EsCorrecto, deshacerTacto.Mensaje);
        Assert.Equal(animal, deshacerTacto.Valor.AnimalId);
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.True((await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId.Value)).EsCorrecto);
    }

    [Fact]
    public async Task Con_una_marca_mas_nueva_que_el_tacto_el_tacto_no_se_deshace()
    {
        // Se arma por SQL: un tacto viejo (hace 30 horas) y una marca posterior (hace 2 horas) en una manga abierta.
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var tacto = _e.Base.Tacto(animal, _e.EstadoPrenada, fechaHora: _e.Reloj.UtcAhora.AddHours(-30));
        var manga = _e.Base.Manga(_e.RodeoId, 1);
        _e.Base.MarcaPasada(animal, manga, fechaHora: _e.Reloj.UtcAhora.AddHours(-2));

        var r = await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto);

        Assert.Equal(CodigoError.TactoNoEsElUltimo, r.Codigo);
    }

    [Fact]
    public async Task Un_tacto_de_anulacion_no_se_puede_deshacer()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        var anulacion = await _e.AnularTactoAsync(tacto);

        var r = await _e.DeshacerAsync(TipoDeshacer.Tacto, anulacion);

        Assert.Equal(CodigoError.TactoDeAnulacion, r.Codigo);
    }

    [Fact]
    public async Task Un_tacto_ya_anulado_no_se_deshace_otra_vez()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        await _e.AnularTactoAsync(tacto);

        Assert.Equal(CodigoError.TactoYaAnulado, (await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto)).Codigo);
    }

    [Fact]
    public async Task Deshacer_un_tacto_que_no_existe_se_rechaza()
    {
        Assert.Equal(CodigoError.NoEncontrado, (await _e.DeshacerAsync(TipoDeshacer.Tacto, Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Despues_de_deshacer_el_tacto_la_vaca_sigue_marcada_y_la_ficha_lo_muestra_anulado()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(5));
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);

        await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto);

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;
        Assert.Equal("SECA", ficha.EstadoNombre);
        Assert.Contains(ficha.Historial, e => e.Anulado && e.Titulo == "Tacto: PREÑADA");
        Assert.Equal(1, (await _e.EstadoAsync()).Contador);
    }

    // ---------- Atomicidad ----------

    [Fact]
    public async Task Una_falla_a_mitad_de_desmarcar_deja_la_marca_vigente()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("AnulacionMarca");

        await Assert.ThrowsAnyAsync<Exception>(() => _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal("0", _e.Col("MarcaPasada", lectura.MarcaId.Value, "Anulado"));
    }
}
