using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Poner en espera, saltear y listar las vacas en espera (spec 004, RF-1 a RF-4, RF-13, RF-14). La espera NO es un registro de
/// campo: no entra en el orden de guardado ni en el "último registro vigente". Pasa de Pendiente a Resuelta o Salteada una
/// sola vez, y NADA más la cierra (ni el tiempo, ni el día, ni terminar el tacto).
/// Las operaciones públicas abren su propia transacción; <see cref="ResolverSiHayAsync"/> no: la llaman los servicios de tacto y de
/// rechazo DENTRO de su transacción, para que el registro y la resolución de la espera se guarden juntos o no se guarde nada (RF-3).
/// </summary>
internal sealed class EsperaService
{
    private readonly IUnitOfWork _uow;
    private readonly IAnimalRepository _animales;
    private readonly IRodeoRepository _rodeos;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IEsperaRepository _esperas;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;

    public EsperaService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos, IEstablecimientoRepository establecimientos,
        IEsperaRepository esperas, IHistorialRepository historial, IRelojService reloj)
    {
        _uow = uow; _animales = animales; _rodeos = rodeos; _establecimientos = establecimientos;
        _esperas = esperas; _historial = historial; _reloj = reloj;
    }

    public Task<Resultado<Guid>> PonerAsync(PonerEnEsperaCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
            if (animal is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El animal no existe.");
            if (animal.Anulado)
                return Resultado.Error<Guid>(CodigoError.AnimalAnulado, "El animal está anulado y no se puede poner en espera.");
            var rodeo = await _rodeos.ObtenerAsync(animal.RodeoId, ct);
            var establecimiento = rodeo is null ? null : await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
            if (rodeo is null || establecimiento is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo del animal no existe.");
            if (!rodeo.Activo) return Resultado.Error<Guid>(CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no admite esperas.");
            if (!establecimiento.Activo)
                return Resultado.Error<Guid>(CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite esperas.");

            // RF-1: solo una vaca que entra en tacto (CVET distinto de 0) y que todavía no está en espera.
            var entra = ReglasCvet.EntraEnTacto(ConstructorFicha.LeerCvet(animal.DatosOriginalesJson));
            var yaEnEspera = await _esperas.ObtenerPendienteAsync(animal.Id, ct) is not null;
            if (!ReglasEspera.PuedePonerseEnEspera(entra, yaEnEspera))
                return yaEnEspera
                    ? Resultado.Error<Guid>(CodigoError.VacaYaEnEspera, MensajesCampo.Para(CodigoError.VacaYaEnEspera))
                    : Resultado.Error<Guid>(CodigoError.VacaNoEntraEnTacto, MensajesCampo.Para(CodigoError.VacaNoEntraEnTacto));

            try
            {
                var ahora = _reloj.UtcAhora;
                var espera = Espera.Crear(animal.Id, ahora, ahora);
                _esperas.Agregar(espera);
                _historial.Agregar(Historial.Crear("Espera", espera.Id, "Estado", null, nameof(EstadoEspera.Pendiente), ahora, AccionHistorial.EsperaCreada));
                return Resultado.Ok(espera.Id);
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
        }, ct);

    /// <summary>Saltea la espera pendiente de la vaca: no guarda ningún registro de campo (RF-3).</summary>
    public Task<Resultado> SaltearAsync(SaltearEsperaCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {

            var espera = await _esperas.ObtenerPendienteAsync(c.AnimalId, ct);
            if (espera is null) return Resultado.Error(CodigoError.EsperaNoPendiente, MensajesCampo.Para(CodigoError.EsperaNoPendiente));

            try
            {
                var ahora = _reloj.UtcAhora;
                espera.Saltear(ahora);
                _historial.Agregar(Historial.Crear("Espera", espera.Id, "Estado", nameof(EstadoEspera.Pendiente), nameof(EstadoEspera.Salteada),
                    ahora, AccionHistorial.EsperaSalteada));
                return Resultado.Ok();
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos(ex); }
        }, ct);

    public Task<IReadOnlyList<EsperaDto>> ListarAsync(Guid rodeoId, CancellationToken ct = default)
        => _esperas.ListarPendientesAsync(rodeoId, ct);

    /// <summary>
    /// Si la vaca tiene una espera pendiente, la deja resuelta por el tacto o el rechazo recién armado (<paramref name="registroId"/>),
    /// con su registro de historial. NO abre transacción: la abre quien lo llama, así que si el registro se rechaza o algo falla,
    /// la espera sigue pendiente (RF-3).
    /// </summary>
    public async Task ResolverSiHayAsync(Guid animalId, Guid registroId, DateTime ahora, CancellationToken ct)
    {
        var espera = await _esperas.ObtenerPendienteAsync(animalId, ct);
        if (espera is null) return;
        espera.Resolver(registroId, ahora);
        _historial.Agregar(Historial.Crear("Espera", espera.Id, "Estado", nameof(EstadoEspera.Pendiente), nameof(EstadoEspera.Resuelta),
            ahora, AccionHistorial.EsperaResuelta));
    }
}
