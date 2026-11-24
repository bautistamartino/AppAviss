using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain;
using Aviss.Domain.Catalogo;
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
    private readonly IEstadoReproductivoRepository _estados;
    private readonly ITactoRepository _tactos;
    private readonly IRegistroCampoRepository _registros;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;
    private readonly IRechazoRepository _rechazos;
    private readonly EsperaService _esperas;

    public TrabajoCampoService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos,
        IEstablecimientoRepository establecimientos, IEstadoReproductivoRepository estados,
        ITactoRepository tactos, IRegistroCampoRepository registros, IHistorialRepository historial, IRelojService reloj,
        IRechazoRepository rechazos, EsperaService esperas)
    {
        _uow = uow; _animales = animales; _rodeos = rodeos; _establecimientos = establecimientos;
        _estados = estados; _tactos = tactos; _registros = registros; _historial = historial; _reloj = reloj;
        _rechazos = rechazos; _esperas = esperas;
    }

    public Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => RegistrarNucleoAsync(c, null, ct), ct);

    public Task<Resultado<Guid>> AnularTactoAsync(AnularTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => AnularNucleoAsync(c, null, ct), ct);


    public Task<Resultado<Guid>> RegistrarSoloEventoAsync(RegistrarSoloEventoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(() => RegistrarSoloEventoNucleoAsync(c, null, ct), ct);

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
            // Lo elegido (con sus días y resultado) o, sin pasos, solo el evento.
            var eleccion = c.Eleccion ?? new EleccionEvento(c.Evento);
            if (eleccion.EventoId != c.Evento)
                return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La elección no coincide con el evento.");
            var tacto = await _tactos.ObtenerAsync(c.RegistroId, ct);
            var rechazo = tacto is null ? await _rechazos.ObtenerAsync(c.RegistroId, ct) : null;
            if (tacto is null && rechazo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El registro no existe.");

            var orden = await _registros.SiguienteOrdenAsync(ct);
            Guid animalId;
            if (tacto is not null)
            {
                animalId = tacto.AnimalId;
                var anulacion = await AnularNucleoAsync(new AnularTactoCommand(tacto.Id), orden, ct);
                if (!anulacion.EsCorrecto) return anulacion;
                orden++; // la anulación del tacto gastó el orden
            }
            else
            {
                animalId = rechazo!.AnimalId;
                var anulacion = await AnularRechazoNucleoAsync(new AnularRechazoCommand(rechazo.Id), ct);
                if (!anulacion.EsCorrecto) return anulacion;
            }

            // El tipo del evento elegido decide qué se guarda: Rechazo fijo o un solo evento del catálogo = registro sin cambio de estado;
            // un estado = tacto. Un id que no existe cae en el tacto, que responde "el estado elegido no existe" (y la transacción se revierte).
            var elegido = c.Evento == EventosCampo.Rechazo ? null : await _estados.ObtenerAsync(c.Evento, ct);
            // También van como rechazo un Aborto con resultado Rechazo y el Rechazo fijo (RF-30); el resto de los eventos con pasos, como tacto.
            return eleccion.RegistraComoRechazo || elegido?.Tipo == TipoEvento.SoloEvento
                ? await RegistrarSoloEventoNucleoAsync(new RegistrarSoloEventoCommand(animalId, c.Evento, c.Observaciones, eleccion), orden, ct)
                : await RegistrarNucleoAsync(new RegistrarTactoCommand(animalId, c.Evento, null, c.Observaciones, null, eleccion), orden, ct);
        }, ct);

    // Núcleo de "registrar un solo evento" (spec 005; el Rechazo de la spec 004 es el caso fijo): mismas validaciones que una marca de
    // pasada (animal, rodeo, establecimiento, reloj), sin transacción propia. NO toca el animal (ni su estado ni su fecha de último
    // tacto). Se guarda en la tabla Rechazo: EventoId nulo = el Rechazo fijo; con valor = un evento del catálogo de tipo SoloEvento
    // (el nombre de la tabla quedó de la spec 004).
    private async Task<Resultado<Guid>> RegistrarSoloEventoNucleoAsync(RegistrarSoloEventoCommand c, long? orden, CancellationToken ct)
    {
        var eleccion = c.Eleccion;
        if (eleccion is not null && eleccion.EventoId != c.EventoId)
            return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La elección no coincide con el evento.");
        var hayDatos = eleccion is { DiasPreniez: not null } or { DiasAborto: not null } or { ResultadoAborto: not null };

        EstadoReproductivo? evento = null;
        int? diasAborto = null;
        if (c.EventoId != EventosCampo.Rechazo)
        {
            evento = await _estados.ObtenerAsync(c.EventoId, ct);
            if (evento is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El evento elegido no existe.");
            if (eleccion?.ResultadoAborto == ResultadoAborto.Rechazo)
            {
                // Aborto con resultado Rechazo (RF-30): un único registro con el evento y los días de aborto; no toca el estado.
                if (evento.ClaveEstandar != "ABORTO" || !evento.ListarPasos().Contains(PasoEvento.ResultadoAborto))
                    return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento no tiene resultado de aborto.");
                if (!evento.Activo) return Resultado.Error<Guid>(CodigoError.EstadoDeBaja, "El evento elegido está dado de baja.");
                var errorPasos = ValidarDatosDePasos(evento, eleccion);
                if (errorPasos is var (codigoPasos, mensajePasos)) return Resultado.Error<Guid>(codigoPasos, mensajePasos);
                diasAborto = eleccion.DiasAborto;
            }
            else
            {
                if (evento.Tipo != TipoEvento.SoloEvento)
                    return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento cambia el estado de la vaca: se guarda como tacto.");
                if (!evento.Activo) return Resultado.Error<Guid>(CodigoError.EstadoDeBaja, "El evento elegido está dado de baja.");
                if (hayDatos) return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento no pide días ni resultado.");
            }
        }
        else if (hayDatos)
            return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "El Rechazo no lleva días ni resultado.");

        var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
        if (animal is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El animal no existe.");
        var rodeo = await _rodeos.ObtenerAsync(animal.RodeoId, ct);
        var establecimiento = rodeo is null ? null : await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
        if (rodeo is null || establecimiento is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo del animal no existe.");

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
            var rechazo = Rechazo.Registrar(animal.Id, ahora, ordenRechazo, c.Observaciones, ahora, evento?.Id, diasAborto);
            _rechazos.Agregar(rechazo);
            // El Rechazo fijo historiza su motivo; un evento del catálogo, su nombre (así el historial lo dice aunque después se renombre o desactive).
            _historial.Agregar(evento is null
                ? Historial.Crear("Rechazo", rechazo.Id, "Observaciones", null, rechazo.Observaciones, ahora, AccionHistorial.RechazoCreado)
                : Historial.Crear("Rechazo", rechazo.Id, "Evento", null, evento.Nombre, ahora, AccionHistorial.RechazoCreado));
            // Si la vaca estaba en espera, queda resuelta en esta misma transacción (RF-3).
            await _esperas.ResolverSiHayAsync(animal.Id, rechazo.Id, ahora, ct);
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

        var ultimo = await _registros.ObtenerUltimoVigenteAsync(rechazo.AnimalId, ct);
        if (ultimo is null || ultimo.Tipo != TipoRegistroCampo.Rechazo || ultimo.Id != rechazo.Id)
            return Resultado.Error<Guid>(CodigoError.RechazoNoEsElUltimo, MensajesCampo.Para(CodigoError.RechazoNoEsElUltimo));

        try
        {
            var ahora = _reloj.UtcAhora;
            rechazo.MarcarAnulado(ahora); // el único cambio que la base permite sobre un rechazo guardado
            _historial.Agregar(Historial.Crear("Rechazo", rechazo.Id, "Anulado", "0", "1", ahora, AccionHistorial.RechazoAnulado));
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
        EstadoReproductivo? estadoNuevo;
        Guid? eventoId = null;
        int? diasPreniezInformados = null, diasAborto = null;
        if (c.Eleccion is null)
        {
            estadoNuevo = await _estados.ObtenerAsync(c.EstadoNuevoId, ct);
            if (estadoNuevo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El estado elegido no existe.");
            // Un solo evento (OK, Revisar Nuevamente) no cambia el estado: nunca se guarda como tacto (spec 005, RF-16).
            if (estadoNuevo.Tipo == TipoEvento.SoloEvento)
                return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento no cambia el estado de la vaca: se guarda como evento, no como tacto.");
            // Un evento con pasos (Aborto) o con destino (Preñada fecha previa) necesita su elección: sin ella faltarían los días o
            // el resultado. Se rechaza acá con un error controlado y no se deja a cargo del trigger de la base.
            if (estadoNuevo.ListarPasos().Count > 0 || estadoNuevo.EstadoDestinoId is not null)
                return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Ese evento pide días o resultado: hay que elegirlo con sus pasos.");
        }
        else
        {
            if (c.Eleccion.EventoId != c.EstadoNuevoId)
                return Resultado.Error<Guid>(CodigoError.DatosInvalidos, "La elección no coincide con el evento.");
            var resolucion = await ResolverEleccionAsync(c.Eleccion, ct);
            if (!resolucion.EsCorrecto) return Resultado.Error<Guid>(resolucion.Codigo!.Value, resolucion.Mensaje!);
            (estadoNuevo, eventoId, diasPreniezInformados, diasAborto) = resolucion.Valor;
        }

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
            var tacto = Tacto.Registrar(animal.Id, ahora, ordenTacto, estadoAnterior, estadoNuevo.Id, c.DiasPrenez,
                c.Observaciones, ahora, eventoId, diasPreniezInformados, diasAborto);
            animal.AplicarTacto(estadoNuevo.Id, ahora, ahora);

            _tactos.Agregar(tacto);
            // Exactamente un registro de historial por tacto; los estados van por id, no por nombre.
            _historial.Agregar(Historial.Crear("Tacto", tacto.Id, "Estado", Ayudas.IdATextoONulo(estadoAnterior),
                Ayudas.IdATexto(estadoNuevo.Id), ahora, AccionHistorial.Tacto));
            // Si la vaca estaba en espera, queda resuelta en esta misma transacción (spec 004, RF-3).
            await _esperas.ResolverSiHayAsync(animal.Id, tacto.Id, ahora, ct);
            return Resultado.Ok(tacto.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }

    /// <summary>
    /// Lo que un evento elegido deja guardado en un tacto: el estado final, el evento (solo si es distinto del estado) y los días.
    /// </summary>
    private sealed record ResolucionTacto(EstadoReproductivo Estado, Guid? EventoId, int? DiasPreniez, int? DiasAborto);

    // Resuelve y revalida una elección que se guarda como tacto (RF-24, RF-29): evento activo, pasos exigidos, días en rango,
    // resultado permitido y activo, que no sobren datos. Fecha previa deja a la vaca en su destino (PREÑADA); un Aborto, en el
    // estado del resultado (VACIA, INSEMIN, SECA o FRESCA). Nunca confía en lo que la pantalla ya validó.
    private async Task<Resultado<ResolucionTacto>> ResolverEleccionAsync(EleccionEvento e, CancellationToken ct)
    {
        var evento = await _estados.ObtenerAsync(e.EventoId, ct);
        if (evento is null) return Resultado.Error<ResolucionTacto>(CodigoError.NoEncontrado, "El estado elegido no existe.");
        if (evento.Tipo == TipoEvento.SoloEvento || e.RegistraComoRechazo)
            return Resultado.Error<ResolucionTacto>(CodigoError.DatosInvalidos, "Ese evento no cambia el estado de la vaca: se guarda como evento, no como tacto.");
        if (!evento.Activo) return Resultado.Error<ResolucionTacto>(CodigoError.EstadoDeBaja, "El evento elegido está dado de baja.");
        if (ValidarDatosDePasos(evento, e) is var (codigo, mensaje)) return Resultado.Error<ResolucionTacto>(codigo, mensaje);

        var final = evento;
        if (evento.EstadoDestinoId is Guid destinoId)
        {
            final = await _estados.ObtenerAsync(destinoId, ct);
            if (final is null) return Resultado.Error<ResolucionTacto>(CodigoError.NoEncontrado, "El estado de destino del evento no existe.");
        }
        else if (e.ResultadoAborto is ResultadoAborto resultado)
        {
            var clave = ReglasResultadoAborto.ClaveEstado(resultado);
            var encontrado = (await _estados.ListarAsync(ct)).FirstOrDefault(x => x.ClaveEstandar == clave);
            final = encontrado is null ? null : await _estados.ObtenerAsync(encontrado.Id, ct);
            if (final is null) return Resultado.Error<ResolucionTacto>(CodigoError.NoEncontrado, "El estado del resultado elegido no existe.");
        }
        if (!final.Activo) return Resultado.Error<ResolucionTacto>(CodigoError.EstadoDeBaja, "El estado en que queda la vaca está dado de baja.");

        // El evento solo se guarda aparte cuando es distinto del estado en que queda la vaca (en lo viejo queda nulo).
        return Resultado.Ok(new ResolucionTacto(final, final.Id == evento.Id ? null : evento.Id, e.DiasPreniez, e.DiasAborto));
    }

    /// <summary>Los datos de la elección deben coincidir con los pasos que el evento declara: ni faltan ni sobran, y los días van en rango.</summary>
    private static (CodigoError, string)? ValidarDatosDePasos(EstadoReproductivo evento, EleccionEvento e)
    {
        var pasos = evento.ListarPasos();

        if (pasos.Contains(PasoEvento.DiasPreniez))
        {
            if (e.DiasPreniez is not int d || !ValidadorDias.EnRango(d, RangosDias.Preniez))
                return (CodigoError.DatosInvalidos, $"Los días de preñez tienen que estar entre {RangosDias.Preniez.Minimo} y {RangosDias.Preniez.Maximo}.");
        }
        else if (e.DiasPreniez is not null) return (CodigoError.DatosInvalidos, "Ese evento no pide días de preñez.");

        if (pasos.Contains(PasoEvento.DiasAborto))
        {
            if (e.DiasAborto is not int d || !ValidadorDias.EnRango(d, RangosDias.Aborto))
                return (CodigoError.DatosInvalidos, $"Los días de aborto tienen que estar entre {RangosDias.Aborto.Minimo} y {RangosDias.Aborto.Maximo}.");
        }
        else if (e.DiasAborto is not null) return (CodigoError.DatosInvalidos, "Ese evento no pide días de aborto.");

        if (pasos.Contains(PasoEvento.ResultadoAborto))
        {
            if (e.ResultadoAborto is not ResultadoAborto r || !Enum.IsDefined(r))
                return (CodigoError.DatosInvalidos, "Falta elegir el resultado del aborto.");
        }
        else if (e.ResultadoAborto is not null) return (CodigoError.DatosInvalidos, "Ese evento no pide resultado de aborto.");

        return null;
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


        try
        {
            var ahora = _reloj.UtcAhora;
            var anterior = await _tactos.ObtenerVigenteAnteriorAsync(animal.Id, tacto.Id, ct);
            var estadoActual = animal.EstadoReproductivoId;
            var ordenAnulacion = orden ?? await _registros.SiguienteOrdenAsync(ct);

            var anulacion = Tacto.CrearAnulacion(tacto, estadoActual, ahora, ordenAnulacion, ahora);
            // El animal vuelve al estado anterior del tacto anulado y a la fecha del último vigente anterior (o vacía).
            animal.RevertirTacto(tacto.EstadoAnteriorId, anterior?.FechaHoraUtc, ahora);
            tacto.MarcarAnulado(ahora); // el único cambio que la base permite sobre un tacto guardado

            _tactos.Agregar(anulacion);
            _historial.Agregar(Historial.Crear("Tacto", anulacion.Id, "Estado", Ayudas.IdATextoONulo(estadoActual),
                Ayudas.IdATextoONulo(anulacion.EstadoNuevoId), ahora, AccionHistorial.AnulacionTacto));
            return Resultado.Ok(anulacion.Id);
        }
        catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
    }
}
