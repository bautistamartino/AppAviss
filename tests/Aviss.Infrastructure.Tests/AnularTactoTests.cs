using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-13 y RF-14: anular el último tacto vigente con otro registro.</summary>
public sealed class AnularTactoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);
    private static string F(DateTime d) => BaseDeDatosDePrueba.Fecha(d);

    private Task<Resultado<Guid>> Anular(Guid tacto)
        => _e.Servicio<ITrabajoCampoService>().AnularTactoAsync(new AnularTactoCommand(tacto));

    [Fact]
    public async Task Guarda_el_tacto_de_anulacion_con_los_datos_exactos_y_devuelve_al_animal_a_lo_anterior()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        var fechaT1 = _e.Reloj.UtcAhora;
        await _e.TactoAsync(animal, _e.EstadoPrenada);   // SECA -> PREÑADA
        _e.Reloj.Avanzar(TimeSpan.FromHours(2));
        var t2 = await _e.TactoAsync(animal, _e.EstadoSeca); // PREÑADA -> SECA

        _e.Reloj.Avanzar(TimeSpan.FromHours(1));
        var ahora = _e.Reloj.UtcAhora;
        var anulacion = await _e.AnularTactoAsync(t2);

        // Tacto de anulación (RF-13).
        Assert.Equal("Anulacion", _e.Col("Tacto", anulacion, "Tipo"));
        Assert.Equal(T(t2), _e.Col("Tacto", anulacion, "TactoAnuladoId"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Tacto", anulacion, "EstadoAnteriorId"));   // estado actual del animal
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Tacto", anulacion, "EstadoNuevoId"));   // estado anterior del anulado
        Assert.Null(_e.Col("Tacto", anulacion, "DiasPrenez"));
        Assert.Equal(F(ahora), _e.Col("Tacto", anulacion, "FechaHoraUtc"));
        Assert.Equal("0", _e.Col("Tacto", anulacion, "Anulado"));

        // El animal vuelve al estado anterior del tacto anulado y a la fecha del último vigente anterior (t1).
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(F(fechaT1), _e.Col("Animal", animal, "FechaUltimoTactoUtc"));

        // El original queda marcado.
        Assert.Equal("1", _e.Col("Tacto", t2, "Anulado"));

        // Historial: tacto t1, tacto t2 y la anulación.
        Assert.Equal(3, _e.Cuenta("Historial"));
        Assert.Equal(1, _e.Cuenta("Historial", $"Accion = 'AnulacionTacto' AND EntidadId = '{T(anulacion)}' AND Entidad = 'Tacto' AND Campo = 'Estado' " +
            $"AND ValorAnterior = '{T(_e.EstadoSeca)}' AND ValorNuevo = '{T(_e.EstadoPrenada)}'"));
    }

    [Fact]
    public async Task Si_era_el_unico_tacto_el_animal_vuelve_sin_estado_y_sin_fecha()
    {
        var animal = _e.AnimalImportado("5"); // sin estado
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        var anulacion = await _e.AnularTactoAsync(t);

        Assert.Null(_e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Null(_e.Col("Tacto", anulacion, "EstadoNuevoId")); // anular devuelve a "sin estado"
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Tacto", anulacion, "EstadoAnteriorId"));
    }

    [Fact]
    public async Task Despues_de_anular_se_puede_volver_a_registrar_y_la_anulacion_no_cuenta_como_ultimo()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        var t1 = await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var t2 = await _e.TactoAsync(animal, _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await _e.AnularTactoAsync(t2);

        // El último vigente es t1 (no la anulación): se puede anular también.
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await _e.AnularTactoAsync(t1);
        Assert.Equal("1", _e.Col("Tacto", t1, "Anulado"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", animal, "FechaUltimoTactoUtc"));
    }

    [Fact]
    public async Task Con_la_misma_fecha_se_anula_el_que_se_guardo_despues()
    {
        var animal = _e.AnimalCampo();
        var t1 = await _e.TactoAsync(animal, _e.EstadoSeca);
        var t2 = await _e.TactoAsync(animal, _e.EstadoPrenada); // mismo instante

        Assert.Equal(CodigoError.TactoNoEsElUltimo, (await Anular(t1)).Codigo);
        Assert.True((await Anular(t2)).EsCorrecto);
    }

    // ---------- Rechazos (RF-14) ----------

    [Fact]
    public async Task Rechaza_un_tacto_ya_anulado()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        await _e.AnularTactoAsync(t);
        var antes = _e.Foto();
        var r = await Anular(t);
        Assert.Equal(CodigoError.TactoYaAnulado, r.Codigo);
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Rechaza_anular_un_tacto_de_anulacion()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        var anulacion = await _e.AnularTactoAsync(t);
        var antes = _e.Foto();
        var r = await Anular(anulacion);
        Assert.Equal(CodigoError.TactoDeAnulacion, r.Codigo);
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Rechaza_un_tacto_que_no_es_el_ultimo_vigente()
    {
        var animal = _e.AnimalCampo();
        var t1 = await _e.TactoAsync(animal, _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await _e.TactoAsync(animal, _e.EstadoPrenada);
        var antes = _e.Foto();
        var r = await Anular(t1);
        Assert.Equal(CodigoError.TactoNoEsElUltimo, r.Codigo);
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Rechaza_si_el_animal_esta_anulado()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        _e.Base.Ejecutar("UPDATE Animal SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id", ("$f", F(_e.Reloj.UtcAhora)), ("$id", animal));
        var antes = _e.Foto();
        var r = await Anular(t);
        Assert.Equal(CodigoError.AnimalAnulado, r.Codigo);
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Rechaza_tacto_inexistente()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        Assert.Equal(CodigoError.NoEncontrado, (await Anular(Guid.NewGuid())).Codigo);
        Assert.Equal("0", _e.Col("Tacto", t, "Anulado"));
    }

    [Fact]
    public async Task La_base_rechaza_anular_directamente_un_tacto_de_anulacion()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);
        var anulacion = await _e.AnularTactoAsync(t);
        Assert.ThrowsAny<SqliteException>(() =>
            _e.Base.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $id", ("$id", anulacion)));
    }

    // ---------- Atomicidad ----------

    [Fact]
    public async Task Una_falla_a_mitad_de_la_anulacion_no_deja_ningun_cambio()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        var t = await _e.TactoAsync(animal, _e.EstadoPrenada);
        var antes = _e.Foto();

        _e.Base.Ejecutar("CREATE TRIGGER falla_historial BEFORE INSERT ON Historial BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Anular(t));

        Assert.Equal(antes, _e.Foto());
        Assert.Equal("0", _e.Col("Tacto", t, "Anulado"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(1, _e.Cuenta("Tacto"));

        _e.Base.Ejecutar("DROP TRIGGER falla_historial");
        Assert.True((await Anular(t)).EsCorrecto);
    }
}
