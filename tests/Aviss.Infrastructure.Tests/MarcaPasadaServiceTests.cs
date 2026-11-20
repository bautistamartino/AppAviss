using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-11, RF-12, RF-20 a RF-22, RF-35: leer una caravana la marca sola, en una única transacción.</summary>
public sealed class MarcaPasadaServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    // ---------- Marca automática ----------

    [Fact]
    public async Task Leer_una_vaca_la_marca_sola_y_muestra_su_ficha()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(animal, r.AnimalId);
        Assert.Equal("100", r.Caravana);
        Assert.Equal("100", r.Ficha!.Caravana);
        Assert.Equal("SECA", r.Ficha.EstadoNombre);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(1, r.NumeroManga);

        // La marca guarda quién, cuándo, en qué manga y con qué orden; no toca el estado ni la fecha de último tacto del animal.
        var marca = r.MarcaId!.Value;
        Assert.Equal(T(animal), _e.Col("MarcaPasada", marca, "AnimalId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("MarcaPasada", marca, "FechaHoraUtc"));
        Assert.Equal(T(_e.UsuarioId), _e.Col("MarcaPasada", marca, "UsuarioId"));
        Assert.Equal(T(_e.DispositivoId), _e.Col("MarcaPasada", marca, "DispositivoId"));
        Assert.Equal("0", _e.Col("MarcaPasada", marca, "Anulado"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
    }

    // ---------- CVET: solo entran en tacto las vacas con CVET distinto de 0 ----------

    [Fact]
    public async Task Una_vaca_con_cvet_cero_queda_marcada_y_suma_al_conteo_pero_no_trae_la_ficha()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca, json: """{"ID":"100","CVET":"0"}""");

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.SinTacto, r.Tipo);
        Assert.Equal(animal, r.AnimalId);
        Assert.Equal("100", r.Caravana);
        Assert.Null(r.Ficha);
        Assert.Equal(1, r.ContadorManga);
        Assert.Equal(1, r.NumeroManga);
        // La marca existe (se puede deshacer y suma a la manga) y no se guardó ningún tacto.
        Assert.NotNull(r.MarcaId);
        Assert.Equal(T(animal), _e.Col("MarcaPasada", r.MarcaId!.Value, "AnimalId"));
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
    }

    [Fact]
    public async Task Leer_otra_vez_una_vaca_con_cvet_cero_avisa_que_es_repetida_y_no_suma_de_nuevo()
    {
        _e.AnimalImportado("100", json: """{"CVET":"0"}""");
        await _e.LeerAsync("100");

        var segunda = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Repetido, segunda.Tipo);
        Assert.Equal(1, _e.Cuenta("MarcaPasada"));
    }

    [Theory]
    [InlineData("""{"CVET":"PRE"}""", "PRE")]
    [InlineData("""{"CVET":"ABT"}""", "ABT")]
    [InlineData("""{"CVET":"3"}""", "3")]
    [InlineData("""{"LACT":"2"}""", "")]          // sin la columna: no se bloquea el trabajo
    [InlineData("{esto no es json", "")]
    public async Task Una_vaca_con_cvet_distinto_de_cero_o_sin_dato_trae_la_ficha_con_su_motivo(string json, string cvetEsperado)
    {
        _e.AnimalImportado("100", json: json);

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(cvetEsperado, r.Ficha!.Cvet);
        Assert.True(r.Ficha.EntraEnTacto);
    }

    [Fact]
    public async Task La_ficha_de_una_vaca_con_cvet_cero_dice_que_no_entra_en_tacto()
    {
        var animal = _e.AnimalImportado("100", json: """{"CVET":"0"}""");

        var ficha = (await _e.Campo.ObtenerFichaAsync(animal)).Valor;

        Assert.Equal("0", ficha.Cvet);
        Assert.False(ficha.EntraEnTacto);
    }

    [Fact]
    public async Task La_primera_marca_abre_la_manga_y_guarda_manga_marca_e_historial_juntos()
    {
        Assert.Equal(0, _e.Cuenta("Manga"));
        _e.AnimalImportado("100");

        var r = await _e.LeerAsync("100");

        Assert.Equal(1, _e.Cuenta("Manga"));
        var manga = Guid.Parse(_e.Base.Escalar("SELECT Id FROM Manga")!.ToString()!);
        Assert.Equal("1", _e.Col("Manga", manga, "Numero"));
        Assert.Equal(T(_e.RodeoId), _e.Col("Manga", manga, "RodeoId"));
        Assert.Equal(T(_e.UsuarioId), _e.Col("Manga", manga, "AbiertaPorUsuarioId"));
        Assert.Equal(T(manga), _e.Col("MarcaPasada", r.MarcaId!.Value, "MangaId"));

        Assert.Equal(2, _e.Cuenta("Historial"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MangaAbierta'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MarcaPasada'"));
    }

    [Fact]
    public async Task La_segunda_vaca_entra_en_la_misma_manga_y_suma_al_contador()
    {
        _e.AnimalImportado("100");
        _e.AnimalImportado("101");

        var a = await _e.LeerAsync("100");
        var b = await _e.LeerAsync("101");

        Assert.Equal(1, _e.Cuenta("Manga"));
        Assert.Equal(2, b.ContadorManga);
        Assert.Equal(_e.Col("MarcaPasada", a.MarcaId!.Value, "MangaId"), _e.Col("MarcaPasada", b.MarcaId!.Value, "MangaId"));
        Assert.Equal(3, _e.Cuenta("Historial")); // manga abierta + 2 marcas
    }

    [Fact]
    public async Task Buscar_por_caravana_gana_sobre_el_identificador_electronico()
    {
        // La vaca A tiene caravana "555"; la vaca B tiene como identificador electrónico "555".
        var a = _e.AnimalImportado("555");
        _e.Base.Animal(_e.RodeoId, "777", "Importado", idElectronico: "555");

        var r = await _e.LeerAsync("555");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(a, r.AnimalId);
    }

    [Fact]
    public async Task Si_no_hay_caravana_busca_por_identificador_electronico()
    {
        var animal = _e.Base.Animal(_e.RodeoId, "777", "Importado", idElectronico: "982000123456789");

        var r = await _e.LeerAsync("982000123456789");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(animal, r.AnimalId);
        Assert.Equal("777", r.Caravana);
    }

    [Fact]
    public async Task Un_codigo_demasiado_largo_para_caravana_se_busca_igual_como_identificador()
    {
        var id = "98200012345678901234567890"; // 26 caracteres: no es una caravana válida (máximo 20) pero sí un identificador
        var animal = _e.Base.Animal(_e.RodeoId, "888", "Importado", idElectronico: id);

        var r = await _e.LeerAsync(id);

        Assert.Equal(animal, r.AnimalId);
    }

    [Fact]
    public async Task Los_espacios_de_los_extremos_del_codigo_no_cuentan()
    {
        var animal = _e.AnimalImportado("100");
        var r = await _e.LeerAsync("  100 ");
        Assert.Equal(animal, r.AnimalId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("999")]
    public async Task Una_caravana_que_no_existe_no_guarda_nada_y_ofrece_alta(string codigo)
    {
        _e.AnimalImportado("100");
        var antes = _e.FotoCampo();

        var r = await _e.LeerAsync(codigo);

        Assert.Equal(TipoResultadoLectura.NoEncontrada, r.Tipo);
        Assert.Equal(codigo, r.CodigoLeido);
        Assert.Null(r.AnimalId);
        Assert.Null(r.Ficha);
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Manga"));
    }

    [Fact]
    public async Task Una_vaca_de_otro_rodeo_no_se_encuentra()
    {
        var otroRodeo = _e.Base.Rodeo(_e.EstablecimientoId);
        _e.AnimalImportado("100", rodeoId: otroRodeo);

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.NoEncontrada, r.Tipo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Una_vaca_anulada_no_se_encuentra()
    {
        _e.Base.Animal(_e.RodeoId, "100", "AltaEnCampo", anulado: true);
        var r = await _e.LeerAsync("100");
        Assert.Equal(TipoResultadoLectura.NoEncontrada, r.Tipo);
    }

    [Fact]
    public async Task El_mismo_codigo_en_dos_rodeos_marca_la_vaca_del_rodeo_en_trabajo()
    {
        var otroRodeo = _e.Base.Rodeo(_e.EstablecimientoId);
        _e.AnimalImportado("100", rodeoId: otroRodeo);
        var mia = _e.AnimalImportado("100");

        var r = await _e.LeerAsync("100");

        Assert.Equal(mia, r.AnimalId);
    }

    // ---------- Vaca ya procesada hoy (RF-22) ----------

    [Fact]
    public async Task Releer_en_la_manga_actual_es_animal_repetido_y_no_guarda_ni_suma()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        var antes = _e.FotoCampo();
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Repetido, r.Tipo);
        Assert.Equal(TipoAviso.Repetido, r.Aviso);
        Assert.Equal(animal, r.AnimalId);
        Assert.Equal("100", r.Caravana);
        Assert.Equal(TipoRegistroCampo.Marca, r.UltimoRegistro!.Tipo);
        Assert.Equal(1, r.UltimoRegistro.NumeroManga);
        Assert.Equal("Vet Uno", r.UltimoRegistro.Veterinario);
        Assert.False(r.PuedeCorregir);        // lo último es la marca: no se ofrece "Corregir"
        Assert.Null(r.Ficha);                 // "Ver ficha" la pide aparte
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(1, (await _e.EstadoAsync()).Contador);
    }

    [Fact]
    public async Task Releer_una_vaca_que_paso_en_otra_manga_del_mismo_dia_es_ya_paso_y_no_se_cuenta()
    {
        await _e.VacaMarcadaAsync("100");
        await _e.FinalizarAsync();           // cierra la manga 1: la manga actual queda "por empezar"
        _e.AnimalImportado("101");
        await _e.LeerAsync("101");           // abre la manga 2

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.YaPaso, r.Tipo);
        Assert.Equal(TipoAviso.YaPasoOtraManga, r.Aviso);
        Assert.Equal(1, r.UltimoRegistro!.NumeroManga); // se dice la manga en que pasó
        Assert.Equal(1, (await _e.EstadoAsync()).Contador); // en la manga 2 solo está la 101
    }

    [Fact]
    public async Task Una_vaca_con_tacto_de_hoy_pero_sin_marca_ya_paso_sin_manga_registrada()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);   // datos de la spec 001: tacto sin marca
        var antes = _e.FotoCampo();

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.YaPaso, r.Tipo);
        Assert.Equal(TipoAviso.YaPasoSinManga, r.Aviso);
        Assert.Equal(TipoRegistroCampo.Tacto, r.UltimoRegistro!.Tipo);
        Assert.Equal("PREÑADA", r.UltimoRegistro.EstadoNombre);
        Assert.Null(r.UltimoRegistro.NumeroManga);
        Assert.True(r.PuedeCorregir);                    // lo último no es la marca: se puede corregir
        Assert.Equal(antes, _e.FotoCampo());             // no suma a ningún contador ni crea manga
        Assert.Equal(0, _e.Cuenta("Manga"));
    }

    [Fact]
    public async Task Una_vaca_marcada_ayer_hoy_se_marca_de_nuevo()
    {
        var animal = _e.AnimalImportado("100");
        var mangaAyer = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId, creadaEn: _e.Reloj.UtcAhora.AddDays(-1));
        _e.Base.MarcaPasada(animal, mangaAyer, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddDays(-1));
        _e.Base.CierreManga(mangaAyer, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddDays(-1));

        var r = await _e.LeerAsync("100");

        Assert.Equal(TipoResultadoLectura.Marcada, r.Tipo);
        Assert.Equal(2, r.NumeroManga); // manga nueva: la 1 estaba cerrada
    }

    // ---------- Rechazos ----------

    [Fact]
    public async Task Un_reloj_atrasado_rechaza_la_marca_sin_guardar_nada_y_sugiere_revisar_la_hora()
    {
        var animal = _e.AnimalImportado("100");
        // Un registro de la vaca con fecha dos días en el futuro (no es "hoy"): la hora de la tablet quedó atrasada.
        _e.Base.Tacto(animal, _e.UsuarioId, _e.EstadoSeca, fechaHora: _e.Reloj.UtcAhora.AddDays(2));
        var antes = _e.FotoCampo();

        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", _e.UsuarioId));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoRegistro, r.Codigo);
        Assert.Contains("hora", r.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Manga"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Un_rodeo_de_baja_rechaza_la_marca()
    {
        _e.AnimalImportado("100");
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");

        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", _e.UsuarioId));

        Assert.Equal(CodigoError.RodeoDeBaja, r.Codigo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Un_establecimiento_de_baja_rechaza_la_marca()
    {
        _e.AnimalImportado("100");
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0");

        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", _e.UsuarioId));

        Assert.Equal(CodigoError.EstablecimientoDeBaja, r.Codigo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Un_veterinario_de_baja_o_inexistente_no_puede_marcar()
    {
        _e.AnimalImportado("100");
        var baja = _e.Base.Usuario("De baja", activo: false);

        var deBaja = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", baja));
        var inexistente = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", Guid.NewGuid()));

        Assert.Equal(CodigoError.UsuarioDeBaja, deBaja.Codigo);
        Assert.Equal(CodigoError.NoEncontrado, inexistente.Codigo);
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
    }

    [Fact]
    public async Task Un_rodeo_inexistente_se_rechaza()
    {
        var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(Guid.NewGuid(), "100", _e.UsuarioId));
        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }

    // ---------- Atomicidad (RF-35) ----------

    [Fact]
    public async Task Una_falla_a_mitad_no_deja_ni_marca_ni_manga_ni_historial()
    {
        _e.AnimalImportado("100");
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("MarcaPasada"); // el historial de la marca falla DESPUÉS de insertar manga y marca

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, "100", _e.UsuarioId)));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("Manga"));
        Assert.Equal(0, _e.Cuenta("MarcaPasada"));
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    // ---------- Inmutabilidad en la base (RF-20) ----------

    [Fact]
    public async Task La_marca_guardada_no_se_puede_modificar_ni_borrar_por_fuera_de_la_app()
    {
        _e.AnimalImportado("100");
        var marca = (await _e.LeerAsync("100")).MarcaId!.Value;

        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("UPDATE MarcaPasada SET FechaHoraUtc = '2000-01-01T00:00:00.0000000Z' WHERE Id = $i", ("$i", marca)));
        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("DELETE FROM MarcaPasada WHERE Id = $i", ("$i", marca)));
    }
}
