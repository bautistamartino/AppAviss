using System.Security.Cryptography;
using Aviss.Application.Comun;
using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DominioExportacion = Aviss.Domain.Entidades.Exportacion;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// El caso de uso "Exportar" (spec 007). Es SINGLETON, como <see cref="ImportacionService"/>: no guarda estado, pero los repositorios y
/// el DbContext son "scoped" (un contexto por operación), así que cada paso abre su propio scope.
///
/// Orden de <see cref="ExportarAsync"/> (plan, "Servicio y orden de pasos"):
///   1. leer el rodeo y su original;  2. armar los valores de cada vaca (una instantánea de la base);
///   3. generar el archivo;  4. validarlo reabriéndolo;  5. guardarlo;  6. recién entonces dejar la constancia y marcar el rodeo.
/// Si algo falla antes del paso 5 no se guarda nada. Si falla el paso 6, el archivo ya está guardado: no se pierde nada y se avisa.
///
/// Ojo: NO se envuelve la consulta en <c>IUnitOfWork</c>. <see cref="ConsultaExportacion"/> abre su propia transacción de lectura
/// DIFERIDA (para no frenar un tacto que se guarda en otra pantalla, RF-25); la unidad de trabajo abre una inmediata y la volvería a bloquear.
/// </summary>
internal sealed class ExportacionService : IExportacionService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IExportadorArchivo _exportador;
    private readonly IEntregaArchivo _entrega;
    private readonly IRelojService _reloj;
    private readonly IZonaHoraria _zona;

    // SQLite admite un solo escritor: la constancia (una escritura chica) espera su turno como las demás.
    private readonly SemaphoreSlim _enSerie = new(1, 1);

    public ExportacionService(IServiceScopeFactory scopes, IExportadorArchivo exportador, IEntregaArchivo entrega, IRelojService reloj, IZonaHoraria zona)
        => (_scopes, _exportador, _entrega, _reloj, _zona) = (scopes, exportador, entrega, reloj, zona);

    // Lo único que se lee del rodeo (sin traer entidades): el original puede pesar hasta 25 MB.
    private sealed record RodeoLeido(string ArchivoNombre, byte[] Original);

    // ------------------------------------------------------------------ Lista (RF-1)

    public async Task<IReadOnlyList<RodeoParaExportarDto>> ListarRodeosAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvissDbContext>();

        // Una sola consulta con la cuenta de vacas incluida; se proyecta sin el original (el blob no se lee).
        var filas = await (from r in db.Rodeos.AsNoTracking()
                           join e in db.Establecimientos.AsNoTracking() on r.EstablecimientoId equals e.Id
                           where r.Activo
                           select new RodeoParaExportarDto(
                               r.Id, r.Nombre, e.Id, e.Nombre,
                               db.Animales.Count(a => a.RodeoId == r.Id && !a.Anulado)))
            .ToListAsync(ct);

        // El orden se hace en memoria (sin distinguir mayúsculas): SQLite no ordena bien los textos con Ñ y tildes.
        return filas
            .OrderBy(f => f.EstablecimientoNombre, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.RodeoNombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------------ Resumen (RF-2, RF-11)

    public async Task<Resultado<ResumenExportacionDto>> ObtenerResumenAsync(Guid rodeoId, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var proveedor = scope.ServiceProvider;
        if (!await ExisteActivoAsync(proveedor.GetRequiredService<AvissDbContext>(), rodeoId, ct))
            return Resultado.Error<ResumenExportacionDto>(CodigoError.NoEncontrado, MensajesExportacion.RodeoNoEncontrado);

        var datos = await proveedor.GetRequiredService<IConsultaExportacion>().ObtenerAsync(rodeoId, ct);
        var ultima = await proveedor.GetRequiredService<IExportacionRepository>().ObtenerUltimaAsync(rodeoId, ct);

        return Resultado.Ok(new ResumenExportacionDto(
            rodeoId, datos.TotalVacas, datos.VacasConEvento, datos.VacasSinEvento, datos.VacasCvetCeroConRegistro, datos.Jornada,
            ultima is null ? null : new UltimaExportacionDto(ultima.FechaUtc, ultima.ArchivoNombre, ultima.Formato, ultima.CantidadVacasConEvento),
            datos.VacasFueraDelArchivoConEvento));
    }

    // ------------------------------------------------------------------ Exportar (RF-18 a RF-24)

    public async Task<Resultado<ExportacionEntregadaDto>> ExportarAsync(Guid rodeoId, FormatoExportacion formato, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 1 y 2. El rodeo con su original, y los valores de cada vaca (la consulta lee todo de una misma instantánea).
        RodeoLeido rodeo;
        DatosExportacion datos;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AvissDbContext>();
            var leido = await db.Rodeos.AsNoTracking()
                .Where(r => r.Id == rodeoId && r.Activo)
                .Select(r => new RodeoLeido(r.ArchivoOrigenNombre, r.ArchivoOrigenBlob))
                .FirstOrDefaultAsync(ct);
            if (leido is null) return Error(CodigoError.NoEncontrado, MensajesExportacion.RodeoNoEncontrado);
            rodeo = leido;
            datos = await scope.ServiceProvider.GetRequiredService<IConsultaExportacion>().ObtenerAsync(rodeoId, ct);
        }

        // 3 y 4. Generar y validar: es CPU (y con 20.000 vacas, segundos), así que va fuera del hilo de la pantalla.
        ResultadoGeneracionArchivo generado;
        ResultadoValidacionArchivo validacion;
        try
        {
            generado = await Task.Run(() => _exportador.Generar(rodeo.Original, formato, datos.ValoresPorCaravana), ct);
            if (!generado.EsCorrecto)
                return Error(CodigoGeneracion(generado.Fallo!.Value), MensajeGeneracion(generado));

            validacion = await Task.Run(() => _exportador.Validar(rodeo.Original, generado.Contenido!, formato, datos.ValoresPorCaravana), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Una falla inesperada del exportador no debe mostrar su texto (podría traer una caravana): mensaje fijo.
            return Error(CodigoError.ExportacionNoValida, MensajesExportacion.NoSePudoGenerar);
        }
        if (!validacion.EsValido)
            return Error(CodigoError.ExportacionNoValida, MensajesExportacion.ExportacionNoValida);

        // 5. Guardar. Desde acá el archivo existe: un cancelar ya no puede dejarlo "a medias" porque quien guarda lo escribe oculto y lo publica al final.
        ct.ThrowIfCancellationRequested();
        var ahoraUtc = _reloj.UtcAhora;
        var nombre = NombreArchivoExportado.Crear(rodeo.ArchivoNombre, ahoraUtc, _zona.Zona, formato);
        ResultadoGuardadoArchivo guardado;
        try
        {
            guardado = await _entrega.GuardarAsync(nombre, generado.Contenido!, formato, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            guardado = ResultadoGuardadoArchivo.Fallido(FalloGuardado.Otro);   // el texto de la excepción no se muestra ni se guarda
        }
        if (!guardado.EsCorrecto)
            return Error(CodigoError.ArchivoNoGuardado, MensajesExportacion.Para(guardado.Fallo!.Value));

        // 6. Recién ahora la constancia y la marca de "exportado", en una transacción corta. Sin token de cancelación: el archivo
        // ya quedó entregado y la constancia tiene que quedar aunque el veterinario cancele en este instante.
        var huella = Convert.ToHexString(SHA256.HashData(generado.Contenido!)).ToLowerInvariant();
        var constancia = await RegistrarConstanciaAsync(rodeoId, ahoraUtc, guardado.Archivo!.Nombre, formato, huella, generado.Filas, generado.FilasConValores);

        return Resultado.Ok(new ExportacionEntregadaDto(guardado.Archivo, formato, generado.Filas, generado.FilasConValores, constancia));
    }

    /// <summary>
    /// Agrega la constancia y marca el rodeo como exportado, juntas o ninguna. Devuelve falso si no se pudo (RF-22: el archivo ya
    /// está guardado y no se pierde nada). La cantidad de vacas con evento es la del ARCHIVO (las que llevan valores en sus filas), no la
    /// de la base: una alta en campo con tacto no figura en el original y no se agrega fila.
    /// </summary>
    private async Task<bool> RegistrarConstanciaAsync(
        Guid rodeoId, DateTime ahoraUtc, string nombreArchivo, FormatoExportacion formato, string huella, int filas, int vacasConEvento)
    {
        await _enSerie.WaitAsync();
        try
        {
            using var scope = _scopes.CreateScope();
            var proveedor = scope.ServiceProvider;
            var resultado = await proveedor.GetRequiredService<IUnitOfWork>().EjecutarAsync(async () =>
            {
                var rodeo = await proveedor.GetRequiredService<IRodeoRepository>().ObtenerAsync(rodeoId);
                if (rodeo is null) return Resultado.Error(CodigoError.NoEncontrado, MensajesExportacion.RodeoNoEncontrado);
                proveedor.GetRequiredService<IExportacionRepository>().Agregar(
                    DominioExportacion.Registrar(rodeoId, ahoraUtc, nombreArchivo, formato, huella, filas, vacasConEvento, ahoraUtc));
                rodeo.MarcarExportado(ahoraUtc);
                return Resultado.Ok();
            });
            return resultado.EsCorrecto;
        }
        catch (Exception ex) when (ex is DbUpdateException or DominioException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return false;   // la transacción se revirtió sola: ni la constancia ni la marca quedaron a medias
        }
        finally
        {
            _enSerie.Release();
        }
    }

    // ------------------------------------------------------------------ Ayudas

    private static Task<bool> ExisteActivoAsync(AvissDbContext db, Guid rodeoId, CancellationToken ct)
        => db.Rodeos.AsNoTracking().AnyAsync(r => r.Id == rodeoId && r.Activo, ct);

    private static Resultado<ExportacionEntregadaDto> Error(CodigoError codigo, string mensaje)
        => Resultado.Error<ExportacionEntregadaDto>(codigo, mensaje);

    private static CodigoError CodigoGeneracion(FalloGeneracion fallo) => fallo switch
    {
        FalloGeneracion.OriginalIlegible => CodigoError.ArchivoRechazado,
        FalloGeneracion.ColumnaDuplicada => CodigoError.ColumnaDuplicada,
        FalloGeneracion.CsvNoAdmiteCaracteres => CodigoError.CsvNoAdmiteCaracteres,
        _ => CodigoError.ExportacionNoValida
    };

    private static string MensajeGeneracion(ResultadoGeneracionArchivo generado) => generado.Fallo switch
    {
        FalloGeneracion.OriginalIlegible => MensajesExportacion.OriginalIlegible,
        FalloGeneracion.ColumnaDuplicada => MensajesExportacion.ColumnaDuplicada(generado.ColumnaDuplicada ?? ""),
        FalloGeneracion.CsvNoAdmiteCaracteres => MensajesExportacion.CsvNoAdmiteCaracteres,
        _ => MensajesExportacion.NoSePudoGenerar
    };
}
