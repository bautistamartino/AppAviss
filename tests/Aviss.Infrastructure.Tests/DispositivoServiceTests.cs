using Aviss.Infrastructure;
using Aviss.Infrastructure.Sistema;

namespace Aviss.Infrastructure.Tests;

/// <summary>El identificador de la tablet vive en un archivo fuera de la base y no cambia (RF-23).</summary>
public sealed class DispositivoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aviss-disp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private DispositivoService Nuevo() => new(new RutasAviss(_dir));

    [Fact]
    public void La_primera_vez_genera_un_guid_y_lo_guarda_en_dispositivo_id()
    {
        var id = Nuevo().ObtenerId();
        Assert.NotEqual(Guid.Empty, id);
        Assert.True(File.Exists(Path.Combine(_dir, "dispositivo.id")));
    }

    [Fact]
    public void Devuelve_el_mismo_id_entre_instancias()
        => Assert.Equal(Nuevo().ObtenerId(), Nuevo().ObtenerId());

    [Fact]
    public void Devuelve_el_mismo_id_en_la_misma_instancia()
    {
        var servicio = Nuevo();
        Assert.Equal(servicio.ObtenerId(), servicio.ObtenerId());
    }

    [Fact]
    public void No_cambia_al_borrar_y_recrear_la_base()
    {
        var antes = Nuevo().ObtenerId();
        var rutas = new RutasAviss(_dir);
        File.WriteAllText(rutas.BaseDeDatos, "base vieja");
        File.Delete(rutas.BaseDeDatos);
        File.WriteAllText(rutas.BaseDeDatos, "base nueva");
        Assert.Equal(antes, Nuevo().ObtenerId());
    }

    [Fact]
    public void Dos_instalaciones_distintas_tienen_ids_distintos()
    {
        var otro = Path.Combine(_dir, "otra");
        Assert.NotEqual(Nuevo().ObtenerId(), new DispositivoService(new RutasAviss(otro)).ObtenerId());
    }

    [Fact]
    public void Si_el_archivo_esta_danado_genera_uno_nuevo_y_lo_persiste()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "dispositivo.id"), "no es un guid");
        var id = Nuevo().ObtenerId();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, Nuevo().ObtenerId());
    }

    [Fact]
    public void No_deja_archivos_temporales()
    {
        Nuevo().ObtenerId();
        Assert.Equal(new[] { "dispositivo.id" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
    }
}
