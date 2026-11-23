using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Registrar y anular tactos (RF-8, RF-8b, RF-13, RF-14). Cada operación es UNA transacción: se lee, se valida con las reglas
/// puras del dominio, se arman las entidades (animal modificado + tacto + historial) y la UnitOfWork las guarda juntas.
/// </summary>
internal sealed class TrabajoCampoService : ITrabajoCampoService
{
    private readonly IUnitOfWork _uow;
    private readonly IAnimalRepository _animales;
    private readonly IRodeoRepository _rodeos;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IUsuarioRepository _usuarios;
    private readonly IEstadoReproductivoRepository _estados;
    private readonly ITactoRepository _tactos;
    private readonly IRegistroCampoRepository _registros;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;
    private readonly IDispositivoService _dispositivo;
    private readonly IRechazoRepository _rechazos;
    private readonly EsperaService _esperas;

    public TrabajoCampoService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos,
        IEstablecimientoRepository establecimientos, IUsuarioRepository usuarios, IEstadoReproductivoRepository estados,
        ITactoRepository tactos, IRegistroCampoRepository registros, IHistorialRepository historial, IRelojService reloj, IDispositivoService dispositivo,
        IRechazoRepository rechazos, EsperaService esperas)
    {
        _uow = uow; _animales = animales; _rodeos = rodeos; _establecimientos = establecimientos; _usuarios = usuarios;
        _estados = estados; _tactos = tactos; _registros = registros; _historial = historial; _reloj = reloj; _dispositivo = dispositivo;
        _rechazos = rechazos; _esperas = esperas;
    }

    public Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => RegistrarNucleoAsync(c, null, ct), ct);

    public Task<Resultado<Guid>> AnularTactoAsync(AnularTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => AnularNucleoAsync(c, null, ct), ct);

    /// <summary>
    /// Corregir un tacto = anularlo y registrar uno nuevo, dentro de UNA transacción: si el nuevo se rechaza (estado dado de baja,
    /// por ejemplo), la anulación también se revierte y el tacto original sigue vigente. Los dos registros necesitan órdenes
    /// distintos y consecutivos, porque el segundo todavía no ve al primero en la base.
    /// </summary>
    public Task<Resultado<Guid>> CorregirTactoAsync(CorregirTactoCommand c, CancellationToken ct = default)
        => CorregirRegistroAsync(new CorregirRegistroCommand(c.TactoId, c.EstadoNuevoId, c.Observaciones, c.UsuarioId), ct);

    public Task<Resultado<Guid>> RegistrarRechazoAsync(RegistrarRechazoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => RegistrarRechazoNucleoAsync(c, null, ct), ct);

    public Task<Resultado> AnularRechazoAsync(AnularRechazoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync<Resultado>(async () =>
        {
            var r = await AnularRechazoNucleoAsync(c, ct);
            return r.EsCorrecto ? Resultado.Ok() : Resultado.Error(r.Codigo!.Value, r.Mensaje!);
        }, ct);

    /// <summary>
    /// Corregir un registro ("Editar" un movimiento) = anularlo y guardar otro, dentro de UNA transacción: si el nuevo se rechaza
    /// (estado dado de baja, por ejemplo), la anulación también se revierte y el original sigue vigente. El registro puede ser un tacto o
    /// un rechazo, y el evento elegido decide qué se guarda: un estado del catálogo = tacto; <see cref="EventosCampo.Rechazo"/> = rechazo
    /// (así se cambia un tacto por un rechazo y al revés). Anular un tacto crea otro tacto (de anulación) que usa un orden de guardado;
    /// anular un rechazo no crea ningún registro, así que ahí el nuevo toma el orden libre directamente.
    /// </summary>
    public Task<Resultado<Guid>> CorregirRegistroAsync(CorregirRegistroCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var tacto = await _tactos.ObtenerAsync(c.RegistroId, ct);
            var rechazo = tacto is null ? await _rechazos.ObtenerAsync(c.RegistroId, ct) : null;
            if (tacto is null && rechazo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El registro no existe.");

            var orden = await _registros.SiguienteOrdenAsync(ct);
            Guid animalId;
            if (tacto is not null)
            {
                animalId = tacto.AnimalId;
                var anulacion = await AnularNucleoAsync(new AnularTactoCommand(tacto.Id, c.UsuarioId), orden, ct);
                if (!anulacion.EsCorrecto) return anulacion;
                orden++; // la anulación del tacto gastó el orden
            }
            else
            {
                animalId = rechazo!.AnimalId;
                var anulacion = await AnularRechazoNucleoAsync(new AnularRechazoCommand(rechazo.Id, c.UsuarioId), ct);
                if (!anulacion.EsCorrecto) return anulacion;
            }

            return c.Evento == EventosCampo.Rechazo
                ? await RegistrarRechazoNucleoAsync(new RegistrarRechazoCommand(animalId, c.Observaciones, c.UsuarioId), orden, ct)
                : await RegistrarNucleoAsync(new RegistrarTactoCommand(animalId, c.Evento, null, c.Observaciones, c.UsuarioId), orden, ct);
        }, ct);

    // Núcleo de "registrar un rechazo": mismas validaciones que una marca de pasada (animal, rodeo, establecimiento, reloj), sin
    // transacción propia. El rechazo NO toca el animal (ni su estado ni su fecha de último tacto, RF-6).
    private async Task<Resultado<Guid>> RegistrarRechazoNucleoAsync(RegistrarRechazoCommand c, long? orden, CancellationToken ct)
    {
        var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
        if (animal is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El animal no existe.");
        var rodeo = await _rodeos.ObtenerAsync(animal.RodeoId, ct);
        var establecimiento = rodeo is null ? null : await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
        if (rodeo is null || establecimiento is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo del animal no existe.");
        var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
        if (errorUsuario is not null) return Resultado.Error<Guid>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

        var ahora = _reloj.UtcAhora;
        var ultimo = await _registros.ObtenerUltimoVigenteAsync(animal.Id, ct);
        var motivo = ReglasMarcaPasada.Validar(new DatosValidacionMarca(
            AnimalAnulado: animal.Anulado, RodeoActivo: rodeo.Activo, EstablecimientoActivo: establecimiento.Activo,
            FechaHoraUtc: ahora, AhoraUtc: ahora, FechaUltimoRegistroVigenteUtc: ultimo?.FechaHoraUtc));
        if (motivo is MotivoRechazoMarca m)
        {
            var (codigo, mensaje) = MensajesCampo.ParaRechazo(m);
            return Resultado.Error<Guid>(codigo, mensaje);
        }

        try
        {
            var ordenRechazo = orden ?? await _registros.SiguienteOrdenAsync(ct);
            var rechazo = Rechazo.Registrar(animal.Id, ahora, ordenRechazo, c.Observaciones, c.UsuarioId, _dispositivo.ObtenerId(), ahora);
            _rechazos.Agregar(rechazo);
            _historial.Agregar(Historial.Crear("Rechazo", rechazo.Id, "Observaciones", null, rechazo.Observaciones, ahora, c.UsuarioId, AccionHistorial.RechazoCreado));
            // Si la vaca estaba en espera, queda resuelta en esta misma transacción (RF-3).
            await _esperas.ResolverSiHayAsync(animal.Id, rechazo.Id, c.UsuarioId, ahora, ct);
            return Resultado.Ok(rechazo.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }

    // Núcleo de "anular un rechazo": solo el último registro vigente de la vaca (RF-7). Sin transacción propia.
    private async Task<Resultado<Guid>> AnularRechazoNucleoAsync(AnularRechazoCommand c, CancellationToken ct)
    {
        var rechazo = await _rechazos.ObtenerAsync(c.RechazoId, ct);
        if (rechazo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rechazo no existe.");
        if (rechazo.Anulado) return Resultado.Error<Guid>(CodigoError.RechazoYaAnulado, MensajesCampo.Para(CodigoError.RechazoYaAnulado));
        var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
        if (errorUsuario is not null) return Resultado.Error<Guid>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

        var ultimo = await _registros.ObtenerUltimoVigenteAsync(rechazo.AnimalId, ct);
        if (ultimo is null || ultimo.Tipo != TipoRegistroCampo.Rechazo || ultimo.Id != rechazo.Id)
            return Resultado.Error<Guid>(CodigoError.RechazoNoEsElUltimo, MensajesCampo.Para(CodigoError.RechazoNoEsElUltimo));

        try
        {
            var ahora = _reloj.UtcAhora;
            rechazo.MarcarAnulado(c.UsuarioId, ahora); // el único cambio que la base permite sobre un rechazo guardado
            _historial.Agregar(Historial.Crear("Rechazo", rechazo.Id, "Anulado", "0", "1", ahora, c.UsuarioId, AccionHistorial.RechazoAnulado));
            return Resultado.Ok(rechazo.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }

    // Núcleo de "registrar un tacto": valida y deja las entidades listas, sin abrir su propia transacción (la abre quien lo llama).
    // Con "orden" nulo toma el siguiente de la base; al corregir, el que le toca después de la anulación.
    private async Task<Resultado<Guid>> RegistrarNucleoAsync(RegistrarTactoCommand c, long? orden, CancellationToken ct)
    {
        var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
        if (animal is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El animal no existe.");
        var rodeo = await _rodeos.ObtenerAsync(animal.RodeoId, ct);
        var establecimiento = rodeo is null ? null : await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
        if (rodeo is null || establecimiento is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo del animal no existe.");
        var estadoNuevo = await _estados.ObtenerAsync(c.EstadoNuevoId, ct);
        if (estadoNuevo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El estado elegido no existe.");
        var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
        if (errorUsuario is not null) return Resultado.Error<Guid>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

        // Un único "ahora": es la hora del tacto y la que se usa para validarlo (RF-8b).
        var ahora = _reloj.UtcAhora;
        // Desde la spec 003 el "último" es el último registro vigente de CUALQUIER tipo (tacto o marca de pasada), RF-21.
        var ultimo = await _registros.ObtenerUltimoVigenteAsync(animal.Id, ct);

        var motivo = ReglasTacto.Validar(new DatosValidacionTacto(
            AnimalAnulado: animal.Anulado,
            RodeoActivo: rodeo.Activo,
            EstablecimientoActivo: establecimiento.Activo,
            EstadoNuevoActivo: estadoNuevo.Activo,
            EstadoActualDelAnimal: animal.EstadoReproductivoId,
            EstadoAnteriorInformado: c.EstadoAnteriorEsperado,
            FechaHoraUtc: ahora,
            AhoraUtc: ahora,
            FechaUltimoVigenteUtc: ultimo?.FechaHoraUtc,
            DiasPrenez: c.DiasPrenez));
        if (motivo is MotivoRechazoTacto m)
        {
            var (codigo, mensaje) = Ayudas.DeMotivo(m);
            return Resultado.Error<Guid>(codigo, mensaje);
        }

        try
        {
            var estadoAnterior = animal.EstadoReproductivoId; // se toma antes de cambiarlo
            var ordenTacto = orden ?? await _registros.SiguienteOrdenAsync(ct); // orden compartido con las marcas de pasada
            var tacto = Tacto.Registrar(animal.Id, ahora, ordenTacto, estadoAnterior, c.EstadoNuevoId, c.DiasPrenez,
                c.Observaciones, c.UsuarioId, _dispositivo.ObtenerId(), ahora);
            animal.AplicarTacto(c.EstadoNuevoId, ahora, ahora);

            _tactos.Agregar(tacto);
            // Exactamente un registro de historial por tacto; los estados van por id, no por nombre.
            _historial.Agregar(Historial.Crear("Tacto", tacto.Id, "Estado", Ayudas.IdATextoONulo(estadoAnterior),
                Ayudas.IdATexto(c.EstadoNuevoId), ahora, c.UsuarioId, AccionHistorial.Tacto));
            // Si la vaca estaba en espera, queda resuelta en esta misma transacción (spec 004, RF-3).
            await _esperas.ResolverSiHayAsync(animal.Id, tacto.Id, c.UsuarioId, ahora, ct);
            return Resultado.Ok(tacto.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }

    // Núcleo de "anular un tacto": igual que el de registrar, sin transacción propia.
    private async Task<Resultado<Guid>> AnularNucleoAsync(AnularTactoCommand c, long? orden, CancellationToken ct)
    {
        var tacto = await _tactos.ObtenerAsync(c.TactoId, ct);
        if (tacto is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El tacto no existe.");
        if (tacto.Tipo == TipoTacto.Anulacion)
            return Resultado.Error<Guid>(CodigoError.TactoDeAnulacion, "Un tacto de anulación no puede anularse: registrá un tacto nuevo.");
        if (tacto.Anulado)
            return Resultado.Error<Guid>(CodigoError.TactoYaAnulado, "El tacto ya está anulado.");

        var animal = await _animales.ObtenerAsync(tacto.AnimalId, ct);
        if (animal is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El animal del tacto no existe.");
        if (animal.Anulado)
            return Resultado.Error<Guid>(CodigoError.AnimalAnulado, "El animal está anulado: no se pueden anular sus tactos.");

        // Solo se anula el último registro vigente del animal, de cualquier tipo (RF-14, spec 003 RF-26): si después
        // del tacto se guardó una marca de pasada, primero hay que desmarcar.
        var ultimo = await _registros.ObtenerUltimoVigenteAsync(animal.Id, ct);
        if (ultimo is null || ultimo.Tipo != TipoRegistroCampo.Tacto || ultimo.Id != tacto.Id)
            return Resultado.Error<Guid>(CodigoError.TactoNoEsElUltimo, "Solo se puede anular el último registro vigente del animal.");

        var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
        if (errorUsuario is not null) return Resultado.Error<Guid>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

        try
        {
            var ahora = _reloj.UtcAhora;
            var anterior = await _tactos.ObtenerVigenteAnteriorAsync(animal.Id, tacto.Id, ct);
            var estadoActual = animal.EstadoReproductivoId;
            var ordenAnulacion = orden ?? await _registros.SiguienteOrdenAsync(ct);

            var anulacion = Tacto.CrearAnulacion(tacto, estadoActual, ahora, ordenAnulacion, c.UsuarioId, _dispositivo.ObtenerId(), ahora);
            // El animal vuelve al estado anterior del tacto anulado y a la fecha del último vigente anterior (o vacía).
            animal.RevertirTacto(tacto.EstadoAnteriorId, anterior?.FechaHoraUtc, ahora);
            tacto.MarcarAnulado(ahora); // el único cambio que la base permite sobre un tacto guardado

            _tactos.Agregar(anulacion);
            _historial.Agregar(Historial.Crear("Tacto", anulacion.Id, "Estado", Ayudas.IdATextoONulo(estadoActual),
                Ayudas.IdATextoONulo(anulacion.EstadoNuevoId), ahora, c.UsuarioId, AccionHistorial.AnulacionTacto));
            return Resultado.Ok(anulacion.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }
}
