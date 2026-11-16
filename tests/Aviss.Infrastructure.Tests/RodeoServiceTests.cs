using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-2: rodeos de un establecimiento.</summary>
public sealed class RodeoServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new(conDatosBase: false);
    public void Dispose() => _e.Dispose();

    private static readonly string Hash = new('a', 64);

    private CrearRodeoCommand Comando(Guid est, string nombre = "Rodeo 1", string? hash = null, byte[]? blob = null,
        string archivo = "tambo.xls", string perfil = "DairyComp")
        => new(est, nombre, _e.Reloj.UtcAhora, archivo, hash ?? Hash, blob ?? new byte[] { 1, 2, 3 }, perfil);

    private Guid NuevoEstablecimiento() => _e.Base.Establecimiento();

    [Fact]
    public async Task Crea_un_rodeo_con_todos_sus_datos()
    {
        var est = NuevoEstablecimiento();
        var r = await _e.Servicio<IRodeoService>().CrearAsync(Comando(est));
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("Abierto", _e.Col("Rodeo", r.Valor, "Estado"));
        Assert.Equal("1", _e.Col("Rodeo", r.Valor, "Activo"));
        Assert.Equal(Hash, _e.Col("Rodeo", r.Valor, "ArchivoOrigenHash"));
        Assert.Equal(3, Convert.ToInt32(_e.Col("Rodeo", r.Valor, "length(ArchivoOrigenBlob)")));
    }

    [Fact]
    public async Task Exige_todos_los_campos_y_no_guarda_nada_si_falta_alguno()
    {
        var est = NuevoEstablecimiento();
        var malos = new[]
        {
            Comando(est, nombre: ""),
            Comando(est, archivo: " "),
            Comando(est, perfil: ""),
            Comando(est, hash: ""),
            Comando(est, hash: new string('a', 63)),
            Comando(est, blob: Array.Empty<byte>()),
        };
        foreach (var c in malos)
        {
            var r = await _e.Servicio<IRodeoService>().CrearAsync(c);
            Assert.False(r.EsCorrecto);
            Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        }
        Assert.Equal(0, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Rechaza_un_establecimiento_inexistente_o_dado_de_baja()
    {
        var r1 = await _e.Servicio<IRodeoService>().CrearAsync(Comando(Guid.NewGuid()));
        Assert.Equal(CodigoError.NoEncontrado, r1.Codigo);

        var est = NuevoEstablecimiento();
        await _e.Servicio<IEstablecimientoService>().DarDeBajaAsync(est);
        var r2 = await _e.Servicio<IRodeoService>().CrearAsync(Comando(est));
        Assert.Equal(CodigoError.EstablecimientoDeBaja, r2.Codigo);
        Assert.Equal(0, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Dos_rodeos_pueden_tener_la_misma_huella()
    {
        var est = NuevoEstablecimiento();
        var a = await _e.Servicio<IRodeoService>().CrearAsync(Comando(est, "Uno"));
        var b = await _e.Servicio<IRodeoService>().CrearAsync(Comando(est, "Dos"));
        Assert.True(a.EsCorrecto && b.EsCorrecto);
        Assert.Equal(2, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task La_cantidad_de_animales_se_cuenta_incluye_altas_y_excluye_anuladas()
    {
        var est = NuevoEstablecimiento();
        var rodeo = (await _e.Servicio<IRodeoService>().CrearAsync(Comando(est))).Valor;
        var vet = _e.Base.Usuario();
        _e.Base.Animal(rodeo, "1", "Importado");
        _e.Base.Animal(rodeo, "2", "Importado");
        _e.Base.Animal(rodeo, "3", "AltaEnCampo");
        _e.Base.Animal(rodeo, "4", "AltaEnCampo", anulado: true);
        Assert.Equal(3, await _e.Servicio<IRodeoService>().ContarAnimalesAsync(rodeo));
        Assert.Equal(0, await _e.Servicio<IRodeoService>().ContarAnimalesAsync(Guid.NewGuid()));
        _ = vet;
    }

    [Fact]
    public async Task Da_de_baja_un_rodeo_sin_borrarlo_ni_tocar_sus_animales()
    {
        var est = NuevoEstablecimiento();
        var rodeo = (await _e.Servicio<IRodeoService>().CrearAsync(Comando(est))).Valor;
        _e.Base.Animal(rodeo, "1");
        var r = await _e.Servicio<IRodeoService>().DarDeBajaAsync(rodeo);
        Assert.True(r.EsCorrecto);
        Assert.Equal("0", _e.Col("Rodeo", rodeo, "Activo"));
        Assert.Equal(1, _e.Cuenta("Animal"));
        Assert.Equal(CodigoError.NoEncontrado, (await _e.Servicio<IRodeoService>().DarDeBajaAsync(Guid.NewGuid())).Codigo);
    }

    [Fact]
    public async Task Un_rodeo_exportado_sigue_aceptando_altas_y_tactos()
    {
        var est = NuevoEstablecimiento();
        var rodeo = (await _e.Servicio<IRodeoService>().CrearAsync(Comando(est))).Valor;
        var vet = _e.Base.Usuario();
        var estado = _e.Base.Estado("SECA", "SECA", "SECA");

        // Lo marca como exportado el dominio (lo hará la función de exportación): se prueba directo con el contexto.
        using (var ctx = _e.Base.NuevoContexto())
        {
            ctx.Rodeos.Single(x => x.Id == rodeo).MarcarExportado(_e.Reloj.UtcAhora);
            ctx.SaveChanges();
        }
        Assert.Equal("Exportado", _e.Col("Rodeo", rodeo, "Estado"));

        var alta = await _e.Servicio<IAnimalService>().AltaEnCampoAsync(new AltaEnCampoCommand(rodeo, "9", null, null, vet));
        Assert.True(alta.EsCorrecto, alta.Mensaje);
        var tacto = await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(new RegistrarTactoCommand(alta.Valor, estado, null, null, vet));
        Assert.True(tacto.EsCorrecto, tacto.Mensaje);
    }
}
