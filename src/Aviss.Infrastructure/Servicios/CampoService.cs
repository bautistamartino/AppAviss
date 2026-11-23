using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// La fachada del trabajo de campo (<see cref="ICampoService"/>). Es SINGLETON: los ViewModels viven toda la vida de la
/// app y no pueden compartir un DbContext (no es seguro entre hilos y acumula entidades), así que cada operación abre su
/// propio scope (un contexto y una transacción) y delega en los servicios del scope, igual que <c>ImportacionService</c>.
/// </summary>
internal sealed class CampoService : ICampoService
{
    private readonly IServiceScopeFactory _scopes;

    // SQLite admite un solo escritor: las operaciones que escriben van de a una, para no chocar con "base ocupada".
    // El ViewModel ya serializa las lecturas (RF-19); esto protege además del deshacer y los cierres que se disparen a la par.
    private readonly SemaphoreSlim _escritura = new(1, 1);

    public CampoService(IServiceScopeFactory scopes) => _scopes = scopes;

    private async Task<T> LeerAsync<T>(Func<IServiceProvider, Task<T>> operacion)
    {
        using var scope = _scopes.CreateScope();
        return await operacion(scope.ServiceProvider);
    }

    private async Task<T> EscribirAsync<T>(Func<IServiceProvider, Task<T>> operacion, CancellationToken ct)
    {
        await _escritura.WaitAsync(ct);
        try
        {
            using var scope = _scopes.CreateScope();
            return await operacion(scope.ServiceProvider);
        }
        finally { _escritura.Release(); }
    }

    // ---- Preparar el trabajo ----

    public Task<IReadOnlyList<RodeoParaTrabajarDto>> ListarRodeosParaTrabajarAsync(CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ListarRodeosParaTrabajarAsync(ct));

    public Task<IReadOnlyList<VeterinarioDto>> ListarVeterinariosActivosAsync(CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ListarVeterinariosActivosAsync(ct));

    public Task<Resultado<Guid>> CrearVeterinarioAsync(string nombre, string? matricula, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<IUsuarioService>().CrearAsync(nombre, matricula, ct), ct);

    public Task<ListaEventosDto> ListarEventosAsync(CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ListarEventosAsync(ct));

    public Task<Resultado<Guid>> CrearEstadoAsync(string nombre, string codigoDairyComp, CancellationToken ct = default)
    {
        var codigo = Aviss.Domain.Reglas.CodigoEstado.DesdeNombre(nombre);
        if (codigo.Length == 0)
            return Task.FromResult(Resultado.Error<Guid>(CodigoError.DatosInvalidos, "Escribí un nombre con letras o números."));
        return EscribirAsync(p => p.GetRequiredService<ICatalogoEstadosService>().CrearAsync(codigo, nombre, codigoDairyComp, ct), ct);
    }

    // ---- Mangas ----

    public Task<Resultado<EstadoTrabajoDto>> IniciarTrabajoAsync(Guid rodeoId, Guid usuarioId, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<MangaService>().IniciarTrabajoAsync(rodeoId, usuarioId, ct), ct);

    public Task<Resultado<EstadoTrabajoDto>> ObtenerEstadoTrabajoAsync(Guid rodeoId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<MangaService>().ObtenerEstadoAsync(rodeoId, ct));

    public Task<Resultado<ResumenMangaDto>> ObtenerResumenMangaAsync(Guid rodeoId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<MangaService>().ObtenerResumenAsync(rodeoId, ct));

    public Task<Resultado<ResultadoCierreDto>> FinalizarMangaAsync(FinalizarMangaCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<MangaService>().FinalizarAsync(comando, ct), ct);

    public Task<Resultado<ResultadoCierreDto>> TerminarElTactoAsync(TerminarElTactoCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<MangaService>().TerminarAsync(comando, ct), ct);

    // ---- Lectura, ficha y tacto ----

    public Task<Resultado<ResultadoLecturaDto>> ProcesarLecturaAsync(ProcesarLecturaCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<MarcaPasadaService>().ProcesarLecturaAsync(comando, ct), ct);

    public Task<Resultado<FichaAnimalDto>> ObtenerFichaAsync(Guid animalId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ObtenerFichaAsync(animalId, ct));

    public Task<Resultado<Guid>> RegistrarTactoAsync(RegistrarTactoCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<ITrabajoCampoService>().RegistrarTactoAsync(comando, ct), ct);

    // ---- Resetear un rodeo (para pruebas) ----

    public Task<Resultado<Guid>> ResetearRodeoAsync(Guid rodeoId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<IImportacionService>().ResetearRodeoAsync(rodeoId, ct));

    // ---- Deshacer, corregir y movimientos ----

    public Task<Resultado<Guid>> CorregirRegistroAsync(CorregirRegistroCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<ITrabajoCampoService>().CorregirRegistroAsync(comando, ct), ct);

    // ---- En Espera y Rechazo (spec 004) ----

    public Task<Resultado<Guid>> PonerEnEsperaAsync(PonerEnEsperaCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<EsperaService>().PonerAsync(comando, ct), ct);

    public Task<Resultado> SaltearEsperaAsync(SaltearEsperaCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<EsperaService>().SaltearAsync(comando, ct), ct);

    public Task<IReadOnlyList<EsperaDto>> ListarEsperasAsync(Guid rodeoId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ListarEsperasAsync(rodeoId, ct));

    public Task<Resultado<Guid>> RegistrarSoloEventoAsync(RegistrarSoloEventoCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => p.GetRequiredService<ITrabajoCampoService>().RegistrarSoloEventoAsync(comando, ct), ct);

    public Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(DeshacerCommand comando, CancellationToken ct = default)
        => EscribirAsync(p => DeshacerEnScopeAsync(p, comando, ct), ct);

    private static async Task<Resultado<ResultadoDeshacerDto>> DeshacerEnScopeAsync(IServiceProvider p, DeshacerCommand c, CancellationToken ct)
    {
        switch (c.Tipo)
        {
            case TipoDeshacer.Marca:
                return await p.GetRequiredService<MarcaPasadaService>().DeshacerAsync(c.Id, c.UsuarioId, ct);
            case TipoDeshacer.Cierre:
                return await p.GetRequiredService<MangaService>().ReabrirAsync(c.Id, c.UsuarioId, ct);
            case TipoDeshacer.Tacto:
            {
                // La anulación de un tacto ya la resuelve el servicio de tactos (spec 001); acá se agrega el animal al resultado
                // para que la pantalla pueda actualizar su ficha.
                var tacto = await p.GetRequiredService<ITactoRepository>().ObtenerAsync(c.Id, ct);
                var r = await p.GetRequiredService<ITrabajoCampoService>().AnularTactoAsync(new AnularTactoCommand(c.Id, c.UsuarioId), ct);
                if (!r.EsCorrecto) return Resultado.Error<ResultadoDeshacerDto>(r.Codigo!.Value, r.Mensaje!);
                return Resultado.Ok(new ResultadoDeshacerDto(TipoDeshacer.Tacto, tacto?.AnimalId, null, null, null));
            }
            case TipoDeshacer.Rechazo:
            {
                // Igual que el tacto: se agrega el animal al resultado para que la pantalla actualice su ficha.
                var rechazo = await p.GetRequiredService<IRechazoRepository>().ObtenerAsync(c.Id, ct);
                var r = await p.GetRequiredService<ITrabajoCampoService>().AnularRechazoAsync(new AnularRechazoCommand(c.Id, c.UsuarioId), ct);
                if (!r.EsCorrecto) return Resultado.Error<ResultadoDeshacerDto>(r.Codigo!.Value, r.Mensaje!);
                return Resultado.Ok(new ResultadoDeshacerDto(TipoDeshacer.Rechazo, rechazo?.AnimalId, null, null, null));
            }
            default:
                return Resultado.Error<ResultadoDeshacerDto>(CodigoError.DatosInvalidos, "No se sabe qué deshacer.");
        }
    }

    public Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().ListarMovimientosAsync(rodeoId, ct));

    public Task<string?> CaravanaDeMuestraAsync(Guid rodeoId, MuestraCaravana tipo, CancellationToken ct = default)
        => LeerAsync(p => p.GetRequiredService<ConsultaCampoService>().CaravanaDeMuestraAsync(rodeoId, tipo, ct));
}
