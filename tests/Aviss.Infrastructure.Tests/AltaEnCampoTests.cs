using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-25: alta de una vaca que no figura en el rodeo y su anulación.</summary>
public sealed class AltaEnCampoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> Alta(string caravana, string? idElec = null, string? obs = null, Guid? rodeo = null)
        => _e.Servicio<IAnimalService>().AltaEnCampoAsync(
            new AltaEnCampoCommand(rodeo ?? _e.RodeoId, caravana, idElec, obs));

    private Task<Resultado> AnularAlta(Guid animal)
        => _e.Servicio<IAnimalService>().AnularAltaAsync(new AnularAltaCommand(animal));

    [Fact]
    public async Task Crea_el_animal_con_los_datos_minimos_origen_alta_y_un_historial()
    {
        var r = await Alta("0123");
        Assert.True(r.EsCorrecto, r.Mensaje);
        var id = r.Valor;
        Assert.Equal("0123", _e.Col("Animal", id, "NumeroCaravana"));
        Assert.Equal(T(_e.RodeoId), _e.Col("Animal", id, "RodeoId"));
        Assert.Equal("AltaEnCampo", _e.Col("Animal", id, "Origen"));
        Assert.Null(_e.Col("Animal", id, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", id, "FechaUltimoTactoUtc"));
        Assert.Null(_e.Col("Animal", id, "IdElectronico"));
        Assert.Null(_e.Col("Animal", id, "DatosOriginalesJson"));
        Assert.Equal("0", _e.Col("Animal", id, "Anulado"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", id, "CreadoEnUtc"));

        Assert.Equal(1, _e.Cuenta("Historial"));
        Assert.Equal("Animal", _e.Base.Escalar("SELECT Entidad FROM Historial"));
        Assert.Equal(T(id), _e.Base.Escalar("SELECT EntidadId FROM Historial"));
        Assert.Equal("Alta", _e.Base.Escalar("SELECT Campo FROM Historial"));
        Assert.Equal("Alta", _e.Base.Escalar("SELECT Accion FROM Historial"));
    }

    [Fact]
    public async Task Guarda_el_identificador_electronico_sin_espacios_y_las_observaciones()
    {
        var r = await Alta("1", "982 000 123", "  renga  ");
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("982000123", _e.Col("Animal", r.Valor, "IdElectronico"));
        Assert.Equal("renga", _e.Col("Animal", r.Valor, "Observaciones"));
    }

    [Fact]
    public async Task Rechaza_una_caravana_repetida_aunque_cambie_la_capitalizacion_o_la_enie()
    {
        Assert.True((await Alta("ñ1a")).EsCorrecto);
        var antes = _e.Foto();
        foreach (var repetida in new[] { "ñ1a", "Ñ1A", "Ñ1a" })
        {
            var r = await Alta(repetida);
            Assert.Equal(CodigoError.CaravanaDuplicada, r.Codigo);
        }
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Distingue_ceros_a_la_izquierda_y_acepta_el_mismo_numero_en_otro_rodeo()
    {
        Assert.True((await Alta("123")).EsCorrecto);
        Assert.True((await Alta("0123")).EsCorrecto);
        var otroRodeo = _e.Base.Rodeo(_e.EstablecimientoId);
        Assert.True((await Alta("123", rodeo: otroRodeo)).EsCorrecto);
    }

    [Fact]
    public async Task Rechaza_una_caravana_invalida()
    {
        foreach (var mala in new[] { "", "   ", " 12", "12 ", new string('9', 21) })
        {
            var r = await Alta(mala);
            Assert.Equal(CodigoError.CaravanaInvalida, r.Codigo);
        }
        Assert.True((await Alta(new string('9', 20))).EsCorrecto);
        Assert.Equal(1, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Rechaza_un_identificador_electronico_invalido_o_repetido_en_el_rodeo()
    {
        Assert.Equal(CodigoError.IdElectronicoInvalido, (await Alta("1", new string('9', 33))).Codigo);
        Assert.True((await Alta("2", "ABC123")).EsCorrecto);
        Assert.Equal(CodigoError.IdElectronicoDuplicado, (await Alta("3", "ABC 123")).Codigo);
        // En otro rodeo puede repetirse.
        Assert.True((await Alta("3", "ABC123", rodeo: _e.Base.Rodeo(_e.EstablecimientoId))).EsCorrecto);
    }

    [Fact]
    public async Task Rechaza_rodeo_establecimiento_o_inexistentes()
    {
        Assert.Equal(CodigoError.NoEncontrado, (await Alta("1", rodeo: Guid.NewGuid())).Codigo);

        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");
        Assert.Equal(CodigoError.RodeoDeBaja, (await Alta("1")).Codigo);
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 1");
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0");
        Assert.Equal(CodigoError.EstablecimientoDeBaja, (await Alta("1")).Codigo);
        Assert.Equal(0, _e.Cuenta("Animal"));
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    // ---------- Anular el alta ----------

    [Fact]
    public async Task Anular_libera_la_caravana_marca_el_animal_y_deja_historial()
    {
        var id = (await Alta("77")).Valor;
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(3));
        var r = await AnularAlta(id);
        Assert.True(r.EsCorrecto, r.Mensaje);

        Assert.Equal("1", _e.Col("Animal", id, "Anulado"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", id, "AnuladoEnUtc"));
        Assert.Equal("77", _e.Col("Animal", id, "NumeroCaravana")); // el dato no se toca
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionAlta' AND Campo = 'Anulado' AND ValorAnterior = '0' AND ValorNuevo = '1'"));
        Assert.Equal(0, await _e.Servicio<IRodeoService>().ContarAnimalesAsync(_e.RodeoId));

        // Libera el número: se puede dar de alta de nuevo.
        Assert.True((await Alta("77")).EsCorrecto);
    }

    [Fact]
    public async Task Anular_se_rechaza_si_tiene_tactos_vigentes_pero_se_acepta_si_todos_estan_anulados()
    {
        var id = (await Alta("1")).Valor;
        var t = await _e.TactoAsync(id, _e.EstadoSeca);

        var antes = _e.Foto();
        var r = await AnularAlta(id);
        Assert.Equal(CodigoError.AnimalConTactosVigentes, r.Codigo);
        Assert.Equal(antes, _e.Foto());

        await _e.AnularTactoAsync(t);
        Assert.True((await AnularAlta(id)).EsCorrecto);
    }

    [Fact]
    public async Task Un_animal_importado_no_se_anula()
    {
        var id = _e.AnimalImportado("5");
        var antes = _e.Foto();
        var r = await AnularAlta(id);
        Assert.Equal(CodigoError.AnimalImportadoNoSeAnula, r.Codigo);
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Anular_dos_veces_o_un_inexistente_se_rechaza()
    {
        var id = (await Alta("1")).Valor;
        Assert.True((await AnularAlta(id)).EsCorrecto);
        Assert.Equal(CodigoError.AnimalAnulado, (await AnularAlta(id)).Codigo);
        Assert.Equal(CodigoError.NoEncontrado, (await AnularAlta(Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Un_animal_anulado_no_admite_tactos_ni_ediciones()
    {
        var id = (await Alta("1")).Valor;
        await AnularAlta(id);

        var tacto = await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(
            new RegistrarTactoCommand(id, _e.EstadoSeca, null, null));
        Assert.Equal(CodigoError.AnimalAnulado, tacto.Codigo);

        var edicion = await _e.Servicio<IAnimalService>().EditarAsync(new EditarAnimalCommand(id, "1", null, "x"));
        Assert.Equal(CodigoError.AnimalAnulado, edicion.Codigo);
    }
}
