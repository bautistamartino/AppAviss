using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Excel;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 010, T2 (RF-13 a RF-16): leer un EID se comporta igual que leer el número. Los animales vienen de IMPORTAR la muestra
/// con EID (no de SQL a mano), así se prueba la cadena completa: archivo, base, búsqueda.
/// Animales de la muestra que se usan: 30092 (CVET PRE, EID 999000458013169), 30001 (CVET 0, EID 999000458000000),
/// 30036 (CVET FRES, sin EID: venía con guion).
/// </summary>
public sealed class MarcaPasadaEidTests : IAsyncLifetime
{
    private const string EidPre = "999000458013169";     // 30092
    private const string EidCero = "999000458000000";    // 30001, CVET 0

    private readonly EscenarioImportacion _e = new(new PerfilDairyComp());
    private Guid _rodeoId;
    private Guid _establecimientoId;

    public async Task InitializeAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "muestras"))) dir = dir.Parent;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(dir!.FullName, "tests", "muestras", "dairy-comp-eid-282-vacas.csv"));
        var prep = await _e.Importacion.PrepararAsync("dairy-comp-eid-282-vacas.csv", new MemoryStream(bytes));
        Assert.True(prep.EsCorrecto, prep.Mensaje);
        var r = await _e.Importacion.ConfirmarAsync(new Aviss.Application.Comandos.ConfirmarImportacionCommand(prep.Valor.Id, null, "Tambo", "Rodeo", false));
        Assert.True(r.EsCorrecto, r.Mensaje);
        _rodeoId = r.Valor.RodeoId;
        _establecimientoId = Guid.Parse((string)_e.Base.Escalar("SELECT Id FROM Establecimiento")!);
    }

    public Task DisposeAsync()
    {
        _e.Dispose();
        return Task.CompletedTask;
    }

    private async Task<ResultadoLecturaDto> LeerAsync(string codigo, Guid? rodeoId = null, bool fichaAbierta = false)
    {
        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(rodeoId ?? _rodeoId, codigo, fichaAbierta));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private Guid IdDe(string caravana)
        => Guid.Parse((string)_e.Base.Escalar("SELECT Id FROM Animal WHERE RodeoId = $r AND NumeroCaravana = $c", ("$r", _rodeoId), ("$c", caravana))!);

    // ---------- RF-13: mismo comportamiento que por número ----------

    [Fact]
    public async Task Leer_el_EID_marca_la_pasada_y_devuelve_la_ficha_con_el_EID()
    {
        var r = await LeerAsync(EidPre);

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(IdDe("30092"), r.AnimalId);
        Assert.Equal("30092", r.Caravana);
        Assert.Equal(EidPre, r.Ficha!.IdElectronico);
        Assert.DoesNotContain(r.Ficha.Extras!, c => c.Nombre == "EID");   // RF-17: una sola fila EID en la ficha
        Assert.True(r.Ficha.EntraEnTacto);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Leer_por_EID_y_despues_por_numero_es_la_misma_vaca_repetida()
    {
        var porEid = await LeerAsync(EidPre);
        var porNumero = await LeerAsync("30092");

        Assert.Equal(porEid.AnimalId, porNumero.AnimalId);
        Assert.Equal(TipoResultadoLectura.Repetido, porNumero.Tipo);
    }

    [Fact]
    public async Task Una_vaca_con_CVET_0_leida_por_EID_se_marca_y_da_SinTacto()
    {
        var r = await LeerAsync(EidCero);

        Assert.Equal(TipoResultadoLectura.SinTacto, r.Tipo);
        Assert.Equal(IdDe("30001"), r.AnimalId);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Leer_dos_veces_el_mismo_EID_da_Repetido_y_no_marca_de_nuevo()
    {
        await LeerAsync(EidPre);

        var segunda = await LeerAsync(EidPre);

        Assert.Equal(TipoResultadoLectura.Repetido, segunda.Tipo);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Una_vaca_en_espera_leida_por_EID_devuelve_EnEspera_sin_marcar()
    {
        _e.Base.Espera(IdDe("30092"));

        var r = await LeerAsync(EidPre);

        Assert.Equal(TipoResultadoLectura.EnEspera, r.Tipo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Con_la_ficha_abierta_leer_un_EID_pide_decision_sin_guardar_nada()
    {
        var r = await LeerAsync(EidPre, fichaAbierta: true);

        Assert.Equal(TipoResultadoLectura.RequiereDecision, r.Tipo);
        Assert.Equal(IdDe("30092"), r.AnimalId);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Manga"));
    }

    // ---------- RF-14: con y sin espacios ----------

    [Theory]
    [InlineData("999000458013169")]
    [InlineData("999 000458013169")]
    [InlineData("999 000 458013169")]
    [InlineData("  999000458013169  ")]
    [InlineData("  999 000 458013169 ")]
    public async Task El_EID_se_encuentra_con_o_sin_espacios(string codigo)
    {
        var r = await LeerAsync(codigo);

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(IdDe("30092"), r.AnimalId);
    }

    // ---------- RF-15: el número corto gana ----------

    [Fact]
    public async Task Si_un_codigo_es_numero_de_un_animal_y_EID_de_otro_gana_el_del_numero_corto()
    {
        var rodeo = _e.Base.Rodeo(_establecimientoId);
        var delNumero = _e.Base.Animal(rodeo, "12345678", "Importado");
        _e.Base.Animal(rodeo, "55", "Importado", idElectronico: "12345678");

        var r = await LeerAsync("12345678", rodeo);

        Assert.Equal(delNumero, r.AnimalId);
        Assert.Equal("12345678", r.Caravana);
    }

    // ---------- RF-16: sin EID, otro rodeo, anulado ----------

    [Fact]
    public async Task Una_vaca_sin_EID_se_encuentra_por_su_numero()
    {
        var r = await LeerAsync("30036");   // venía con guion

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Null(r.Ficha!.IdElectronico);
    }

    [Fact]
    public async Task Un_EID_que_no_existe_da_NoEncontrada_sin_guardar_nada()
    {
        var r = await LeerAsync("999000458999999");

        Assert.Equal(TipoResultadoLectura.NoEncontrada, r.Tipo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task El_EID_de_un_animal_de_otro_rodeo_da_NoEncontrada()
    {
        var otro = _e.Base.Rodeo(_establecimientoId);
        _e.Base.Animal(otro, "7", "Importado", idElectronico: "999000111222333");

        Assert.Equal(TipoResultadoLectura.NoEncontrada, (await LeerAsync("999000111222333")).Tipo);
        Assert.Equal(TipoResultadoLectura.Marcada, (await LeerAsync("999000111222333", otro)).Tipo);
    }

    [Fact]
    public async Task El_EID_de_un_animal_anulado_da_NoEncontrada()
    {
        // Solo un alta en campo puede anularse (CK_Animal_Anulado).
        _e.Base.Animal(_rodeoId, "9999", "AltaEnCampo", idElectronico: "999000111222444", anulado: true);

        Assert.Equal(TipoResultadoLectura.NoEncontrada, (await LeerAsync("999000111222444")).Tipo);
    }
}
