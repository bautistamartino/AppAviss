using System.Security.Cryptography;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006, RF-12, RF-15, RF-16 y RF-22: el arranque actualiza una base de la versión anterior (con veterinarios y datos)
/// con la migración REAL "QuitarVeterinario": copia previa verificada, y si algo falla, el estado anterior vuelve y el mensaje
/// es siempre el mismo. Los casos de arranque en general (base nueva, dañada, más nueva) están en <see cref="ArranqueServiceTests"/>.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class ArranqueActualizacionTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    private readonly FakeReloj _reloj = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public ArranqueActualizacionTests() => _rutas = new RutasAviss(_db.Directorio);
    public void Dispose() => _db.Dispose();

    private ArranqueService Servicio(IMigrador? migrador = null)
        => new(_rutas, migrador ?? new MigradorEf(), new BackupService(_rutas, _reloj, _espacio), new SembradorInicial(_reloj),
            new ArranqueResultado(), new LimpiezaDeRastros(_rutas, new LimpiezaPreferenciasNula()));

    /// <summary>Deja en aviss.db una base de la versión anterior con datos de todas las tablas y sin conexiones abiertas.</summary>
    private EsquemaAnterior.Poblado BaseVieja()
    {
        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        var p = new EsquemaAnterior(_db).Poblar();
        _db.CerrarCruda();
        return p;
    }

    private Dictionary<string, List<string>> Foto()
    {
        var foto = EsquemaAnterior.Instantanea(_db);
        _db.CerrarCruda();
        return foto;
    }

    /// <summary>
    /// Las filas de antes siguen idénticas. Se deja afuera el catálogo de estados: al abrir, el arranque siembra los que falten
    /// (las que ya estaban se comprueban una por una).
    /// </summary>
    private static void AssertIguales(Dictionary<string, List<string>> antes, Dictionary<string, List<string>> despues)
    {
        foreach (var (tabla, filas) in antes)
        {
            if (tabla == "EstadoReproductivo") Assert.All(filas, f => Assert.Contains(f, despues[tabla]));
            else Assert.True(filas.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió.");
        }
    }

    private static SqliteConnection Abrir(string ruta)
    {
        var cx = new SqliteConnection($"Data Source={ruta};Pooling=False;Mode=ReadOnly");
        cx.Open();
        return cx;
    }

    private static long Contar(string ruta, string sql)
    {
        using var cx = Abrir(ruta);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Texto(string ruta, string sql)
    {
        using var cx = Abrir(ruta);
        using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar())!;
    }

    private string[] Backups() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db").OrderBy(f => f, StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();

    private string HuellaArchivos()
    {
        var bytes = File.ReadAllBytes(_rutas.BaseDeDatos);
        var wal = _rutas.BaseDeDatos + "-wal";
        if (File.Exists(wal)) bytes = bytes.Concat(File.ReadAllBytes(wal)).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private const string QuitarVeterinarioEnHistorial = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_QuitarVeterinario'";
    private const string HayUsuario = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'";

    // ---------- RF-12: copia previa y actualización ----------

    [Fact]
    public async Task Hace_la_copia_previa_verificada_y_actualiza()
    {
        BaseVieja();
        var antes = Foto();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.True(r.PuedeAbrir);

        // La copia previa existe, está sana y es la base VIEJA completa (con Usuario y sin la migración nueva).
        var copia = Assert.Single(Backups());
        Assert.EndsWith("-previa-actualizacion.db", copia);
        Assert.Equal("ok", Texto(copia, "PRAGMA integrity_check"));
        Assert.Equal(1, Contar(copia, HayUsuario));
        Assert.Equal(0, Contar(copia, QuitarVeterinarioEnHistorial));
        Assert.Equal(antes["Tacto"].Count, Contar(copia, "SELECT COUNT(*) FROM Tacto"));

        // La base quedó actualizada y con todos sus datos.
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, HayUsuario));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, QuitarVeterinarioEnHistorial));
        AssertIguales(antes, Foto());
    }

    [Fact]
    public async Task Reabrir_no_repite_nada()
    {
        BaseVieja();
        var antes = Foto();
        await Servicio().IniciarAsync();
        var fotoActualizada = Foto();
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.Single(Backups());                       // no se hizo otra copia
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, QuitarVeterinarioEnHistorial));
        Assert.Equal(fotoActualizada, Foto());          // ni una fila distinta
        AssertIguales(antes, Foto());
    }

    [Fact]
    public async Task Sin_espacio_para_la_copia_no_se_actualiza_nada()
    {
        BaseVieja();
        var antes = Foto();
        var huella = HuellaArchivos();
        _espacio.Bytes = 0;

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.SinEspacioParaCopia, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(huella, HuellaArchivos());          // ni un byte
        Assert.Empty(Backups());
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));
        AssertIguales(antes, Foto());
    }

    [Fact]
    public async Task Si_la_copia_previa_falla_no_se_actualiza_nada()
    {
        BaseVieja();
        var antes = Foto();
        var huella = HuellaArchivos();
        File.WriteAllText(_rutas.DirectorioBackups, "un archivo donde debería haber una carpeta"); // fuerza el error de copia

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.CopiaPreviaFallida, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(huella, HuellaArchivos());
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));
        AssertIguales(antes, Foto());
    }

    // ---------- RF-22: fallas ----------

    [Fact]
    public async Task Falla_a_mitad_de_la_migracion_real_deja_la_base_igual_y_se_repite_el_mismo_mensaje()
    {
        BaseVieja();
        var antes = Foto();
        var huellaEsquema = Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
        var migrador = new MigradorConInterceptor(new InterceptorDeComandos { FallarEnComando = 40 });

        var primero = await Servicio(migrador).IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(1));
        var segundo = await Servicio(migrador).IniciarAsync();

        Assert.Equal(2, migrador.Intentos);
        foreach (var r in new[] { primero, segundo })
        {
            Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
            Assert.False(r.PuedeAbrir);
        }
        Assert.Equal(primero.Mensaje, segundo.Mensaje);
        Assert.Equal(ArranqueService.MensajePara(TipoResultadoArranque.ActualizacionFallidaRestaurada), segundo.Mensaje);

        // La base es idéntica a la de antes y la copia previa más reciente se conserva, sana.
        Assert.Equal(huellaEsquema, Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, QuitarVeterinarioEnHistorial));
        Assert.Equal(antes, Foto());
        var copias = Backups();
        Assert.Equal(2, copias.Length);
        Assert.Equal("ok", Texto(copias[^1], "PRAGMA integrity_check"));
        Assert.Equal(1, Contar(copias[^1], HayUsuario));
    }

    [Fact]
    public async Task Falla_que_deja_restos_se_restaura_la_copia_y_se_repite_el_mismo_mensaje()
    {
        BaseVieja();
        var antes = Foto();
        var huellaEsquema = Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
        // Este migrador falso SÍ deja una tabla de más antes de fallar: obliga a restaurar la copia previa de verdad.
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaAMitad };

        var primero = await Servicio(falso).IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(1));
        var segundo = await Servicio(falso).IniciarAsync();

        Assert.Equal(2, falso.Llamadas);
        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, primero.Tipo);
        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, segundo.Tipo);
        Assert.Equal(primero.Mensaje, segundo.Mensaje);
        Assert.Equal(huellaEsquema, Aviss.Infrastructure.Arranque.InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos));
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'v2'"));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));
        Assert.Equal(antes, Foto());
        Assert.NotEmpty(Backups());
    }

    [Fact]
    public async Task Si_no_se_puede_restaurar_se_conserva_todo_y_la_app_no_abre()
    {
        BaseVieja();
        var falso = new MigradorFalso { Modo = ModoMigrador.FallaAMitad };
        // Justo antes de migrar se estropea la copia previa: la restauración no puede verificarse.
        falso.AlAplicar = () =>
        {
            foreach (var copia in Backups()) File.WriteAllText(copia, "copia estropeada");
        };

        var r = await Servicio(falso).IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaSinRestaurar, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.True(File.Exists(_rutas.BaseDeDatos));                 // no se borra la base
        Assert.Single(Backups());                                      // ni la copia (aunque esté estropeada)
        Assert.False(File.Exists(_rutas.BaseDeDatos + ".restaurando"));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));       // los datos siguen ahí
        Assert.DoesNotContain("aviss.db", r.Mensaje);
    }

    // ---------- RF-15: volver a una copia vieja a mano ----------

    [Fact]
    public async Task Restaurar_a_mano_una_copia_vieja_vuelve_a_actualizar()
    {
        BaseVieja();
        var antes = Foto();
        await Servicio().IniciarAsync();
        var copia = Assert.Single(Backups());

        // Alguien pone la copia previa (versión anterior) en lugar de la base.
        _db.CerrarCruda();
        foreach (var sufijo in new[] { "-wal", "-shm" })
            if (File.Exists(_rutas.BaseDeDatos + sufijo)) File.Delete(_rutas.BaseDeDatos + sufijo);
        File.Copy(copia, _rutas.BaseDeDatos, overwrite: true);
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, HayUsuario));
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.Equal(2, Backups().Length);                              // otra copia previa, de la base restaurada
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, HayUsuario));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, QuitarVeterinarioEnHistorial));
        AssertIguales(antes, Foto());

        // Y vuelven a valer las garantías de la base nueva (RF-13 / RF-14).
        Assert.Equal(0, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'TR_Tacto_NoModificar'"));
        Assert.Equal(1, Contar(_rutas.BaseDeDatos, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'TR_Historial_NoBorrar'"));
    }
}
