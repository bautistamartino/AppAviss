using System.Security.Cryptography;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Importación de un .xls (spec 002). Es SINGLETON porque la vista previa tiene que vivir entre la pantalla de vista
/// previa y la de confirmar; pero los repositorios y el DbContext son "scoped" (un contexto y una transacción por
/// operación), así que cada operación abre su propio scope con <see cref="IServiceScopeFactory"/>.
///
/// Dos tiempos:
///   PrepararAsync: lee el archivo una sola vez, lo hashea, lo valida y deja una vista previa en memoria. No guarda nada.
///   ConfirmarAsync: copia de seguridad a pedido y, recién entonces, UNA transacción con establecimiento, rodeo y animales.
/// </summary>
internal sealed class ImportacionService : IImportacionService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IExcelProfile _perfil;
    private readonly IBackupService _backup;
    private readonly IRelojService _reloj;

    // Una sola vista previa a la vez, con estado atómico (Pendiente / Confirmando / Consumida): resuelve el doble toque (RF-19).
    private readonly AlmacenVistasPrevias _almacen = new();

    // SQLite admite un solo escritor: las confirmaciones van de a una para no chocar con "base ocupada" (RF-20).
    private readonly SemaphoreSlim _enSerie = new(1, 1);

    public ImportacionService(IServiceScopeFactory scopes, IExcelProfile perfil, IBackupService backup, IRelojService reloj)
        => (_scopes, _perfil, _backup, _reloj) = (scopes, perfil, backup, reloj);

    // ------------------------------------------------------------------ Preparar

    public async Task<Resultado<VistaPreviaImportacion>> PrepararAsync(string nombreArchivo, Stream contenido, CancellationToken ct = default)
    {
        // 1. Tamaño primero (RF-2): se lee hasta 25 MB + 1 byte; si hay un byte de más, el archivo se pasó y no se sigue.
        var bytes = await LeerConTopeAsync(contenido, ct);
        if (bytes is null)
            return Rechazo(MotivoRechazoArchivo.Excede25Mb);

        // La huella se calcula sobre los MISMOS bytes que se leyeron y que se van a guardar (RF-1).
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        IReadOnlyList<EstadoReproductivo> estados;
        IReadOnlyList<RodeoImportadoResumen> repetidos;
        using (var scope = _scopes.CreateScope())
        {
            estados = await scope.ServiceProvider.GetRequiredService<IEstadoReproductivoRepository>().ListarAsync(ct);
            repetidos = await scope.ServiceProvider.GetRequiredService<IRodeoRepository>().BuscarPorHashAsync(hash, ct);
        }

        // 2. Formato, encabezados y límites (RF-2, RF-3). Es trabajo de CPU: se hace fuera del hilo de la pantalla.
        var validacion = await Task.Run(() =>
        {
            var lectura = _perfil.Leer(bytes, LimitesImportacion.MaxFilas);
            return lectura.EsCorrecto
                ? (Rechazo: (MotivoRechazoArchivo?)null, Mensaje: (string?)null, Validacion: ValidadorImportacion.Validar(lectura.Lectura!, estados))
                : (lectura.Rechazo, lectura.Mensaje, null);
        }, ct);

        if (validacion.Rechazo is not null)
            return Resultado.Error<VistaPreviaImportacion>(CodigoError.ArchivoRechazado, validacion.Mensaje!);
        var v = validacion.Validacion!;
        if (v.Rechazo is { } rechazoValidador)   // 0 filas de datos o demasiadas
            return Rechazo(rechazoValidador);

        var vista = new VistaPreviaImportacion(
            Guid.NewGuid(), NombreSeguro(nombreArchivo), hash, bytes, _perfil.Nombre,
            v.FilasLeidas, v.Validas, v.Excluidas, v.Advertencias, v.ConteoPorEstado, repetidos);

        _almacen.Guardar(vista);   // si había otra vista previa, se descarta (RF-22)
        return Resultado.Ok(vista);
    }

    /// <summary>Lee el flujo entero a memoria. Devuelve nulo si pasa de 25 MB (se corta apenas se pasa).</summary>
    private static async Task<byte[]?> LeerConTopeAsync(Stream flujo, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await flujo.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + leidos > LimitesImportacion.MaxBytes) return null;
            memoria.Write(buffer, 0, leidos);
        }
        return memoria.ToArray();
    }

    private static Resultado<VistaPreviaImportacion> Rechazo(MotivoRechazoArchivo motivo)
        => Resultado.Error<VistaPreviaImportacion>(CodigoError.ArchivoRechazado, MensajesImportacion.Para(motivo));

    /// <summary>Solo el nombre del archivo (sin carpetas); si viene vacío, uno genérico, porque el rodeo lo exige.</summary>
    private static string NombreSeguro(string? nombreArchivo)
    {
        var nombre = Path.GetFileName(nombreArchivo ?? "").Trim();
        return nombre.Length == 0 ? "archivo.xls" : nombre;
    }

    // ------------------------------------------------------------------ Establecimientos

    public async Task<IReadOnlyList<EstablecimientoResumen>> ListarEstablecimientosActivosAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var todos = await scope.ServiceProvider.GetRequiredService<IEstablecimientoRepository>().ListarAsync(ct);
        return todos.Where(e => e.Activo)
            .OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new EstablecimientoResumen(e.Id, e.Nombre))
            .ToList();
    }

    // ------------------------------------------------------------------ Confirmar

    public async Task<Resultado<ResultadoImportacion>> ConfirmarAsync(ConfirmarImportacionCommand comando, CancellationToken ct = default)
    {
        // 1. Los nombres se validan ANTES de tocar la vista previa: un nombre mal escrito no la gasta (se corrige y se reintenta).
        var nombreRodeo = (comando.NombreRodeo ?? "").Trim();
        var errorNombre = ValidarNombre(nombreRodeo, "rodeo");
        if (errorNombre is not null) return Resultado.Error<ResultadoImportacion>(CodigoError.DatosInvalidos, errorNombre);

        string? nombreNuevoEstablecimiento = null;
        if (comando.EstablecimientoId is null)
        {
            nombreNuevoEstablecimiento = (comando.NuevoEstablecimientoNombre ?? "").Trim();
            errorNombre = ValidarNombre(nombreNuevoEstablecimiento, "establecimiento");
            if (errorNombre is not null) return Resultado.Error<ResultadoImportacion>(CodigoError.DatosInvalidos, errorNombre);
        }

        // 2. Pendiente → Confirmando, de forma atómica: solo una confirmación de esta vista puede pasar (RF-19).
        switch (_almacen.Tomar(comando.VistaPreviaId, out var vista))
        {
            case ResultadoToma.NoDisponible: return Error(CodigoError.VistaPreviaNoDisponible);
            case ResultadoToma.EnCurso: return Error(CodigoError.ImportacionEnCurso);
            case ResultadoToma.YaConfirmada: return Error(CodigoError.ImportacionYaConfirmada);
        }

        // De acá en adelante, si algo sale mal la vista vuelve a Pendiente (finally) y el veterinario puede reintentar.
        var consumida = false;
        try
        {
            if (!vista!.PuedeConfirmar) return Error(CodigoError.SinFilasValidas);
            if (vista.EsArchivoRepetido && !comando.ConfirmaImportarArchivoRepetido) return Error(CodigoError.ArchivoRepetidoSinConfirmar);

            await _enSerie.WaitAsync(ct);
            try
            {
                // 3. Copia de seguridad a pedido ANTES de abrir la transacción (RF-17): si falla, no se importa nada.
                var copia = await _backup.CrearAsync(MotivoBackup.APedido, ct);
                if (!copia.EsCorrecto)
                    return Resultado.Error<ResultadoImportacion>(copia.Codigo!.Value, copia.Mensaje!);

                // 4. UNA transacción: establecimiento (si es nuevo) + rodeo + animales (RF-18).
                using var scope = _scopes.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var resultado = await uow.EjecutarAsync(
                    () => GuardarAsync(scope.ServiceProvider, vista, comando.EstablecimientoId, nombreNuevoEstablecimiento, nombreRodeo, ct), ct);

                if (resultado.EsCorrecto)
                {
                    _almacen.Consumir(vista.Id);   // Confirmando → Consumida: libera los bytes de la memoria
                    consumida = true;
                }
                return resultado;
            }
            finally
            {
                _enSerie.Release();
            }
        }
        finally
        {
            // Rechazo de negocio o excepción (disco lleno, base): vuelve a Pendiente. Las excepciones siguen su camino.
            if (!consumida) _almacen.Devolver(comando.VistaPreviaId);
        }
    }

    /// <summary>Todo lo que se guarda, dentro de la transacción. Un rechazo (Resultado de error) revierte todo.</summary>
    private async Task<Resultado<ResultadoImportacion>> GuardarAsync(
        IServiceProvider servicios, VistaPreviaImportacion vista, Guid? establecimientoId, string? nombreNuevo,
        string nombreRodeo, CancellationToken ct)
    {
        var establecimientos = servicios.GetRequiredService<IEstablecimientoRepository>();
        var rodeos = servicios.GetRequiredService<IRodeoRepository>();
        var animales = servicios.GetRequiredService<IAnimalRepository>();
        var ahora = _reloj.UtcAhora;

        Guid idEstablecimiento;
        if (establecimientoId is { } existente)
        {
            // Pudo darse de baja entre la vista previa y ahora (RF-20).
            var est = await establecimientos.ObtenerAsync(existente, ct);
            if (est is null) return Error(CodigoError.NoEncontrado, "El establecimiento no existe.");
            if (!est.Activo) return Error(CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite rodeos nuevos.");
            idEstablecimiento = est.Id;
        }
        else
        {
            // Nombre repetido, sin distinguir mayúsculas, incluidos los dados de baja (RF-15). Dentro de la transacción
            // y con las confirmaciones en serie, dos importaciones no pueden crear el mismo establecimiento.
            var clave = Caravana.CalcularClave(nombreNuevo!);
            var todos = await establecimientos.ListarAsync(ct);
            if (todos.Any(e => Caravana.CalcularClave(e.Nombre) == clave))
                return Error(CodigoError.Duplicado, MensajesImportacion.EstablecimientoRepetido);

            try
            {
                var nuevo = Establecimiento.Crear(nombreNuevo, null, ahora);
                establecimientos.Agregar(nuevo);
                idEstablecimiento = nuevo.Id;
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoImportacion>(ex); }
        }

        try
        {
            // El rodeo guarda los MISMOS bytes que se leyeron y hashearon (RF-1).
            var rodeo = Rodeo.Crear(idEstablecimiento, nombreRodeo, ahora, vista.NombreArchivo, vista.Hash,
                vista.Contenido, vista.PerfilNombre, ahora);
            rodeos.Agregar(rodeo);

            // Sin identificador electrónico ni observaciones (COM queda solo en el JSON): RF-12.
            animales.AgregarVarios(vista.Validas.Select(f =>
                Animal.CrearImportado(rodeo.Id, f.Caravana, null, f.EstadoId, null, f.DatosOriginalesJson, ahora)));

            return Resultado.Ok(new ResultadoImportacion(
                rodeo.Id, rodeo.Nombre, vista.FilasLeidas, vista.Validas.Count, vista.Excluidas.Count, vista.Advertencias));
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<ResultadoImportacion>(ex); }
    }

    // ------------------------------------------------------------------ Descartar

    public void Descartar(Guid vistaPreviaId) => _almacen.Descartar(vistaPreviaId);

    // ------------------------------------------------------------------ Ayudas

    private static Resultado<ResultadoImportacion> Error(CodigoError codigo)
        => Resultado.Error<ResultadoImportacion>(codigo, MensajesImportacion.Para(codigo));

    private static Resultado<ResultadoImportacion> Error(CodigoError codigo, string mensaje)
        => Resultado.Error<ResultadoImportacion>(codigo, mensaje);

    /// <summary>Obligatorio y de hasta 100 caracteres (ya recortado). Mensaje fijo: nunca lleva el nombre escrito (RF-24).</summary>
    private static string? ValidarNombre(string nombreRecortado, string que)
    {
        if (nombreRecortado.Length == 0) return $"El nombre del {que} es obligatorio.";
        if (nombreRecortado.Length > LimitesImportacion.MaxLargoNombre)
            return $"El nombre del {que} no puede superar {LimitesImportacion.MaxLargoNombre} caracteres.";
        return null;
    }
}
