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

    public TrabajoCampoService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos,
        IEstablecimientoRepository establecimientos, IUsuarioRepository usuarios, IEstadoReproductivoRepository estados,
        ITactoRepository tactos, IRegistroCampoRepository registros, IHistorialRepository historial, IRelojService reloj, IDispositivoService dispositivo)
    {
        _uow = uow; _animales = animales; _rodeos = rodeos; _establecimientos = establecimientos; _usuarios = usuarios;
        _estados = estados; _tactos = tactos; _registros = registros; _historial = historial; _reloj = reloj; _dispositivo = dispositivo;
    }

    public Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
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
                var orden = await _registros.SiguienteOrdenAsync(ct); // orden compartido con las marcas de pasada
                var tacto = Tacto.Registrar(animal.Id, ahora, orden, estadoAnterior, c.EstadoNuevoId, c.DiasPrenez,
                    c.Observaciones, c.UsuarioId, _dispositivo.ObtenerId(), ahora);
                animal.AplicarTacto(c.EstadoNuevoId, ahora, ahora);

                _tactos.Agregar(tacto);
                // Exactamente un registro de historial por tacto; los estados van por id, no por nombre.
                _historial.Agregar(Historial.Crear("Tacto", tacto.Id, "Estado", Ayudas.IdATextoONulo(estadoAnterior),
                    Ayudas.IdATexto(c.EstadoNuevoId), ahora, c.UsuarioId, AccionHistorial.Tacto));
                return Resultado.Ok(tacto.Id);
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
        }, ct);

    public Task<Resultado<Guid>> AnularTactoAsync(AnularTactoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
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
                var orden = await _registros.SiguienteOrdenAsync(ct);

                var anulacion = Tacto.CrearAnulacion(tacto, estadoActual, ahora, orden, c.UsuarioId, _dispositivo.ObtenerId(), ahora);
                // El animal vuelve al estado anterior del tacto anulado y a la fecha del último vigente anterior (o vacía).
                animal.RevertirTacto(tacto.EstadoAnteriorId, anterior?.FechaHoraUtc, ahora);
                tacto.MarcarAnulado(ahora); // el único cambio que la base permite sobre un tacto guardado

                _tactos.Agregar(anulacion);
                _historial.Agregar(Historial.Crear("Tacto", anulacion.Id, "Estado", Ayudas.IdATextoONulo(estadoActual),
                    Ayudas.IdATextoONulo(anulacion.EstadoNuevoId), ahora, c.UsuarioId, AccionHistorial.AnulacionTacto));
                return Resultado.Ok(anulacion.Id);
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }
        }, ct);
}
