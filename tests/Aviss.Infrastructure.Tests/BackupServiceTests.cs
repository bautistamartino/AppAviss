using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-21: copia verificada, rotación de 10 y protección de la previa a actualización más reciente.</summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _base = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public BackupServiceTests() => _rutas = new RutasAviss(_base.Directorio);

    public void Dispose() => _base.Dispose();

    private BackupService Servicio() => new(_rutas, _base.Reloj, _espacio);

    private string[] Copias() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db").Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();

    private async Task CrearVariasAsync(int cantidad, MotivoBackup motivo = MotivoBackup.APedido)
    {
        for (var i = 0; i < cantidad; i++)
        {
            var r = await Servicio().CrearAsync(motivo);
            Assert.True(r.EsCorrecto, r.Mensaje);
            _base.Reloj.Avanzar(TimeSpan.FromMinutes(1)); // cada copia con su fecha, así el orden es determinista
        }
    }

    [Fact]
    public async Task El_nombre_lleva_la_fecha_utc_y_el_motivo()
    {
        await Servicio().CrearAsync(MotivoBackup.APedido);
        _base.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await Servicio().CrearAsync(MotivoBackup.PreviaActualizacion);

        Assert.Equal(new[]
        {
            "aviss-20261116T120000000Z-a-pedido.db",
            "aviss-20261116T120100000Z-previa-actualizacion.db"
        }, Copias());
    }

    [Fact]
    public async Task La_copia_se_puede_abrir_tiene_los_datos_y_pasa_la_verificacion()
    {
        _base.Establecimiento("Tambo Uno");

        var r = await Servicio().CrearAsync(MotivoBackup.APedido);

        Assert.True(r.EsCorrecto);
        var copia = Path.Combine(_rutas.DirectorioBackups, Copias().Single());
        using var cx = new SqliteConnection($"Data Source={copia};Mode=ReadOnly;Pooling=False");
        cx.Open();
        using var cmd = cx.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Establecimiento";
        Assert.Equal(1L, cmd.ExecuteScalar());
        cmd.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", cmd.ExecuteScalar());
        // No queda ningún archivo temporal.
        Assert.Empty(Directory.GetFiles(_rutas.DirectorioBackups, "*.tmp*"));
    }

    [Fact]
    public async Task Sin_espacio_no_copia_nada()
    {
        _espacio.Bytes = 0;

        var r = await Servicio().CrearAsync(MotivoBackup.PreviaActualizacion);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.SinEspacioParaCopia, r.Codigo);
        Assert.Empty(Copias());
    }

    [Fact]
    public async Task La_copia_numero_11_borra_la_mas_antigua()
    {
        await CrearVariasAsync(11);

        var copias = Copias();
        Assert.Equal(10, copias.Length);
        Assert.DoesNotContain("aviss-20261116T120000000Z-a-pedido.db", copias); // la primera
        Assert.Contains("aviss-20261116T121000000Z-a-pedido.db", copias);       // la 11.ª
    }

    [Fact]
    public async Task La_previa_a_actualizacion_mas_reciente_nunca_se_borra_aunque_quede_fuera_de_las_10()
    {
        await CrearVariasAsync(1, MotivoBackup.PreviaActualizacion); // la más antigua de todas
        await CrearVariasAsync(10);                                  // 11 copias: la previa quedaría fuera

        var copias = Copias();
        Assert.Equal(11, copias.Length); // 10 más nuevas + la previa protegida
        Assert.Contains("aviss-20261116T120000000Z-previa-actualizacion.db", copias);

        await CrearVariasAsync(1);       // 12 copias creadas: se sigue protegiendo
        copias = Copias();
        Assert.Equal(11, copias.Length);
        Assert.Contains("aviss-20261116T120000000Z-previa-actualizacion.db", copias);
        Assert.DoesNotContain("aviss-20261116T120100000Z-a-pedido.db", copias); // la a-pedido más vieja sí se fue
    }

    [Fact]
    public async Task Una_previa_vieja_se_borra_cuando_hay_otra_previa_mas_reciente()
    {
        await CrearVariasAsync(1, MotivoBackup.PreviaActualizacion); // minuto 0
        await CrearVariasAsync(5);                                   // minutos 1 a 5
        await CrearVariasAsync(1, MotivoBackup.PreviaActualizacion); // minuto 6
        await CrearVariasAsync(5);                                   // minutos 7 a 11: ahora son 12

        var copias = Copias();
        Assert.DoesNotContain("aviss-20261116T120000000Z-previa-actualizacion.db", copias); // la vieja, fuera
        Assert.Contains("aviss-20261116T120600000Z-previa-actualizacion.db", copias);       // la reciente, adentro
        Assert.Equal(10, copias.Length);
    }

    [Fact]
    public async Task Si_la_copia_nueva_falla_no_se_borra_ninguna_de_las_anteriores()
    {
        await CrearVariasAsync(10);
        var antes = Copias();

        _espacio.Bytes = 0; // la nueva no se puede hacer
        var r = await Servicio().CrearAsync(MotivoBackup.APedido);

        Assert.False(r.EsCorrecto);
        Assert.Equal(antes, Copias());
    }

    [Fact]
    public async Task Si_la_base_de_origen_esta_danada_la_copia_falla_sin_dejar_restos_ni_rotar()
    {
        await CrearVariasAsync(10);
        var antes = Copias();
        SqliteConnection.ClearAllPools();
        File.WriteAllText(_rutas.BaseDeDatos, "esto no es una base sqlite");
        foreach (var extra in new[] { "-wal", "-shm" })
            if (File.Exists(_rutas.BaseDeDatos + extra)) File.Delete(_rutas.BaseDeDatos + extra);

        var r = await Servicio().CrearAsync(MotivoBackup.APedido);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.CopiaFallida, r.Codigo);
        Assert.Equal(antes, Copias());
        Assert.Empty(Directory.GetFiles(_rutas.DirectorioBackups, "*.tmp*"));
    }

    [Fact]
    public async Task Un_tmp_huerfano_se_ignora_no_cuenta_para_la_rotacion_y_se_limpia()
    {
        Directory.CreateDirectory(_rutas.DirectorioBackups);
        var huerfano = Path.Combine(_rutas.DirectorioBackups, "aviss-20250101T000000000Z-a-pedido.db.tmp");
        File.WriteAllText(huerfano, "copia a medias");

        await CrearVariasAsync(10);

        Assert.False(File.Exists(huerfano));
        Assert.Equal(10, Copias().Length);
    }

    [Fact]
    public async Task Sin_base_no_hay_nada_que_copiar()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_rutas.BaseDeDatos);
        foreach (var extra in new[] { "-wal", "-shm" })
            if (File.Exists(_rutas.BaseDeDatos + extra)) File.Delete(_rutas.BaseDeDatos + extra);

        var r = await Servicio().CrearAsync(MotivoBackup.APedido);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }
}
