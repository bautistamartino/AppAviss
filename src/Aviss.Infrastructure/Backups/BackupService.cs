using System.Globalization;
using System.Text.RegularExpressions;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Sistema;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Backups;

/// <summary>
/// Copias de seguridad de la base (RF-21). Orden de cada copia, pensado para que NUNCA se pierda una buena por una mala:
/// 1) comprobar espacio, 2) copiar con la API de backup de SQLite (consistente aunque haya WAL) a un archivo .tmp,
/// 3) verificar la copia, 4) renombrarla al nombre final, 5) recién entonces rotar (borrar las sobrantes).
/// </summary>
public sealed class BackupService : IBackupService
{
    public const int CopiasAConservar = 10;
    private const long MargenFijoBytes = 16L * 1024 * 1024;

    // aviss-20261116T120000000Z-previa-actualizacion.db
    private static readonly Regex PatronNombre =
        new(@"^aviss-\d{8}T\d{9}Z-(previa-actualizacion|a-pedido)\.db$", RegexOptions.Compiled);

    private readonly RutasAviss _rutas;
    private readonly IRelojService _reloj;
    private readonly IEspacioLibreProvider _espacio;

    public BackupService(RutasAviss rutas, IRelojService reloj, IEspacioLibreProvider espacio)
    {
        _rutas = rutas;
        _reloj = reloj;
        _espacio = espacio;
    }

    public Task<Resultado> CrearAsync(MotivoBackup motivo, CancellationToken ct = default)
    {
        // La copia es trabajo de disco, no hay nada realmente asíncrono: se hace en el momento.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Crear(motivo));
    }

    private Resultado Crear(MotivoBackup motivo)
    {
        var origen = _rutas.BaseDeDatos;
        if (!File.Exists(origen))
            return Resultado.Error(CodigoError.NoEncontrado, "No hay una base de datos para copiar.");

        string? temporal = null;
        try
        {
            Directory.CreateDirectory(_rutas.DirectorioBackups);
            LimpiarTemporales();

            // Espacio: base + su WAL + 10 % + 16 MB de margen.
            var necesario = Tamano(origen) + Tamano(origen + "-wal");
            necesario += necesario / 10 + MargenFijoBytes;
            if (_espacio.BytesLibres(_rutas.DirectorioBackups) < necesario)
                return Resultado.Error(CodigoError.SinEspacioParaCopia, "No hay espacio suficiente para hacer la copia de seguridad.");

            var final = Path.Combine(_rutas.DirectorioBackups, NombreFinal(motivo));
            if (File.Exists(final))
                return Resultado.Error(CodigoError.CopiaFallida, "No se pudo hacer la copia de seguridad.");
            temporal = final + ".tmp";

            Copiar(origen, temporal);

            // Se verifica ANTES de darle el nombre final: una copia incompleta nunca parece válida.
            if (!InspeccionSqlite.EstaSana(temporal, completa: true)
                || InspeccionSqlite.HuellaEsquema(temporal) != InspeccionSqlite.HuellaEsquema(origen)
                || InspeccionSqlite.MigracionesAplicadas(temporal).Count != InspeccionSqlite.MigracionesAplicadas(origen).Count)
            {
                BorrarConAcompaniantes(temporal);
                return Resultado.Error(CodigoError.CopiaFallida, "La copia de seguridad no pasó la verificación.");
            }

            BorrarSiExiste(temporal + "-wal");
            BorrarSiExiste(temporal + "-shm");
            File.Move(temporal, final);
            temporal = null;

            Rotar(); // recién ahora, con la copia nueva ya completa y verificada
            return Resultado.Ok();
        }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (temporal is not null) BorrarConAcompaniantes(temporal);
            return Resultado.Error(CodigoError.CopiaFallida, "No se pudo hacer la copia de seguridad.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private string NombreFinal(MotivoBackup motivo)
    {
        var fecha = _reloj.UtcAhora.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var etiqueta = motivo == MotivoBackup.PreviaActualizacion ? "previa-actualizacion" : "a-pedido";
        return $"aviss-{fecha}-{etiqueta}.db";
    }

    private static void Copiar(string origen, string destino)
    {
        using var desde = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = origen, Pooling = false }.ToString());
        using var hacia = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destino, Pooling = false }.ToString());
        desde.Open();
        hacia.Open();
        desde.BackupDatabase(hacia);
    }

    /// <summary>Conserva las 10 más nuevas (por la fecha del nombre) y, además, la previa a actualización más reciente si quedó fuera.</summary>
    private void Rotar()
    {
        var copias = Directory.GetFiles(_rutas.DirectorioBackups)
            .Where(f => PatronNombre.IsMatch(Path.GetFileName(f)))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        var conservar = copias.Take(CopiasAConservar).ToHashSet();
        var previaMasReciente = copias.FirstOrDefault(f => f.EndsWith("-previa-actualizacion.db", StringComparison.Ordinal));
        if (previaMasReciente is not null) conservar.Add(previaMasReciente);

        foreach (var sobrante in copias.Where(f => !conservar.Contains(f)))
            BorrarSiExiste(sobrante); // si no se puede borrar ahora, se reintenta en la próxima copia
    }

    /// <summary>Los .tmp de copias que se cortaron a medias: no son copias, se limpian.</summary>
    private void LimpiarTemporales()
    {
        foreach (var f in Directory.GetFiles(_rutas.DirectorioBackups, "*.tmp*"))
            BorrarSiExiste(f);
    }

    private static long Tamano(string ruta) => File.Exists(ruta) ? new FileInfo(ruta).Length : 0;

    private static void BorrarConAcompaniantes(string ruta)
    {
        BorrarSiExiste(ruta);
        BorrarSiExiste(ruta + "-wal");
        BorrarSiExiste(ruta + "-shm");
        BorrarSiExiste(ruta + "-journal");
    }

    private static void BorrarSiExiste(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
