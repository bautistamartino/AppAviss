using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Resetear un rodeo (para pruebas): se vuelve a importar el Excel original como rodeo nuevo y se da de baja el viejo, así lo hecho
/// antes no se borra pero el rodeo vuelve a quedar como cuando se subió el archivo.
/// </summary>
public sealed class ResetearRodeoTests : IDisposable
{
    private readonly EscenarioImportacion _e = new(new PerfilFalso("Otro Sistema", PerfilFalso.Vacas(3)));
    public void Dispose() => _e.Dispose();

    private async Task<(Guid Rodeo, Guid Establecimiento)> ImportarAsync()
    {
        var vista = await _e.Importacion.PrepararAsync("tambo.xls", new MemoryStream([1, 2, 3, 4]));
        Assert.True(vista.EsCorrecto, vista.Mensaje);
        var r = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(vista.Valor.Id, null, "Tambo Uno", "Rodeo Mayo", false));
        Assert.True(r.EsCorrecto, r.Mensaje);
        var est = Guid.Parse((string)_e.Base.Escalar("SELECT EstablecimientoId FROM Rodeo WHERE Id = $i", ("$i", r.Valor.RodeoId))!);
        return (r.Valor.RodeoId, est);
    }

    [Fact]
    public async Task Resetear_crea_un_rodeo_nuevo_igual_al_original_y_da_de_baja_el_viejo()
    {
        var (viejo, establecimiento) = await ImportarAsync();
        var animalesDelViejo = _e.Cuenta("Animal", $"UPPER(RodeoId) = UPPER('{viejo}')");

        var r = await _e.Importacion.ResetearRodeoAsync(viejo);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.NotEqual(viejo, r.Valor);
        Assert.Equal(2, _e.Cuenta("Rodeo"));
        Assert.Equal(0L, Convert.ToInt64(_e.Base.Escalar("SELECT Activo FROM Rodeo WHERE Id = $i", ("$i", viejo))));   // el viejo queda guardado pero dado de baja
        Assert.Equal(1L, Convert.ToInt64(_e.Base.Escalar("SELECT Activo FROM Rodeo WHERE Id = $i", ("$i", r.Valor))));
        Assert.Equal("Rodeo Mayo", _e.Base.Escalar("SELECT Nombre FROM Rodeo WHERE Id = $i", ("$i", r.Valor)));
        Assert.Equal(establecimiento, Guid.Parse((string)_e.Base.Escalar("SELECT EstablecimientoId FROM Rodeo WHERE Id = $i", ("$i", r.Valor))!));
        Assert.Equal(animalesDelViejo, _e.Cuenta("Animal", $"UPPER(RodeoId) = UPPER('{r.Valor}')"));
        // El archivo original guardado es el mismo.
        Assert.Equal(_e.Base.Escalar("SELECT ArchivoOrigenHash FROM Rodeo WHERE Id = $i", ("$i", viejo)),
            _e.Base.Escalar("SELECT ArchivoOrigenHash FROM Rodeo WHERE Id = $i", ("$i", r.Valor)));
    }

    [Fact]
    public async Task Un_rodeo_que_no_existe_se_rechaza_y_no_cambia_nada()
    {
        await ImportarAsync();

        var r = await _e.Importacion.ResetearRodeoAsync(Guid.NewGuid());

        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Resetear_dos_veces_deja_un_solo_rodeo_activo()
    {
        var (viejo, _) = await ImportarAsync();
        var primero = await _e.Importacion.ResetearRodeoAsync(viejo);

        var segundo = await _e.Importacion.ResetearRodeoAsync(primero.Valor);

        Assert.True(segundo.EsCorrecto, segundo.Mensaje);
        Assert.Equal(3, _e.Cuenta("Rodeo"));
        Assert.Equal(1, _e.Cuenta("Rodeo", "Activo = 1"));
    }
}
