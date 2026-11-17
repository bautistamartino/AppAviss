using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-25 y RF-29: alta en campo con su marca en la manga actual, y su anulación junto con la marca.</summary>
public sealed class AltaConMarcaTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<ResultadoAltaDto>> Alta(string caravana, string? codigoLeido = null, Guid? rodeo = null, Guid? usuario = null)
        => _e.Campo.AltaEnCampoConMarcaAsync(new AltaConMarcaCommand(rodeo ?? _e.RodeoId, caravana, codigoLeido, null, usuario ?? _e.UsuarioId));

    private async Task<ResultadoAltaDto> AltaOk(string caravana, string? codigoLeido = null)
    {
        var r = await Alta(caravana, codigoLeido);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    // ---------- Alta feliz ----------

    [Fact]
    public async Task El_alta_queda_sin_estado_ni_tacto_pero_marcada_en_la_manga_actual()
    {
        var r = await AltaOk("500");

        Assert.Equal("500", _e.Col("Animal", r.AnimalId, "NumeroCaravana"));
        Assert.Equal("AltaEnCampo", _e.Col("Animal", r.AnimalId, "Origen"));
        Assert.Null(_e.Col("Animal", r.AnimalId, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", r.AnimalId, "FechaUltimoTactoUtc"));
        Assert.Equal(0, _e.Cuenta("Tacto"));

        // La manga se abre con esta marca (era "por empezar") y el contador queda en 1.
        Assert.Equal(1, _e.Cuenta("Manga"));
        Assert.Equal(1, r.NumeroManga);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(T(r.AnimalId), _e.Col("MarcaPasada", r.MarcaId, "AnimalId"));

        // Alta + marca + manga, cada uno con su historial, y la ficha lista para resolverse.
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'Alta'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MarcaPasada'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MangaAbierta'"));
        Assert.Equal("500", r.Ficha.Caravana);
        Assert.True(r.Ficha.EsAltaEnCampo);
        Assert.True(r.Ficha.PuedeAnularAlta);
        Assert.Null(r.Ficha.EstadoNombre);
    }

    [Fact]
    public async Task Si_la_manga_ya_estaba_abierta_la_vaca_entra_en_esa_manga()
    {
        await _e.VacaMarcadaAsync("100");

        var r = await AltaOk("500");

        Assert.Equal(1, r.NumeroManga);
        Assert.Equal(2, r.ContadorManga);
        Assert.Equal(1, _e.Cuenta("Manga"));
    }

    [Fact]
    public async Task El_alta_cuenta_como_procesada_hoy()
    {
        await AltaOk("500");
        var estado = await _e.EstadoAsync();
        Assert.Equal(1, estado.Avance.TotalVacas);
        Assert.Equal(1, estado.Avance.Procesadas);
        Assert.Equal(0, estado.Avance.Pendientes);
    }

    // ---------- Identificador electrónico (RF-25) ----------

    [Fact]
    public async Task Si_el_codigo_leido_es_igual_a_la_caravana_no_se_guarda_identificador()
    {
        var r = await AltaOk("500", codigoLeido: "500");
        Assert.Null(_e.Col("Animal", r.AnimalId, "IdElectronico"));
        Assert.False(r.IdElectronicoOmitido);
    }

    [Fact]
    public async Task Si_el_veterinario_tipea_otra_caravana_el_codigo_leido_pasa_a_ser_el_identificador()
    {
        var r = await AltaOk("500", codigoLeido: "982000123456789");
        Assert.Equal("500", _e.Col("Animal", r.AnimalId, "NumeroCaravana"));
        Assert.Equal("982000123456789", _e.Col("Animal", r.AnimalId, "IdElectronico"));
    }

    [Fact]
    public async Task Desde_buscar_a_mano_solo_se_carga_la_caravana()
    {
        var r = await AltaOk("500", codigoLeido: null);
        Assert.Null(_e.Col("Animal", r.AnimalId, "IdElectronico"));
    }

    [Fact]
    public async Task Un_codigo_leido_que_no_sirve_como_identificador_da_el_alta_sin_identificador_y_avisa()
    {
        var ilegible = new string('9', 40); // más de 32 caracteres
        var r = await AltaOk("500", codigoLeido: ilegible);
        Assert.Null(_e.Col("Animal", r.AnimalId, "IdElectronico"));
        Assert.True(r.IdElectronicoOmitido);
        Assert.Equal(1, _e.Cuenta("MarcaPasada")); // el alta se hizo igual, con su marca
    }

    [Fact]
    public async Task La_alta_leida_con_un_codigo_se_encuentra_despues_por_ese_codigo()
    {
        var r = await AltaOk("500", codigoLeido: "982000123456789");
        await _e.FinalizarAsync(); // para que no salga "repetido"
        _e.Reloj.Avanzar(TimeSpan.FromDays(1));

        var lectura = await _e.LeerAsync("982000123456789");

        Assert.Equal(r.AnimalId, lectura.AnimalId);
    }

    // ---------- Rechazos: no se crea nada ----------

    [Fact]
    public async Task Una_caravana_repetida_en_el_rodeo_se_rechaza_sin_guardar_nada()
    {
        _e.AnimalImportado("500");
        var antes = _e.FotoCampo();

        var r = await Alta("500");

        Assert.Equal(CodigoError.CaravanaDuplicada, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Manga"));
    }

    [Fact]
    public async Task Una_caravana_repetida_con_otras_mayusculas_tambien_se_rechaza()
    {
        _e.AnimalImportado("ab1");
        Assert.Equal(CodigoError.CaravanaDuplicada, (await Alta("AB1")).Codigo);
    }

    [Fact]
    public async Task Un_identificador_repetido_en_el_rodeo_se_rechaza()
    {
        _e.Base.Animal(_e.RodeoId, "777", "Importado", idElectronico: "982000123456789");
        var antes = _e.FotoCampo();

        var r = await Alta("500", codigoLeido: "982000123456789");

        Assert.Equal(CodigoError.IdElectronicoDuplicado, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901")] // 21 caracteres
    public async Task Una_caravana_invalida_se_rechaza(string caravana)
    {
        var r = await Alta(caravana);
        Assert.Equal(CodigoError.CaravanaInvalida, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Un_rodeo_de_baja_no_admite_altas()
    {
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");
        Assert.Equal(CodigoError.RodeoDeBaja, (await Alta("500")).Codigo);
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Un_veterinario_de_baja_no_puede_dar_altas()
    {
        var baja = _e.Base.Usuario("De baja", activo: false);
        Assert.Equal(CodigoError.UsuarioDeBaja, (await Alta("500", usuario: baja)).Codigo);
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Una_falla_a_mitad_no_deja_alta_ni_marca_ni_manga()
    {
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("MarcaPasada");

        await Assert.ThrowsAnyAsync<Exception>(() => Alta("500"));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Animal"));
        Assert.Equal(0, _e.Cuenta("Manga"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    // ---------- Anular el alta junto con su marca (RF-29) ----------

    [Fact]
    public async Task Anular_un_alta_con_solo_su_marca_anula_las_dos_y_libera_la_caravana()
    {
        var alta = await AltaOk("500");

        var r = await _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(TipoDeshacer.Alta, r.Valor.Tipo);
        Assert.Equal(0, r.Valor.ContadorManga);
        Assert.Equal("1", _e.Col("Animal", alta.AnimalId, "Anulado"));
        Assert.Equal("1", _e.Col("MarcaPasada", alta.MarcaId, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionMarca'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionAlta'"));

        // La caravana quedó libre y el alta ya no cuenta en el rodeo ni en el contador.
        var estado = await _e.EstadoAsync();
        Assert.Equal(0, estado.Avance.TotalVacas);
        Assert.Equal(0, estado.Contador);
        Assert.True((await Alta("500")).EsCorrecto);
    }

    [Fact]
    public async Task Anular_un_alta_con_un_tacto_vigente_se_rechaza_hasta_deshacerlo()
    {
        var alta = await AltaOk("500");
        await _e.TactoAsync(alta.AnimalId, _e.EstadoSeca);
        var antes = _e.FotoCampo();

        var r = await _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId);

        Assert.Equal(CodigoError.AnimalConTactosVigentes, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Anular_un_alta_con_otras_marcas_vigentes_se_rechaza()
    {
        var alta = await AltaOk("500");
        await _e.FinalizarAsync();
        _e.Reloj.Avanzar(TimeSpan.FromDays(1));
        await _e.LeerAsync("500"); // otro día, otra manga, otra marca vigente
        var antes = _e.FotoCampo();

        var r = await _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId);

        Assert.Equal(CodigoError.AnimalConMarcasVigentes, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Un_animal_importado_no_se_anula()
    {
        var animal = _e.AnimalImportado("200");
        var r = await _e.DeshacerAsync(TipoDeshacer.Alta, animal);
        Assert.Equal(CodigoError.AnimalImportadoNoSeAnula, r.Codigo);
    }

    [Fact]
    public async Task Anular_un_alta_ya_anulada_o_inexistente_se_rechaza()
    {
        var alta = await AltaOk("500");
        await _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId);

        Assert.Equal(CodigoError.AnimalAnulado, (await _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId)).Codigo);
        Assert.Equal(CodigoError.NoEncontrado, (await _e.DeshacerAsync(TipoDeshacer.Alta, Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Una_falla_a_mitad_de_anular_el_alta_deja_todo_como_estaba()
    {
        var alta = await AltaOk("500");
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("AnulacionAlta");

        await Assert.ThrowsAnyAsync<Exception>(() => _e.DeshacerAsync(TipoDeshacer.Alta, alta.AnimalId));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal("0", _e.Col("MarcaPasada", alta.MarcaId, "Anulado"));
    }

    // ---------- El servicio viejo respeta las marcas ----------

    [Fact]
    public async Task El_servicio_de_animales_tambien_rechaza_anular_un_alta_con_marcas_vigentes()
    {
        var alta = await AltaOk("500");

        var r = await _e.Servicio<IAnimalService>().AnularAltaAsync(new AnularAltaCommand(alta.AnimalId, _e.UsuarioId));

        Assert.Equal(CodigoError.AnimalConMarcasVigentes, r.Codigo);
        Assert.Equal("0", _e.Col("Animal", alta.AnimalId, "Anulado"));
    }
}
