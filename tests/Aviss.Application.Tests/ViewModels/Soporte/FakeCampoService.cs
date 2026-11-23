using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels.Soporte;

/// <summary>
/// La fachada de campo falsa: cada operación devuelve lo que el test configura (por defecto, algo que funciona) y
/// anota con qué la llamaron. Así los tests de los ViewModels no necesitan base de datos.
/// </summary>
public class FakeCampoService : ICampoService
{
    // ---- configuración ----
    public IReadOnlyList<RodeoParaTrabajarDto> Rodeos { get; set; } = [];
    public IReadOnlyList<VeterinarioDto> Veterinarios { get; set; } = [];
    public IReadOnlyList<EventoCampoDto> Estados { get; set; } = [];
    public IReadOnlyList<MovimientoDto> Movimientos { get; set; } = [];

    public EstadoTrabajoDto Estado { get; set; } = Datos.EstadoTrabajo();
    public Dictionary<string, ResultadoLecturaDto> Lecturas { get; } = [];
    public Dictionary<Guid, FichaAnimalDto> Fichas { get; } = [];

    public Func<ProcesarLecturaCommand, Resultado<ResultadoLecturaDto>>? OnProcesarLectura { get; set; }
    public Func<RegistrarTactoCommand, Resultado<Guid>> OnRegistrarTacto { get; set; } = _ => Resultado.Ok(Guid.NewGuid());
    public Func<DeshacerCommand, Resultado<ResultadoDeshacerDto>>? OnDeshacer { get; set; }
    public Func<Resultado<ResumenMangaDto>> OnResumen { get; set; } = () => Resultado.Ok(Datos.Resumen());
    public Func<FinalizarMangaCommand, Resultado<ResultadoCierreDto>>? OnFinalizar { get; set; }
    public Func<TerminarElTactoCommand, Resultado<ResultadoCierreDto>>? OnTerminar { get; set; }
    public Func<string, Resultado<Guid>>? OnCrearVeterinario { get; set; }
    public string? CaravanaMuestra { get; set; } = "MUESTRA-1";

    // ---- lo que llamaron ----
    public List<(Guid RodeoId, Guid UsuarioId)> IniciosDeTrabajo { get; } = [];
    public List<ProcesarLecturaCommand> LecturasProcesadas { get; } = [];
    public List<RegistrarTactoCommand> Tactos { get; } = [];
    public List<DeshacerCommand> Deshaceres { get; } = [];
    public List<FinalizarMangaCommand> Finalizados { get; } = [];
    public List<TerminarElTactoCommand> Terminados { get; } = [];
    public List<(string Nombre, string? Matricula)> VeterinariosCreados { get; } = [];
    public int ConsultasDeEstado { get; private set; }
    public int ConsultasDeMovimientos { get; private set; }
    public List<Guid> FichasPedidas { get; } = [];

    // ---- ICampoService ----
    public Task<IReadOnlyList<RodeoParaTrabajarDto>> ListarRodeosParaTrabajarAsync(CancellationToken ct = default) => Task.FromResult(Rodeos);
    public virtual Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosActivosAsync(CancellationToken ct = default) => Task.FromResult(Veterinarios);
    /// <summary>Los eventos del fake: <c>Estados</c> es la lista plana; los principales van arriba y el resto en "Otro evento" (con el Rechazo fijo primero, como el servicio real).</summary>
    public Task<ListaEventosDto> ListarEventosAsync(CancellationToken ct = default)
        => Task.FromResult(new ListaEventosDto(
            Estados.Where(e => e.EsPrincipal).ToList(),
            [ArmadorListaEventos.EventoRechazo, .. Estados.Where(e => !e.EsPrincipal)]));

    public List<(string Nombre, string CodigoDairyComp)> EstadosCreados { get; } = [];
    public Func<string, Resultado<Guid>>? OnCrearEstado { get; set; }

    public Task<Resultado<Guid>> CrearEstadoAsync(string nombre, string codigoDairyComp, CancellationToken ct = default)
    {
        EstadosCreados.Add((nombre, codigoDairyComp));
        if (OnCrearEstado is not null) return Task.FromResult(OnCrearEstado(nombre));
        var id = Guid.NewGuid();
        Estados = [.. Estados, new EventoCampoDto(id, nombre.ToUpperInvariant(), true, false)];
        return Task.FromResult(Resultado.Ok(id));
    }

    public Task<Resultado<Guid>> CrearVeterinarioAsync(string nombre, string? matricula, CancellationToken ct = default)
    {
        VeterinariosCreados.Add((nombre, matricula));
        if (OnCrearVeterinario is not null) return Task.FromResult(OnCrearVeterinario(nombre));
        var id = Guid.NewGuid();
        Veterinarios = [.. Veterinarios, new VeterinarioDto(id, nombre, matricula)];
        return Task.FromResult(Resultado.Ok(id));
    }

    public Task<Resultado<EstadoTrabajoDto>> IniciarTrabajoAsync(Guid rodeoId, Guid usuarioId, CancellationToken ct = default)
    {
        IniciosDeTrabajo.Add((rodeoId, usuarioId));
        return Task.FromResult(Resultado.Ok(Estado));
    }

    public Task<Resultado<EstadoTrabajoDto>> ObtenerEstadoTrabajoAsync(Guid rodeoId, CancellationToken ct = default)
    {
        ConsultasDeEstado++;
        return Task.FromResult(Resultado.Ok(Estado));
    }

    public Task<Resultado<ResumenMangaDto>> ObtenerResumenMangaAsync(Guid rodeoId, CancellationToken ct = default) => Task.FromResult(OnResumen());

    public Task<Resultado<ResultadoCierreDto>> FinalizarMangaAsync(FinalizarMangaCommand comando, CancellationToken ct = default)
    {
        Finalizados.Add(comando);
        return Task.FromResult(OnFinalizar?.Invoke(comando) ?? Resultado.Ok(Datos.ResultadoCierre(comando.CantidadReal)));
    }

    public Task<Resultado<ResultadoCierreDto>> TerminarElTactoAsync(TerminarElTactoCommand comando, CancellationToken ct = default)
    {
        Terminados.Add(comando);
        return Task.FromResult(OnTerminar?.Invoke(comando) ?? Resultado.Ok(Datos.ResultadoCierre(comando.CantidadReal, TipoCierreManga.TerminoElTacto)));
    }

    public Task<Resultado<ResultadoLecturaDto>> ProcesarLecturaAsync(ProcesarLecturaCommand comando, CancellationToken ct = default)
    {
        LecturasProcesadas.Add(comando);
        if (OnProcesarLectura is not null) return Task.FromResult(OnProcesarLectura(comando));
        var r = Lecturas.TryGetValue(comando.Codigo, out var dto) ? dto : Datos.NoEncontrada(comando.Codigo);
        return Task.FromResult(Resultado.Ok(r));
    }

    public Task<Resultado<FichaAnimalDto>> ObtenerFichaAsync(Guid animalId, CancellationToken ct = default)
    {
        FichasPedidas.Add(animalId);
        return Task.FromResult(Fichas.TryGetValue(animalId, out var f)
            ? Resultado.Ok(f)
            : Resultado.Error<FichaAnimalDto>(CodigoError.NoEncontrado, "No se encontró la vaca."));
    }

    public Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand comando, CancellationToken ct = default)
    {
        Tactos.Add(comando);
        return Task.FromResult(OnRegistrarTacto(comando));
    }

    public Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(DeshacerCommand comando, CancellationToken ct = default)
    {
        Deshaceres.Add(comando);
        return Task.FromResult(OnDeshacer?.Invoke(comando)
            ?? Resultado.Ok(new ResultadoDeshacerDto(comando.Tipo, null, null, 1, 0)));
    }


    // ---- En Espera y Rechazo (spec 004) ----
    public IReadOnlyList<EsperaDto> Esperas { get; set; } = [];
    public List<PonerEnEsperaCommand> EsperasPuestas { get; } = [];
    public List<SaltearEsperaCommand> EsperasSalteadas { get; } = [];
    public List<RegistrarSoloEventoCommand> Rechazos { get; } = [];
    public List<CorregirRegistroCommand> RegistrosCorregidos { get; } = [];
    public Func<PonerEnEsperaCommand, Resultado<Guid>>? OnPonerEnEspera { get; set; }
    public Func<SaltearEsperaCommand, Resultado>? OnSaltearEspera { get; set; }
    public Func<RegistrarSoloEventoCommand, Resultado<Guid>>? OnRegistrarRechazo { get; set; }
    public Func<CorregirRegistroCommand, Resultado<Guid>>? OnCorregirRegistro { get; set; }
    public int ConsultasDeEsperas { get; private set; }

    public Task<Resultado<Guid>> CorregirRegistroAsync(CorregirRegistroCommand comando, CancellationToken ct = default)
    {
        RegistrosCorregidos.Add(comando);
        return Task.FromResult(OnCorregirRegistro?.Invoke(comando) ?? Resultado.Ok(Guid.NewGuid()));
    }

    public Task<Resultado<Guid>> PonerEnEsperaAsync(PonerEnEsperaCommand comando, CancellationToken ct = default)
    {
        EsperasPuestas.Add(comando);
        return Task.FromResult(OnPonerEnEspera?.Invoke(comando) ?? Resultado.Ok(Guid.NewGuid()));
    }

    public Task<Resultado> SaltearEsperaAsync(SaltearEsperaCommand comando, CancellationToken ct = default)
    {
        EsperasSalteadas.Add(comando);
        return Task.FromResult(OnSaltearEspera?.Invoke(comando) ?? Resultado.Ok());
    }

    public Task<IReadOnlyList<EsperaDto>> ListarEsperasAsync(Guid rodeoId, CancellationToken ct = default)
    {
        ConsultasDeEsperas++;
        return Task.FromResult(Esperas);
    }

    public Task<Resultado<Guid>> RegistrarSoloEventoAsync(RegistrarSoloEventoCommand comando, CancellationToken ct = default)
    {
        Rechazos.Add(comando);
        return Task.FromResult(OnRegistrarRechazo?.Invoke(comando) ?? Resultado.Ok(Guid.NewGuid()));
    }

    public List<Guid> Reseteados { get; } = [];
    public Func<Guid, Resultado<Guid>>? OnResetear { get; set; }

    public Task<Resultado<Guid>> ResetearRodeoAsync(Guid rodeoId, CancellationToken ct = default)
    {
        Reseteados.Add(rodeoId);
        return Task.FromResult(OnResetear?.Invoke(rodeoId) ?? Resultado.Ok(Guid.NewGuid()));
    }

    public virtual Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct = default)
    {
        ConsultasDeMovimientos++;
        return Task.FromResult(Movimientos);
    }

    public Task<string?> CaravanaDeMuestraAsync(Guid rodeoId, MuestraCaravana tipo, CancellationToken ct = default) => Task.FromResult(CaravanaMuestra);
}

/// <summary>Constructores de DTOs con valores razonables para los tests.</summary>
public static class Datos
{
    public static readonly Guid RodeoId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly VeterinarioDto Vet = new(Guid.Parse("22222222-0000-0000-0000-000000000001"), "Dra. Ana", "M-1");

    public static AvanceRodeoDto Avance(int total = 100, int procesadas = 0, DateTime? primera = null, DateTime? fin = null, bool terminado = false)
        => new(new DateOnly(2026, 11, 17), total, procesadas, total - procesadas, total == 0 ? 0 : procesadas * 100 / total, primera, fin, terminado);

    public static EstadoTrabajoDto EstadoTrabajo(int manga = 1, bool abierta = false, int contador = 0, int procesadas = 0, AvanceRodeoDto? avance = null)
        => new(RodeoId, manga, abierta, abierta ? Guid.NewGuid() : null, contador, avance ?? Avance(procesadas: procesadas));

    public static RodeoParaTrabajarDto Rodeo(string establecimiento = "Tambo Norte", string rodeo = "Rodeo 1", Guid? establecimientoId = null, Guid? rodeoId = null)
        => new(establecimientoId ?? Guid.NewGuid(), establecimiento, rodeoId ?? RodeoId, rodeo, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), EstadoRodeo.Abierto, Avance());

    public static FichaAnimalDto Ficha(Guid? animalId = null, string caravana = "1001", string? idElectronico = null, string cvet = "", bool entraEnTacto = true,
        bool enEspera = false, RechazoVigenteDto? rechazoVigente = null)
        => new(animalId ?? Guid.NewGuid(), caravana, idElectronico, null, null, null, [], "", "", [], cvet, entraEnTacto, null, enEspera, rechazoVigente);

    /// <summary>La lectura de una vaca que está en espera: trae su ficha (con el cartel) y no marca ni suma.</summary>
    public static ResultadoLecturaDto EnEspera(Guid animalId, string caravana)
        => new(TipoResultadoLectura.EnEspera, caravana, animalId, caravana, Ficha(animalId, caravana, cvet: "PRE", enEspera: true), null, null, null, null, null, false);

    public static EsperaDto Espera(string caravana, Guid? animalId = null, DateTime? desdeUtc = null, string cvet = "PRE")
        => new(Guid.NewGuid(), animalId ?? Guid.NewGuid(), caravana, cvet, desdeUtc ?? new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc));

    public static ResultadoLecturaDto Marcada(Guid animalId, string caravana, Guid? marcaId = null, int manga = 1, int contador = 1)
        => new(TipoResultadoLectura.Marcada, caravana, animalId, caravana, Ficha(animalId, caravana), marcaId ?? Guid.NewGuid(), manga, contador, null, null, false);

    /// <summary>Una vaca con CVET 0: queda marcada y suma al conteo, pero sin ficha.</summary>
    public static ResultadoLecturaDto SinTacto(Guid animalId, string caravana, Guid? marcaId = null, int manga = 1, int contador = 1)
        => new(TipoResultadoLectura.SinTacto, caravana, animalId, caravana, null, marcaId ?? Guid.NewGuid(), manga, contador, null, null, false);

    public static UltimoRegistroDto Ultimo(TipoRegistroCampo tipo = TipoRegistroCampo.Marca, string? estado = null, int? manga = 1)
        => new(tipo, Guid.NewGuid(), new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc), estado, "Dra. Ana", manga);

    public static ResultadoLecturaDto Repetido(Guid animalId, string caravana, UltimoRegistroDto? ultimo = null, bool puedeCorregir = false)
        => new(TipoResultadoLectura.Repetido, caravana, animalId, caravana, null, null, 1, null, TipoAviso.Repetido, ultimo ?? Ultimo(), puedeCorregir);

    public static ResultadoLecturaDto YaPaso(Guid animalId, string caravana, UltimoRegistroDto? ultimo = null, bool puedeCorregir = false)
        => new(TipoResultadoLectura.YaPaso, caravana, animalId, caravana, null, null, 2, null, TipoAviso.YaPasoOtraManga, ultimo ?? Ultimo(manga: 1), puedeCorregir);

    public static ResultadoLecturaDto NoEncontrada(string codigo)
        => new(TipoResultadoLectura.NoEncontrada, codigo, null, null, null, null, null, null, null, null, false);


    public static ResumenMangaDto Resumen(int manga = 1, params string[] caravanas)
        => new(Guid.NewGuid(), manga, caravanas.Length == 0 ? 3 : caravanas.Length, caravanas.Length == 0 ? ["1", "2", "3"] : caravanas);

    public static ResultadoCierreDto ResultadoCierre(int? confirmada, TipoCierreManga tipo = TipoCierreManga.Manual, int aviss = 3)
        => new(new CierreMangaDto(Guid.NewGuid(), Guid.NewGuid(), 1, tipo, new DateTime(2026, 11, 17, 14, 0, 0, DateTimeKind.Utc),
            aviss, confirmada ?? aviss, (confirmada ?? aviss) - aviss, aviss), Avance(procesadas: aviss));

    public static MovimientoDto Movimiento(TipoMovimiento tipo, bool puedeDeshacer = true, bool anulado = false,
        int? cantidadAviss = null, int? cantidadConfirmada = null, int? contadorActual = null, string? caravana = "1001",
        string? detalle = null, string? cvet = null, string? observaciones = null, Guid? eventoId = null)
        => new(tipo, Guid.NewGuid(), tipo == TipoMovimiento.Cierre ? null : Guid.NewGuid(), caravana, 1, detalle,
            new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc), "Dra. Ana", anulado, puedeDeshacer,
            cantidadAviss, cantidadConfirmada, contadorActual, cvet, observaciones,
            eventoId ?? (tipo == TipoMovimiento.Rechazo ? EventosCampo.Rechazo : null));
}
