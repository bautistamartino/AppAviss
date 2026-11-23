using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006, RF-21: al abrir la app se borran los restos de la versión anterior (el archivo dispositivo.id y la preferencia del
/// veterinario recordado), pero solo si la app queda abierta, y nunca se falla por no poder borrarlos. La baja real de la clave
/// en SharedPreferences (PreferenciasMaui) no se puede probar acá: se mira en el emulador.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class LimpiezaDeRastrosTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aviss-limpieza-" + Guid.NewGuid().ToString("N"));
    private readonly FakeReloj _reloj = new();
    private readonly RutasAviss _rutas;

    public LimpiezaDeRastrosTests()
    {
        Directory.CreateDirectory(_dir);
        _rutas = new RutasAviss(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Doble de las preferencias: cuenta los llamados y puede fallar.</summary>
    private sealed class PreferenciasFalsas : ILimpiezaPreferencias
    {
        public int Llamadas { get; private set; }
        public bool Falla { get; set; }

        public void EliminarClavesObsoletas()
        {
            Llamadas++;
            if (Falla) throw new InvalidOperationException("preferencias no disponibles");
        }
    }

    private readonly PreferenciasFalsas _prefs = new();

    private LimpiezaDeRastros Limpieza() => new(_rutas, _prefs);

    private ArranqueService Arranque(IMigrador? migrador = null)
        => new(_rutas, migrador ?? new MigradorEf(), new BackupService(_rutas, _reloj, new FakeEspacioLibre()),
            new SembradorInicial(_reloj), new ArranqueResultado(), Limpieza());

    private void PlantarRestos()
    {
        File.WriteAllText(_rutas.ArchivoDispositivoObsoleto, Guid.NewGuid().ToString());
        File.WriteAllText(_rutas.ArchivoDispositivoObsoleto + ".tmp", Guid.NewGuid().ToString());
    }

    private bool HayRestos() => File.Exists(_rutas.ArchivoDispositivoObsoleto) || File.Exists(_rutas.ArchivoDispositivoObsoleto + ".tmp");

    // ---------- La limpieza sola ----------

    [Fact]
    public void Borra_el_identificador_de_la_tablet_y_su_temporal_y_limpia_la_preferencia()
    {
        PlantarRestos();

        Limpieza().Ejecutar();

        Assert.False(HayRestos());
        Assert.Equal(1, _prefs.Llamadas);
    }

    [Fact]
    public void Si_no_hay_restos_no_falla_ni_crea_nada()
    {
        Limpieza().Ejecutar();
        Limpieza().Ejecutar();

        Assert.False(HayRestos());
        Assert.Empty(Directory.GetFiles(_dir));
        Assert.Equal(2, _prefs.Llamadas);
    }

    [Fact]
    public void No_toca_ningun_otro_archivo_del_directorio_de_datos()
    {
        PlantarRestos();
        var otro = Path.Combine(_dir, "aviss.db");
        File.WriteAllText(otro, "datos");

        Limpieza().Ejecutar();

        Assert.True(File.Exists(otro));
        Assert.Equal("datos", File.ReadAllText(otro));
    }

    [Fact]
    public void Un_archivo_que_no_se_puede_borrar_no_hace_fallar_y_se_borra_la_proxima_vez()
    {
        PlantarRestos();

        // Un lector con el archivo abierto y sin compartirlo: en Windows impide borrarlo (en Linux el borrado igual anda).
        using (new FileStream(_rutas.ArchivoDispositivoObsoleto, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => Limpieza().Ejecutar());
            Assert.Null(ex);
            Assert.Equal(1, _prefs.Llamadas);       // la preferencia se limpia igual
        }

        Limpieza().Ejecutar();                      // "el próximo arranque"
        Assert.False(HayRestos());
    }

    [Fact]
    public void Si_la_preferencia_no_se_puede_borrar_igual_se_borran_los_archivos_y_no_falla()
    {
        PlantarRestos();
        _prefs.Falla = true;

        var ex = Record.Exception(() => Limpieza().Ejecutar());

        Assert.Null(ex);
        Assert.False(HayRestos());
        Assert.Equal(1, _prefs.Llamadas);
    }

    // ---------- Cuándo la llama el arranque ----------

    [Fact]
    public async Task Una_base_nueva_no_genera_dispositivo_id_y_limpia_lo_que_hubiera()
    {
        PlantarRestos();   // una tablet que ya tenía la app y se borró solo la base

        var r = await Arranque().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseCreada, r.Tipo);
        Assert.False(HayRestos());
        Assert.Equal(1, _prefs.Llamadas);
    }

    [Fact]
    public async Task Al_reabrir_una_base_al_dia_borra_los_restos_y_no_los_vuelve_a_crear()
    {
        await Arranque().IniciarAsync();       // crea la base
        PlantarRestos();

        var r = await Arranque().IniciarAsync();
        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.False(HayRestos());

        await Arranque().IniciarAsync();
        Assert.False(HayRestos());
        Assert.Equal(3, _prefs.Llamadas);      // una por cada vez que la app quedó abierta
    }

    [Fact]
    public async Task Despues_de_actualizar_la_base_borra_los_restos()
    {
        await Arranque().IniciarAsync();
        PlantarRestos();
        var llamadasAntes = _prefs.Llamadas;
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Arranque(new MigradorFalso { Modo = ModoMigrador.Exito }).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.False(HayRestos());
        Assert.Equal(llamadasAntes + 1, _prefs.Llamadas);
    }

    [Fact]
    public async Task Un_archivo_bloqueado_no_impide_abrir_la_app()
    {
        await Arranque().IniciarAsync();
        PlantarRestos();

        using (new FileStream(_rutas.ArchivoDispositivoObsoleto, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = await Arranque().IniciarAsync();
            Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
            Assert.True(r.PuedeAbrir);
        }
    }

    [Fact]
    public async Task Si_la_actualizacion_falla_no_se_borra_nada()
    {
        await Arranque().IniciarAsync();
        PlantarRestos();
        var llamadasAntes = _prefs.Llamadas;
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Arranque(new MigradorFalso { Modo = ModoMigrador.FallaSinCambios }).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.True(HayRestos());
        Assert.Equal(llamadasAntes, _prefs.Llamadas);
    }

    [Fact]
    public async Task Si_la_base_es_mas_nueva_no_se_borra_nada()
    {
        await Arranque().IniciarAsync();
        SqliteConnection.ClearAllPools();
        using (var cx = new SqliteConnection($"Data Source={_rutas.BaseDeDatos};Pooling=False"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_Futura', '99.0.0')";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        PlantarRestos();
        var llamadasAntes = _prefs.Llamadas;

        var r = await Arranque().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseMasNueva, r.Tipo);
        Assert.True(HayRestos());
        Assert.Equal(llamadasAntes, _prefs.Llamadas);
    }

    [Fact]
    public async Task Si_la_base_esta_danada_no_se_borra_nada()
    {
        File.WriteAllText(_rutas.BaseDeDatos, "esto no es una base de datos de verdad");
        PlantarRestos();

        var r = await Arranque().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseDanada, r.Tipo);
        Assert.True(HayRestos());
        Assert.Equal(0, _prefs.Llamadas);
    }

    [Fact]
    public async Task Si_no_hay_espacio_para_la_copia_no_se_borra_nada()
    {
        await Arranque().IniciarAsync();
        PlantarRestos();
        var llamadasAntes = _prefs.Llamadas;
        var sinEspacio = new ArranqueService(_rutas, new MigradorFalso(), new BackupService(_rutas, _reloj, new FakeEspacioLibre { Bytes = 0 }),
            new SembradorInicial(_reloj), new ArranqueResultado(), Limpieza());

        var r = await sinEspacio.IniciarAsync();

        Assert.Equal(TipoResultadoArranque.SinEspacioParaCopia, r.Tipo);
        Assert.True(HayRestos());
        Assert.Equal(llamadasAntes, _prefs.Llamadas);
    }
}
