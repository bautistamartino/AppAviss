using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-26: solo observaciones, identificador electrónico y (en altas en campo) la caravana son editables.</summary>
public sealed class EditarAnimalTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private Task<Resultado> Editar(Guid animal, string caravana, string? idElec, string? obs)
        => _e.Servicio<IAnimalService>().EditarAsync(new EditarAnimalCommand(animal, caravana, idElec, obs));

    [Fact]
    public async Task Edita_observaciones_e_identificador_electronico_con_un_historial_por_campo()
    {
        var id = _e.AnimalCampo("10");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        var r = await Editar(id, "10", "9 8 2", "coja");
        Assert.True(r.EsCorrecto, r.Mensaje);

        Assert.Equal("982", _e.Col("Animal", id, "IdElectronico"));
        Assert.Equal("coja", _e.Col("Animal", id, "Observaciones"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", id, "ActualizadoEnUtc"));

        Assert.Equal(2, _e.Cuenta("Historial"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'Edicion' AND Campo = 'IdElectronico' AND ValorAnterior IS NULL AND ValorNuevo = '982'"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'Edicion' AND Campo = 'Observaciones' AND ValorAnterior IS NULL AND ValorNuevo = 'coja'"));
        Assert.Equal(2, _e.Cuenta("Historial", $"EntidadId = '{EscenarioConServicios.Texto(id)}' AND Entidad = 'Animal'"));
    }

    [Fact]
    public async Task Sin_cambios_reales_no_genera_historial_ni_toca_la_fecha()
    {
        var id = _e.AnimalCampo("10");
        var antes = _e.Foto();
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        Assert.True((await Editar(id, "10", null, "  ")).EsCorrecto); // observaciones vacías = nulo = igual
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Cambia_la_caravana_de_un_alta_en_campo_y_libera_la_anterior()
    {
        var id = _e.AnimalCampo("10");
        var r = await Editar(id, "11", null, null);
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("11", _e.Col("Animal", id, "NumeroCaravana"));
        Assert.Equal(1, _e.Cuenta("Historial", "Campo = 'NumeroCaravana' AND ValorAnterior = '10' AND ValorNuevo = '11'"));

        // Otro animal puede ocupar ahora la "10".
        Assert.True((await _e.Servicio<IAnimalService>().AltaEnCampoAsync(new AltaEnCampoCommand(_e.RodeoId, "10", null, null))).EsCorrecto);
    }

    [Fact]
    public async Task Cambiar_solo_las_mayusculas_de_la_propia_caravana_se_acepta()
    {
        var id = _e.AnimalCampo("ab1");
        Assert.True((await Editar(id, "AB1", null, null)).EsCorrecto);
        Assert.Equal("AB1", _e.Col("Animal", id, "NumeroCaravana"));
    }

    [Fact]
    public async Task La_caravana_de_un_importado_no_se_cambia_pero_si_sus_otros_campos()
    {
        var id = _e.AnimalImportado("200");
        var antes = _e.Foto();
        var rechazo = await Editar(id, "201", null, "x");
        Assert.Equal(CodigoError.DatosInvalidos, rechazo.Codigo);
        Assert.Equal(antes, _e.Foto()); // tampoco se aplicó la observación

        var ok = await Editar(id, "200", "ID9", "nota");
        Assert.True(ok.EsCorrecto, ok.Mensaje);
        Assert.Equal("nota", _e.Col("Animal", id, "Observaciones"));
        Assert.Equal("{}", _e.Col("Animal", id, "DatosOriginalesJson")); // las columnas originales no cambian
        Assert.Equal("Importado", _e.Col("Animal", id, "Origen"));
    }

    [Fact]
    public async Task Rechaza_caravana_o_identificador_repetidos_y_formatos_invalidos()
    {
        var a = _e.AnimalCampo("1");
        _e.Base.Animal(_e.RodeoId, "2", "AltaEnCampo", idElectronico: "XYZ");
        Assert.Equal(CodigoError.CaravanaDuplicada, (await Editar(a, "2", null, null)).Codigo);
        Assert.Equal(CodigoError.IdElectronicoDuplicado, (await Editar(a, "1", "xyz".ToUpper(), null)).Codigo);
        Assert.Equal(CodigoError.CaravanaInvalida, (await Editar(a, " 1", null, null)).Codigo);
        Assert.Equal(CodigoError.IdElectronicoInvalido, (await Editar(a, "1", new string('1', 33), null)).Codigo);
        Assert.Equal(0, _e.Cuenta("Historial"));
    }

    [Fact]
    public async Task Rechaza_animal_inexistente()
    {
        var a = _e.AnimalCampo("1");
        Assert.Equal(CodigoError.NoEncontrado, (await Editar(Guid.NewGuid(), "1", null, null)).Codigo);
        Assert.Null(_e.Col("Animal", a, "Observaciones"));
    }
}
