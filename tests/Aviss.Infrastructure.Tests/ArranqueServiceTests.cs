using System.Security.Cryptography;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Sistema;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-19, 20, 22, 27 y 28: crear, actualizar con copia previa, restaurar y no tocar lo que no se entiende.</summary>
public sealed class ArranqueServiceTests : IDisposable
{
    private const string CaravanaSecreta = "CARAVANA-SECRETA-777";
    private const string TamboSecreto = "Tambo Secreto La Esperanza";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aviss-arranque-" + Guid.NewGuid().ToString("N"));
    private readonly FakeReloj _reloj = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public ArranqueServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _rutas = new RutasAviss(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private ArranqueService Servicio(IMigrador? migrador = null, ArranqueResultado? resultado = null)
        => new(_rutas, migrador ?? new MigradorEf(), new BackupService(_rutas, _reloj, _espacio),
            new SembradorInicial(_reloj), resultado ?? new ArranqueResultado());

    private static SqliteConnection Abrir(string ruta, bool soloLectura = false)
    {
        var cx = new SqliteConnection($"Data Source={ruta};Pooling=False" + (soloLectura ? ";Mode=ReadOnly" : ""));
        cx.Open();
        return cx;
    }

    private static object? Escalar(string ruta, string sql)
    {
        using var cx = Abrir(ruta, soloLectura: true);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Ejecutar(string ruta, string sql)
    {
        using var cx = Abrir(ruta);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Contar(string ruta, string sql) => Convert.ToInt64(Escalar(ruta, sql));

    /// <summary>Huella del archivo principal y de su -wal: si no cambia ni un byte, la base no se tocó.</summary>
    private string HuellaArchivos()
    {
        var bytes = File.ReadAllBytes(_rutas.BaseDeDatos);
        var wal = _rutas.BaseDeDatos + "-wal";
        if (File.Exists(wal)) bytes = bytes.Concat(File.ReadAllBytes(wal)).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private string[] Backups() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db") : Array.Empty<string>();

    /// <summary>Deja una base real, creada por el propio arranque, con un tambo y una caravana reconocibles.</summary>
    private async Task BaseExistenteAsync()
    {
        var r = await Servicio().IniciarAsync();
        Assert.Equal(TipoResultadoArranque.BaseCreada, r.Tipo);
        SqliteConnection.ClearAllPools();
        var ahora = ConvertidorFechaUtc.ATexto(_reloj.UtcAhora);
        var hash = new string('a', 64);
        Ejecutar(_rutas.BaseDeDatos,
            $"INSERT INTO Establecimiento (Id, Nombre, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ('E1', '{TamboSecreto}', 1, '{ahora}', '{ahora}');" +
            $"INSERT INTO Rodeo (Id, EstablecimientoId, Nombre, FechaImportacionUtc, ArchivoOrigenNombre, ArchivoOrigenHash, ArchivoOrigenBlob, PerfilExcel, Estado, Activo, CreadoEnUtc, ActualizadoEnUtc) " +
            $"VALUES ('R1', 'E1', 'Rodeo', '{ahora}', 'r.xls', '{hash}', x'010203', 'DairyComp', 'Abierto', 1, '{ahora}', '{ahora}');" +
            $"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, Origen, Anulado, CreadoEnUtc, ActualizadoEnUtc) " +
            $"VALUES ('A1', 'R1', '{CaravanaSecreta}', 'AltaEnCampo', 0, '{ahora}', '{ahora}');");
        SqliteConnection.ClearAllPools();
    }

    // ---------- Creación inicial (RF-20, RF-7b) ----------

    [Fact]
    public async Task Sin_base_crea_una_completa_con_estados_sin_veterinarios_y_sin_backup()
    {
        var resultado = new ArranqueResultado();
        var r = await Servicio(resultado: resultado).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseCreada, r.Tipo);
        Assert.True(r.PuedeAbrir);
        Assert.Same(r, resultado.Actual);
        Assert.True(File.Exists(_rutas.BaseDeDatos));
        Assert.False(File.Exists(_rutas.BaseDeDatosCreando));
        Assert.False(File.Exists(_rutas.ArchivoDispositivoObsoleto)); // ya no se genera ningún identificador de tablet
        Assert.Empty(Backups());

        Assert.Equal(0, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        Assert.Equal(6, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal("ok", Escalar(_rutas.BaseDeDatos, "PRAGMA integrity_check"));
    }

    [Fact]
    public async Task Un_creando_huerfano_se_descarta_y_se_recrea()
    {
        File.WriteAllText(_rutas.BaseDeDatosCreando, "restos de una creación cortada");
        File.WriteAllText(_rutas.BaseDeDatosCreando + "-wal", "restos");
        File.WriteAllText(_rutas.BaseDeDatosCreando + "-shm", "restos");

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseCreada, r.Tipo);
        Assert.False(File.Exists(_rutas.BaseDeDatosCreando));
        Assert.False(File.Exists(_rutas.BaseDeDatosCreando + "-wal"));
        Assert.False(File.Exists(_rutas.BaseDeDatosCreando + "-shm"));
        Assert.Equal(6, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task Si_la_creacion_falla_aviss_db_no_existe_a_medias()
    {
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaSinCambios };

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ErrorInesperado, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.False(File.Exists(_rutas.BaseDeDatos));
        Assert.False(File.Exists(_rutas.BaseDeDatosCreando));
    }

    // ---------- Base ya al día ----------

    [Fact]
    public async Task Base_al_dia_no_migra_ni_hace_backup()
    {
        await BaseExistenteAsync();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.True(r.PuedeAbrir);
        Assert.Empty(Backups());
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM Animal"));
    }

    [Fact]
    public async Task Al_arrancar_no_pisa_los_estados_que_el_veterinario_modifico()
    {
        await BaseExistenteAsync();
        Ejecutar(_rutas.BaseDeDatos, "UPDATE EstadoReproductivo SET Nombre = 'Seca larga' WHERE Codigo = 'SECA'");
        SqliteConnection.ClearAllPools();

        await Servicio().IniciarAsync();

        Assert.Equal("Seca larga", Escalar(_rutas.BaseDeDatos, "SELECT Nombre FROM EstadoReproductivo WHERE Codigo = 'SECA'"));
        Assert.Equal(6, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    // ---------- Actualización con backup (RF-19, RF-22) ----------

    [Fact]
    public async Task Con_una_migracion_pendiente_hace_el_backup_verificado_ANTES_de_migrar()
    {
        await BaseExistenteAsync();
        var estabaElBackupAlMigrar = false;
        var falso = new MigradorFalso { Modo = ModoMigrador.Exito };
        falso.AlAplicar = () =>
        {
            var copias = Backups();
            estabaElBackupAlMigrar = copias.Length == 1 && copias[0].EndsWith("-previa-actualizacion.db")
                && Convert.ToString(Escalar(copias[0], "PRAGMA integrity_check")) == "ok"
                && Contar(copias[0], "SELECT COUNT(*) FROM Animal") == 1;
        };

        var r = await Servicio(falso).IniciarAsync();

        Assert.True(estabaElBackupAlMigrar);
        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.True(r.PuedeAbrir);
        Assert.Equal(1, falso.Llamadas);
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'v2'"));
    }

    [Fact]
    public async Task Sin_espacio_para_la_copia_no_migra()
    {
        await BaseExistenteAsync();
        var antes = HuellaArchivos();
        _espacio.Bytes = 0;
        var falso = new MigradorFalso();

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.SinEspacioParaCopia, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(0, falso.Llamadas);
        Assert.Equal(antes, HuellaArchivos());
        Assert.Empty(Backups());
    }

    [Fact]
    public async Task Si_la_copia_previa_falla_no_migra()
    {
        await BaseExistenteAsync();
        var antes = HuellaArchivos();
        File.WriteAllText(_rutas.DirectorioBackups, "un archivo donde debería haber una carpeta"); // fuerza el error de copia
        var falso = new MigradorFalso();

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.CopiaPreviaFallida, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(0, falso.Llamadas);
        Assert.Equal(antes, HuellaArchivos());
    }

    [Fact]
    public async Task Si_la_migracion_falla_a_mitad_se_restaura_la_base_original()
    {
        await BaseExistenteAsync();
        const string esquema = "SELECT group_concat(name) FROM (SELECT name FROM sqlite_master ORDER BY name)";
        var esquemaAntes = Escalar(_rutas.BaseDeDatos, esquema);
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaAMitad };

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'v2'"));
        Assert.Equal(esquemaAntes, Escalar(_rutas.BaseDeDatos, esquema));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM Animal"));
        Assert.Equal("ok", Escalar(_rutas.BaseDeDatos, "PRAGMA integrity_check"));
        Assert.False(File.Exists(_rutas.BaseDeDatos + ".restaurando"));
        Assert.Single(Backups()); // la copia previa se conserva
    }

    [Fact]
    public async Task Si_la_migracion_falla_sin_cambiar_nada_la_base_queda_como_estaba()
    {
        await BaseExistenteAsync();
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaSinCambios };

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM Animal"));
        Assert.Equal("ok", Escalar(_rutas.BaseDeDatos, "PRAGMA integrity_check"));
    }

    [Fact]
    public async Task Si_la_restauracion_falla_se_conservan_la_base_y_la_copia_y_la_app_no_abre()
    {
        await BaseExistenteAsync();
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaAMitad };
        // Justo antes de migrar se estropea la copia previa: así la restauración no puede verificarse.
        falso.AlAplicar = () =>
        {
            foreach (var copia in Backups()) File.WriteAllText(copia, "copia estropeada");
        };

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaSinRestaurar, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.True(File.Exists(_rutas.BaseDeDatos));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'v2'")); // quedó como la dejó la falla
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM Animal"));
        Assert.Single(Backups());
        Assert.False(File.Exists(_rutas.BaseDeDatos + ".restaurando"));
    }

    // ---------- Base más nueva y base dañada (RF-27) ----------

    [Fact]
    public async Task Una_base_mas_nueva_no_se_abre_ni_cambia()
    {
        await BaseExistenteAsync();
        Ejecutar(_rutas.BaseDeDatos, "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_Futura', '99.0.0')");
        SqliteConnection.ClearAllPools();
        var antes = HuellaArchivos();
        var falso = new MigradorFalso();

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseMasNueva, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(0, falso.Llamadas);
        Assert.Equal(antes, HuellaArchivos());
        Assert.Empty(Backups());
    }

    [Fact]
    public async Task Un_archivo_que_no_es_sqlite_se_copia_a_danadas_y_el_original_queda_intacto()
    {
        File.WriteAllText(_rutas.BaseDeDatos, "esto no es una base de datos de verdad");
        var original = File.ReadAllBytes(_rutas.BaseDeDatos);

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseDanada, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(original, File.ReadAllBytes(_rutas.BaseDeDatos));
        var copias = Directory.GetFiles(_rutas.DirectorioDanadas);
        Assert.Single(copias);
        Assert.Equal(original, File.ReadAllBytes(copias[0]));
    }

    [Fact]
    public async Task Reiniciar_con_la_misma_base_danada_no_acumula_copias()
    {
        File.WriteAllText(_rutas.BaseDeDatos, "esto no es una base de datos de verdad");

        await Servicio().IniciarAsync();
        await Servicio().IniciarAsync();

        Assert.Single(Directory.GetFiles(_rutas.DirectorioDanadas));
    }

    // ---------- Mensajes (RF-28) ----------

    [Fact]
    public void Cada_tipo_tiene_un_mensaje_en_espanol_sin_rutas_ni_datos()
    {
        var tipos = Enum.GetValues<TipoResultadoArranque>();
        foreach (var tipo in tipos)
        {
            var mensaje = ArranqueService.MensajePara(tipo);
            Assert.False(string.IsNullOrWhiteSpace(mensaje), tipo.ToString());
            Assert.DoesNotContain("aviss.db", mensaje);
            Assert.DoesNotContain("/", mensaje);
            Assert.DoesNotContain("\\", mensaje);
            Assert.DoesNotContain("Exception", mensaje);
            Assert.DoesNotContain("SQLite", mensaje, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(tipos.Length, tipos.Select(ArranqueService.MensajePara).Distinct().Count());
    }

    [Fact]
    public async Task Los_mensajes_de_los_errores_reales_no_incluyen_caravanas_nombres_ni_rutas()
    {
        await BaseExistenteAsync();
        var mensajes = new List<string>();

        // Falla de migración con restauración.
        mensajes.Add((await Servicio(new MigradorFalso { Modo = ModoMigrador.FallaAMitad }).IniciarAsync()).Mensaje);

        // Sin espacio.
        _espacio.Bytes = 0;
        mensajes.Add((await Servicio(new MigradorFalso()).IniciarAsync()).Mensaje);
        _espacio.Bytes = long.MaxValue / 2;

        // Más nueva.
        SqliteConnection.ClearAllPools();
        Ejecutar(_rutas.BaseDeDatos, "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_Futura', '99.0.0')");
        SqliteConnection.ClearAllPools();
        mensajes.Add((await Servicio().IniciarAsync()).Mensaje);

        Assert.Equal(3, mensajes.Count);
        foreach (var m in mensajes)
        {
            Assert.False(string.IsNullOrWhiteSpace(m));
            Assert.DoesNotContain(CaravanaSecreta, m);
            Assert.DoesNotContain(TamboSecreto, m);
            Assert.DoesNotContain(_dir, m);
            Assert.DoesNotContain("aviss.db", m);
        }
    }

    // ---------- Contenedor ----------

    [Fact]
    public void El_contenedor_resuelve_el_arranque_los_backups_y_el_resultado()
    {
        var servicios = new ServiceCollection();
        servicios.AddAvissInfrastructure(_rutas);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.NotNull(proveedor.GetRequiredService<IArranqueService>());
        Assert.NotNull(proveedor.GetRequiredService<IBackupService>());
        Assert.NotNull(proveedor.GetRequiredService<IMigrador>());
        Assert.Same(proveedor.GetRequiredService<IArranqueResultado>(), proveedor.GetRequiredService<IArranqueResultado>());
    }
}
