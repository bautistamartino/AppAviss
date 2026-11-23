using System.Security.Cryptography;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Sistema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Arranque;

/// <summary>
/// Lo que pasa al abrir la app: crear la base si no existe, o revisarla y actualizarla con copia previa (RF-19 a RF-22, RF-27).
/// Regla de oro: ante la duda no se toca nada. Una base dañada o más nueva se deja como está, y si una actualización
/// falla se vuelve a la copia previa; si ni eso se puede, se conserva todo y la app no abre.
/// </summary>
public sealed class ArranqueService : IArranqueService
{
    private readonly RutasAviss _rutas;
    private readonly IMigrador _migrador;
    private readonly IBackupService _backups;
    private readonly SembradorInicial _sembrador;
    private readonly ArranqueResultado _resultado;
    private readonly LimpiezaDeRastros _limpieza;

    public ArranqueService(RutasAviss rutas, IMigrador migrador, IBackupService backups, SembradorInicial sembrador,
        ArranqueResultado resultado, LimpiezaDeRastros limpieza)
    {
        _rutas = rutas;
        _migrador = migrador;
        _backups = backups;
        _sembrador = sembrador;
        _resultado = resultado;
        _limpieza = limpieza;
    }

    public async Task<ResultadoArranque> IniciarAsync(CancellationToken ct = default)
    {
        ResultadoArranque resultado;
        try
        {
            resultado = await IniciarInternoAsync(ct);
        }
        catch (ForeignKeysNoActivasException)
        {
            resultado = Crear(TipoResultadoArranque.ClavesForaneasInactivas);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca se devuelve el texto de la excepción: podría traer rutas o datos (RF-28).
            resultado = Crear(TipoResultadoArranque.ErrorInesperado);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
        // Solo si la app queda abierta (Correcto, BaseCreada o BaseActualizada): una actualización fallida, una base dañada
        // o más nueva no pierden nada por esto (spec 006, RF-21). Nunca lanza.
        if (resultado.PuedeAbrir) _limpieza.Ejecutar();

        _resultado.Establecer(resultado);
        return resultado;
    }

    private async Task<ResultadoArranque> IniciarInternoAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_rutas.DirectorioDatos);

        DescartarArchivosDeCreacion();

        if (!File.Exists(_rutas.BaseDeDatos))
        {
            await CrearBaseNuevaAsync(ct);
            await VerificarConexionAsync(ct);
            return Crear(TipoResultadoArranque.BaseCreada);
        }

        // 1) ¿Está sana? Si no, se copia aparte y NO se toca (RF-27).
        if (!InspeccionSqlite.EstaSana(_rutas.BaseDeDatos))
        {
            CopiarADanadas();
            return Crear(TipoResultadoArranque.BaseDanada);
        }

        // 2) ¿Es de una versión más nueva que la app? No se abre ni se toca.
        var aplicadas = _migrador.Aplicadas(_rutas.BaseDeDatos);
        var conocidas = _migrador.Conocidas();
        if (aplicadas.Any(a => !conocidas.Contains(a)))
            return Crear(TipoResultadoArranque.BaseMasNueva);

        // 3) ¿Hay migraciones pendientes? Copia previa verificada y recién después se migra.
        var tipo = TipoResultadoArranque.Correcto;
        if (conocidas.Any(c => !aplicadas.Contains(c)))
        {
            var copia = await _backups.CrearAsync(MotivoBackup.PreviaActualizacion, ct);
            if (!copia.EsCorrecto)
                return Crear(copia.Codigo == CodigoError.SinEspacioParaCopia
                    ? TipoResultadoArranque.SinEspacioParaCopia
                    : TipoResultadoArranque.CopiaPreviaFallida);

            var huellaAntes = InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
            try
            {
                await _migrador.AplicarAsync(_rutas.BaseDeDatos, ct);
                tipo = TipoResultadoArranque.BaseActualizada;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return RestaurarSiHaceFalta(huellaAntes, aplicadas);
            }
        }

        // 4) Estados que falten y comprobación de claves foráneas.
        await VerificarConexionAsync(ct);
        return Crear(tipo);
    }

    // ---------- Creación (RF-20) ----------

    /// <summary>
    /// Arma la base en <c>aviss.db.creando</c>; solo cuando está completa y verificada se mueve a <c>aviss.db</c>.
    /// Si la app se corta en el medio queda un .creando huérfano (se descarta al próximo arranque) y nunca un aviss.db a medias.
    /// </summary>
    private async Task CrearBaseNuevaAsync(CancellationToken ct)
    {
        var creando = _rutas.BaseDeDatosCreando;
        try
        {
            await _migrador.AplicarAsync(creando, ct);

            using (var ctx = NuevoContexto(creando))
            {
                await _sembrador.SembrarEstadosAsync(ctx, ct);
            }

            // Se vuelca el WAL al archivo principal para que el .creando sea autosuficiente antes de moverlo.
            using (var cx = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = creando, Pooling = false }.ToString()))
            {
                cx.Open();
                using var cmd = cx.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            if (!InspeccionSqlite.EstaSana(creando, completa: true))
                throw new InvalidOperationException("La base recién creada no pasó la verificación.");

            BorrarSiExiste(creando + "-wal");
            BorrarSiExiste(creando + "-shm");
            File.Move(creando, _rutas.BaseDeDatos);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            DescartarArchivosDeCreacion();
            throw;
        }
    }

    private void DescartarArchivosDeCreacion()
    {
        foreach (var sufijo in new[] { "", "-wal", "-shm", "-journal" })
            BorrarSiExiste(_rutas.BaseDeDatosCreando + sufijo);
    }

    // ---------- Base dañada (RF-27) ----------

    /// <summary>
    /// Copia el archivo dañado a <c>danadas/</c> para que alguien pueda intentar recuperarlo. El nombre sale del contenido:
    /// si se reinicia la app con la misma base dañada no se acumulan copias iguales. El original no se toca.
    /// </summary>
    private void CopiarADanadas()
    {
        try
        {
            Directory.CreateDirectory(_rutas.DirectorioDanadas);
            string hash;
            using (var flujo = new FileStream(_rutas.BaseDeDatos, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                hash = Convert.ToHexString(SHA256.HashData(flujo))[..16].ToLowerInvariant();

            var destino = Path.Combine(_rutas.DirectorioDanadas, $"aviss-danada-{hash}.db");
            if (File.Exists(destino)) return;
            File.Copy(_rutas.BaseDeDatos, destino);
            var wal = _rutas.BaseDeDatos + "-wal";
            if (File.Exists(wal)) File.Copy(wal, destino + "-wal", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si ni siquiera se puede copiar, igual se informa que la base está dañada; el original sigue intacto.
        }
    }

    // ---------- Restauración (RF-22) ----------

    private ResultadoArranque RestaurarSiHaceFalta(string huellaAntes, IReadOnlyList<string> aplicadasAntes)
    {
        SqliteConnection.ClearAllPools();

        // Si la migración falló sin dejar rastros (mismo esquema y mismas migraciones) no hay nada que restaurar.
        bool cambio;
        try
        {
            cambio = InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos) != huellaAntes
                || !_migrador.Aplicadas(_rutas.BaseDeDatos).SequenceEqual(aplicadasAntes);
        }
        catch (SqliteException)
        {
            cambio = true;
        }
        if (!cambio) return Crear(TipoResultadoArranque.ActualizacionFallidaRestaurada);

        var restaurando = _rutas.BaseDeDatos + ".restaurando";
        try
        {
            var previa = Directory.Exists(_rutas.DirectorioBackups)
                ? Directory.GetFiles(_rutas.DirectorioBackups, "aviss-*-previa-actualizacion.db")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (previa is null) throw new FileNotFoundException("No hay copia previa.");

            // Se copia a un archivo aparte y se VERIFICA antes de tocar la base: si la copia no sirve, la base queda como está.
            File.Copy(previa, restaurando, overwrite: true);
            if (!InspeccionSqlite.EstaSana(restaurando, completa: true)
                || InspeccionSqlite.HuellaEsquema(restaurando) != huellaAntes
                || !InspeccionSqlite.MigracionesAplicadas(restaurando).SequenceEqual(aplicadasAntes))
                throw new InvalidOperationException("La copia previa no coincide con la base original.");

            // Un -wal viejo mezclado con el archivo restaurado lo corrompería: se borran antes de reemplazar.
            BorrarSiExiste(_rutas.BaseDeDatos + "-wal");
            BorrarSiExiste(_rutas.BaseDeDatos + "-shm");
            File.Move(restaurando, _rutas.BaseDeDatos, overwrite: true);
            return Crear(TipoResultadoArranque.ActualizacionFallidaRestaurada);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
        {
            // No se borra ni se sobrescribe nada importante: la base (como quedó) y la copia previa se conservan.
            BorrarSiExiste(restaurando);
            return Crear(TipoResultadoArranque.ActualizacionFallidaSinRestaurar);
        }
    }

    // ---------- Apertura normal ----------

    /// <summary>Siembra los estados que falten y comprueba (RF-16) que la conexión exige las claves foráneas.</summary>
    private async Task VerificarConexionAsync(CancellationToken ct)
    {
        using var ctx = NuevoContexto(_rutas.BaseDeDatos);
        await ctx.Database.OpenConnectionAsync(ct); // el interceptor activa los PRAGMA y verifica foreign_keys
        InterceptorConexion.VerificarForeignKeys(ctx.Database.GetDbConnection());
        await _sembrador.SembrarEstadosAsync(ctx, ct);
    }

    private static AvissDbContext NuevoContexto(string ruta)
    {
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, ruta);
        return new AvissDbContext(opciones.Options);
    }

    private static void BorrarSiExiste(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- Mensajes (RF-28) ----------

    private static ResultadoArranque Crear(TipoResultadoArranque tipo) => new(tipo, MensajePara(tipo));

    /// <summary>
    /// Un mensaje fijo por tipo, en español. A propósito NO lleva rutas, nombres de tambos ni de animales, ni el texto de
    /// las excepciones (podría traer datos): el detalle técnico no se muestra al veterinario.
    /// </summary>
    public static string MensajePara(TipoResultadoArranque tipo) => tipo switch
    {
        TipoResultadoArranque.Correcto => "La base de datos está lista.",
        TipoResultadoArranque.BaseCreada => "Se creó la base de datos de Aviss.",
        TipoResultadoArranque.BaseActualizada => "Se actualizó la base de datos. Antes se guardó una copia de seguridad.",
        TipoResultadoArranque.BaseMasNueva => "Estos datos fueron creados con una versión más nueva de Aviss. Actualizá la aplicación para poder abrirlos. No se modificó nada.",
        TipoResultadoArranque.BaseDanada => "La base de datos parece dañada. Se guardó una copia aparte y no se modificó el original. Pedí ayuda antes de seguir trabajando.",
        TipoResultadoArranque.SinEspacioParaCopia => "No hay espacio suficiente para hacer la copia de seguridad previa a la actualización. Liberá espacio en la tablet y volvé a abrir Aviss. No se modificó nada.",
        TipoResultadoArranque.CopiaPreviaFallida => "No se pudo hacer la copia de seguridad previa a la actualización. No se modificó nada; volvé a abrir Aviss.",
        TipoResultadoArranque.ActualizacionFallidaRestaurada => "La actualización de la base de datos falló y se recuperó el estado anterior. Esta versión de Aviss no puede abrirse; avisá al soporte.",
        TipoResultadoArranque.ActualizacionFallidaSinRestaurar => "La actualización de la base de datos falló y no se pudo recuperar el estado anterior automáticamente. No se borró nada: los datos y la copia de seguridad se conservan. Avisá al soporte antes de volver a abrir Aviss.",
        TipoResultadoArranque.ClavesForaneasInactivas => "No se pudo activar la verificación de relaciones de la base de datos. Por seguridad, Aviss no se abre.",
        TipoResultadoArranque.ErrorInesperado => "Ocurrió un error inesperado al preparar la base de datos. Volvé a abrir Aviss; si se repite, avisá al soporte.",
        _ => "No se pudo preparar la base de datos."
    };
}
