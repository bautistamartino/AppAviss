using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain;
using Aviss.Domain.Entidades;

namespace Aviss.Infrastructure.Servicios;

// Establecimientos, rodeos, usuarios y catálogo de estados. Cada método es una operación: una transacción (UnitOfWork)
// y un Resultado. Los rechazos de negocio no lanzan excepciones; los fallos reales (disco, base) sí, y revierten todo.

internal sealed class EstablecimientoService : IEstablecimientoService
{
    private readonly IUnitOfWork _uow;
    private readonly IEstablecimientoRepository _repo;
    private readonly IRelojService _reloj;

    public EstablecimientoService(IUnitOfWork uow, IEstablecimientoRepository repo, IRelojService reloj)
        => (_uow, _repo, _reloj) = (uow, repo, reloj);

    public Task<Resultado<Guid>> CrearAsync(string nombre, string? notas, CancellationToken ct = default)
        => _uow.EjecutarAsync(() =>
        {
            try
            {
                var e = Establecimiento.Crear(nombre, notas, _reloj.UtcAhora);
                _repo.Agregar(e);
                return Task.FromResult(Resultado.Ok(e.Id));
            }
            catch (DominioException ex) { return Task.FromResult(Ayudas.DatosInvalidos<Guid>(ex)); }
        }, ct);

    public Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default)
        => CambiarAsync(id, (e, ahora) => e.DarDeBaja(ahora), ct);

    public Task<Resultado> ReactivarAsync(Guid id, CancellationToken ct = default)
        => CambiarAsync(id, (e, ahora) => e.Reactivar(ahora), ct);

    public Task<IReadOnlyList<Establecimiento>> ListarAsync(CancellationToken ct = default) => _repo.ListarAsync(ct);

    private Task<Resultado> CambiarAsync(Guid id, Action<Establecimiento, DateTime> cambio, CancellationToken ct)
        => _uow.EjecutarAsync(async () =>
        {
            var e = await _repo.ObtenerAsync(id, ct);
            if (e is null) return Resultado.Error(CodigoError.NoEncontrado, "El establecimiento no existe.");
            cambio(e, _reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);
}

internal sealed class RodeoService : IRodeoService
{
    private readonly IUnitOfWork _uow;
    private readonly IRodeoRepository _rodeos;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IRelojService _reloj;

    public RodeoService(IUnitOfWork uow, IRodeoRepository rodeos, IEstablecimientoRepository establecimientos, IRelojService reloj)
        => (_uow, _rodeos, _establecimientos, _reloj) = (uow, rodeos, establecimientos, reloj);

    public Task<Resultado<Guid>> CrearAsync(CrearRodeoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var est = await _establecimientos.ObtenerAsync(c.EstablecimientoId, ct);
            if (est is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El establecimiento no existe.");
            if (!est.Activo) return Resultado.Error<Guid>(CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite rodeos nuevos.");
            try
            {
                var rodeo = Rodeo.Crear(c.EstablecimientoId, c.Nombre, c.FechaImportacionUtc, c.ArchivoOrigenNombre,
                    c.ArchivoOrigenHash, c.ArchivoOrigenBlob, c.PerfilExcel, _reloj.UtcAhora);
                _rodeos.Agregar(rodeo);
                return Resultado.Ok(rodeo.Id);
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
        }, ct);

    public Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var rodeo = await _rodeos.ObtenerAsync(id, ct);
            if (rodeo is null) return Resultado.Error(CodigoError.NoEncontrado, "El rodeo no existe.");
            if (rodeo.Activo) rodeo.DarDeBaja(_reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);

    // La cantidad no se guarda: se cuenta (RF-2).
    public Task<int> ContarAnimalesAsync(Guid rodeoId, CancellationToken ct = default) => _rodeos.ContarAnimalesAsync(rodeoId, ct);
}

internal sealed class UsuarioService : IUsuarioService
{
    private readonly IUnitOfWork _uow;
    private readonly IUsuarioRepository _repo;
    private readonly IRelojService _reloj;

    public UsuarioService(IUnitOfWork uow, IUsuarioRepository repo, IRelojService reloj)
        => (_uow, _repo, _reloj) = (uow, repo, reloj);

    public Task<Resultado<Guid>> CrearAsync(string nombre, string? matricula, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            try
            {
                var u = Usuario.Crear(nombre, matricula, _reloj.UtcAhora);
                // El nombre no se repite con ningún otro usuario (activo o de baja, "Sin asignar" incluido), sin
                // distinguir mayúsculas ni espacios de los extremos (spec 003, RF-1). La base lo garantiza también
                // con un índice único; este chequeo da el mensaje claro antes de llegar a ese error.
                if (await _repo.ExisteNombreAsync(u.Nombre, ct))
                    return Resultado.Error<Guid>(CodigoError.Duplicado, "Ya hay un veterinario con ese nombre.");
                _repo.Agregar(u);
                return Resultado.Ok(u.Id);
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
        }, ct);

    public Task<Resultado> DarDeBajaAsync(Guid id, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var u = await _repo.ObtenerAsync(id, ct);
            if (u is null) return Resultado.Error(CodigoError.NoEncontrado, "El usuario no existe.");
            // "Sin asignar" no se da de baja si no queda otro usuario activo (RF-7b); además lo impide un trigger.
            if (id == Usuario.SinAsignarId && u.Activo && await _repo.ContarActivosExceptoAsync(id, ct) == 0)
                return Resultado.Error(CodigoError.DatosInvalidos, "Este usuario no se puede dar de baja mientras no haya otro usuario activo.");
            if (u.Activo) u.DarDeBaja(_reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);

    public Task<Resultado> ReactivarAsync(Guid id, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var u = await _repo.ObtenerAsync(id, ct);
            if (u is null) return Resultado.Error(CodigoError.NoEncontrado, "El usuario no existe.");
            if (!u.Activo) u.Reactivar(_reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);
}

internal sealed class CatalogoEstadosService : ICatalogoEstadosService
{
    private readonly IUnitOfWork _uow;
    private readonly IEstadoReproductivoRepository _repo;
    private readonly IRelojService _reloj;

    public CatalogoEstadosService(IUnitOfWork uow, IEstadoReproductivoRepository repo, IRelojService reloj)
        => (_uow, _repo, _reloj) = (uow, repo, reloj);

    public Task<Resultado<Guid>> CrearAsync(string codigo, string nombre, string codigoDairyComp, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            EstadoReproductivo estado;
            try { estado = EstadoReproductivo.Crear(codigo, nombre, codigoDairyComp, _reloj.UtcAhora); }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }

            if (await _repo.ObtenerPorCodigoAsync(estado.Codigo, ct) is not null)
                return Resultado.Error<Guid>(CodigoError.Duplicado, "Ya existe un estado con ese código interno.");
            if (await _repo.ExisteNombreAsync(estado.Nombre, null, ct))
                return Resultado.Error<Guid>(CodigoError.Duplicado, "Ya existe un estado con ese nombre.");
            if (await _repo.ExisteCodigoDairyCompActivoAsync(estado.CodigoDairyComp, null, ct))
                return Resultado.Error<Guid>(CodigoError.CodigoDairyCompDuplicado, "Ya hay un estado activo con ese código de Dairy Comp.");

            _repo.Agregar(estado);
            return Resultado.Ok(estado.Id);
        }, ct);

    public Task<Resultado> ModificarAsync(Guid id, string nombre, string codigoDairyComp, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var estado = await _repo.ObtenerAsync(id, ct);
            if (estado is null) return Resultado.Error(CodigoError.NoEncontrado, "El estado no existe.");
            try { estado.Modificar(nombre, codigoDairyComp, _reloj.UtcAhora); }
            catch (DominioException ex) { return Ayudas.DatosInvalidos(ex); }

            // Se excluye al propio estado: cambiar solo mayúsculas de su nombre no es un repetido.
            if (await _repo.ExisteNombreAsync(estado.Nombre, id, ct))
                return Resultado.Error(CodigoError.Duplicado, "Ya existe un estado con ese nombre.");
            if (estado.Activo && await _repo.ExisteCodigoDairyCompActivoAsync(estado.CodigoDairyComp, id, ct))
                return Resultado.Error(CodigoError.CodigoDairyCompDuplicado, "Ya hay un estado activo con ese código de Dairy Comp.");
            return Resultado.Ok();
        }, ct);

    public Task<Resultado> DesactivarAsync(Guid id, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var estado = await _repo.ObtenerAsync(id, ct);
            if (estado is null) return Resultado.Error(CodigoError.NoEncontrado, "El estado no existe.");
            if (estado.Activo) estado.Desactivar(_reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);

    public Task<Resultado> ActivarAsync(Guid id, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var estado = await _repo.ObtenerAsync(id, ct);
            if (estado is null) return Resultado.Error(CodigoError.NoEncontrado, "El estado no existe.");
            if (estado.Activo) return Resultado.Ok();
            // Al reactivarlo su código de Dairy Comp vuelve a tener que ser único entre los activos.
            if (await _repo.ExisteCodigoDairyCompActivoAsync(estado.CodigoDairyComp, id, ct))
                return Resultado.Error(CodigoError.CodigoDairyCompDuplicado, "Ya hay un estado activo con ese código de Dairy Comp.");
            estado.Activar(_reloj.UtcAhora);
            return Resultado.Ok();
        }, ct);
}
