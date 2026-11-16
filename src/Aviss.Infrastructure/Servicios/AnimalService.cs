using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Infrastructure.Servicios;

/// <summary>Alta en campo, edición y anulación de alta de animales (RF-25, RF-26).</summary>
internal sealed class AnimalService : IAnimalService
{
    private readonly IUnitOfWork _uow;
    private readonly IAnimalRepository _animales;
    private readonly IRodeoRepository _rodeos;
    private readonly IEstablecimientoRepository _establecimientos;
    private readonly IUsuarioRepository _usuarios;
    private readonly IHistorialRepository _historial;
    private readonly IRelojService _reloj;

    public AnimalService(IUnitOfWork uow, IAnimalRepository animales, IRodeoRepository rodeos,
        IEstablecimientoRepository establecimientos, IUsuarioRepository usuarios, IHistorialRepository historial, IRelojService reloj)
        => (_uow, _animales, _rodeos, _establecimientos, _usuarios, _historial, _reloj)
            = (uow, animales, rodeos, establecimientos, usuarios, historial, reloj);

    public Task<Resultado<Guid>> AltaEnCampoAsync(AltaEnCampoCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
            if (errorUsuario is not null) return Resultado.Error<Guid>(errorUsuario.Codigo!.Value, errorUsuario.Mensaje!);

            var rodeo = await _rodeos.ObtenerAsync(c.RodeoId, ct);
            if (rodeo is null) return Resultado.Error<Guid>(CodigoError.NoEncontrado, "El rodeo no existe.");
            if (!rodeo.Activo) return Resultado.Error<Guid>(CodigoError.RodeoDeBaja, "El rodeo está dado de baja y no admite altas.");
            var est = await _establecimientos.ObtenerAsync(rodeo.EstablecimientoId, ct);
            if (est is null || !est.Activo)
                return Resultado.Error<Guid>(CodigoError.EstablecimientoDeBaja, "El establecimiento está dado de baja y no admite altas.");

            if (!Caravana.TryCrear(c.NumeroCaravana, out var caravana, out var errorCaravana))
                return Resultado.Error<Guid>(CodigoError.CaravanaInvalida, errorCaravana!);

            string? idElec = null;
            if (!string.IsNullOrWhiteSpace(c.IdElectronico))
            {
                if (!IdElectronico.TryCrear(c.IdElectronico, out var id, out var errorId))
                    return Resultado.Error<Guid>(CodigoError.IdElectronicoInvalido, errorId!);
                idElec = id.Valor;
            }

            if (await _animales.ObtenerPorCaravanaAsync(c.RodeoId, caravana, ct) is not null)
                return Resultado.Error<Guid>(CodigoError.CaravanaDuplicada, "Ya hay un animal con esa caravana en el rodeo.");
            if (idElec is not null && await _animales.ExisteIdElectronicoAsync(c.RodeoId, idElec, null, ct))
                return Resultado.Error<Guid>(CodigoError.IdElectronicoDuplicado, "Ya hay un animal con ese identificador electrónico en el rodeo.");

            var ahora = _reloj.UtcAhora;
            Animal animal;
            try { animal = Animal.CrearAltaEnCampo(c.RodeoId, caravana, idElec, c.Observaciones, ahora); }
            catch (DominioException ex) { return Ayudas.DatosInvalidos<Guid>(ex); }

            _animales.Agregar(animal);
            _historial.Agregar(Historial.Crear("Animal", animal.Id, "Alta", null, animal.NumeroCaravana, ahora, c.UsuarioId, AccionHistorial.Alta));
            return Resultado.Ok(animal.Id);
        }, ct);

    public Task<Resultado> EditarAsync(EditarAnimalCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
            if (animal is null) return Resultado.Error(CodigoError.NoEncontrado, "El animal no existe.");
            if (animal.Anulado) return Resultado.Error(CodigoError.AnimalAnulado, "El animal está anulado y no admite ediciones.");

            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
            if (errorUsuario is not null) return errorUsuario;

            // Los valores nuevos se normalizan igual que al dar de alta.
            if (!Caravana.TryCrear(c.NumeroCaravana, out var caravana, out var errorCaravana))
                return Resultado.Error(CodigoError.CaravanaInvalida, errorCaravana!);
            string? idElec = null;
            if (!string.IsNullOrWhiteSpace(c.IdElectronico))
            {
                if (!IdElectronico.TryCrear(c.IdElectronico, out var id, out var errorId))
                    return Resultado.Error(CodigoError.IdElectronicoInvalido, errorId!);
                idElec = id.Valor;
            }
            var observaciones = Ayudas.TextoONulo(c.Observaciones);

            var cambiaCaravana = !string.Equals(caravana.Valor, animal.NumeroCaravana, StringComparison.Ordinal);
            var cambiaIdElec = !string.Equals(idElec, animal.IdElectronico, StringComparison.Ordinal);
            var cambiaObs = !string.Equals(observaciones, animal.Observaciones, StringComparison.Ordinal);
            if (!cambiaCaravana && !cambiaIdElec && !cambiaObs) return Resultado.Ok(); // nada cambió: no se registra nada

            if (cambiaCaravana)
            {
                if (animal.Origen != OrigenAnimal.AltaEnCampo)
                    return Resultado.Error(CodigoError.DatosInvalidos, "La caravana de un animal importado no se puede cambiar.");
                // Puede "chocar" con el propio animal (solo cambian mayúsculas): eso no es un repetido.
                var otro = await _animales.ObtenerPorCaravanaAsync(animal.RodeoId, caravana, ct);
                if (otro is not null && otro.Id != animal.Id)
                    return Resultado.Error(CodigoError.CaravanaDuplicada, "Ya hay un animal con esa caravana en el rodeo.");
            }
            if (cambiaIdElec && idElec is not null && await _animales.ExisteIdElectronicoAsync(animal.RodeoId, idElec, animal.Id, ct))
                return Resultado.Error(CodigoError.IdElectronicoDuplicado, "Ya hay un animal con ese identificador electrónico en el rodeo.");

            var ahora = _reloj.UtcAhora;
            try
            {
                // Un registro de historial por campo cambiado (RF-10); se anota el valor anterior antes de cambiarlo.
                if (cambiaCaravana)
                {
                    _historial.Agregar(Historial.Crear("Animal", animal.Id, "NumeroCaravana", animal.NumeroCaravana, caravana.Valor, ahora, c.UsuarioId, AccionHistorial.Edicion));
                    animal.CambiarCaravana(caravana, ahora);
                }
                if (cambiaIdElec)
                {
                    _historial.Agregar(Historial.Crear("Animal", animal.Id, "IdElectronico", animal.IdElectronico, idElec, ahora, c.UsuarioId, AccionHistorial.Edicion));
                    animal.CambiarIdElectronico(idElec, ahora);
                }
                if (cambiaObs)
                {
                    _historial.Agregar(Historial.Crear("Animal", animal.Id, "Observaciones", animal.Observaciones, observaciones, ahora, c.UsuarioId, AccionHistorial.Edicion));
                    animal.CambiarObservaciones(observaciones, ahora);
                }
            }
            catch (DominioException ex) { return Ayudas.DatosInvalidos(ex); }
            return Resultado.Ok();
        }, ct);

    public Task<Resultado> AnularAltaAsync(AnularAltaCommand c, CancellationToken ct = default)
        => _uow.EjecutarAsync(async () =>
        {
            var animal = await _animales.ObtenerAsync(c.AnimalId, ct);
            if (animal is null) return Resultado.Error(CodigoError.NoEncontrado, "El animal no existe.");
            if (animal.Origen != OrigenAnimal.AltaEnCampo)
                return Resultado.Error(CodigoError.AnimalImportadoNoSeAnula, "Un animal importado no puede anularse.");
            if (animal.Anulado) return Resultado.Error(CodigoError.AnimalAnulado, "El animal ya está anulado.");

            var errorUsuario = await Ayudas.ValidarUsuarioAsync(_usuarios, c.UsuarioId, ct);
            if (errorUsuario is not null) return errorUsuario;

            if (await _animales.TieneTactosVigentesAsync(animal.Id, ct))
                return Resultado.Error(CodigoError.AnimalConTactosVigentes, "El animal tiene tactos vigentes: anulá primero esos tactos.");

            var ahora = _reloj.UtcAhora;
            try { animal.Anular(ahora); }
            catch (DominioException ex) { return Ayudas.DatosInvalidos(ex); }
            _historial.Agregar(Historial.Crear("Animal", animal.Id, "Anulado", "0", "1", ahora, c.UsuarioId, AccionHistorial.AnulacionAlta));
            return Resultado.Ok();
        }, ct);
}
