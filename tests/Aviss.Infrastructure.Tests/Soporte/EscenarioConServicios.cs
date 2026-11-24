using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Arma el contenedor real de la app (AddAvissInfrastructure) sobre una base SQLite de prueba, con el reloj falso.
/// Cada llamada a <see cref="Servicio{T}"/> abre un scope NUEVO (un DbContext nuevo), igual que hará la app con cada
/// operación. Trae datos base listos (establecimiento, rodeo y dos estados) creados por SQL crudo.
/// </summary>
public sealed class EscenarioConServicios : IDisposable
{
    private readonly ServiceProvider _proveedor;
    private readonly List<IServiceScope> _scopes = new();

    public BaseDeDatosDePrueba Base { get; }
    public FakeReloj Reloj => Base.Reloj;
    public FakeZonaHoraria Zona { get; } = new();

    public Guid EstablecimientoId { get; }
    public Guid RodeoId { get; }
    public Guid EstadoSeca { get; }
    public Guid EstadoPrenada { get; }

    public EscenarioConServicios(bool conDatosBase = true)
    {
        Base = new BaseDeDatosDePrueba();
        var servicios = new ServiceCollection();
        servicios.AddAvissInfrastructure(new RutasAviss(Base.Directorio));
        servicios.AddSingleton<IRelojService>(Base.Reloj); // la última registración gana: reemplaza al reloj del sistema
        servicios.AddSingleton<IZonaHoraria>(Zona);
        _proveedor = servicios.BuildServiceProvider();

        if (conDatosBase)
        {
            EstablecimientoId = Base.Establecimiento();
            RodeoId = Base.Rodeo(EstablecimientoId);
            EstadoSeca = Base.Estado("SECA", "SECA", "SECA");
            EstadoPrenada = Base.Estado("PRENADA", "PREÑADA", "PREÑADA");
        }
    }

    /// <summary>Un scope nuevo (con su propio DbContext) para pedir varios servicios que deben compartirlo.</summary>
    public IServiceScope NuevoScope()
    {
        var scope = _proveedor.CreateScope();
        _scopes.Add(scope);
        return scope;
    }

    /// <summary>Un servicio con un scope (y DbContext) nuevo.</summary>
    public T Servicio<T>() where T : notnull
    {
        var scope = _proveedor.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    // ---------- Atajos para armar y leer datos ----------

    /// <summary>Como la base guarda los GUID: texto en mayúsculas.</summary>
    public static string Texto(Guid? id) => id?.ToString().ToUpperInvariant() ?? "";

    public Guid AnimalCampo(string caravana = "100", Guid? estadoId = null, Guid? rodeoId = null)
        => Base.Animal(rodeoId ?? RodeoId, caravana, "AltaEnCampo", estadoId: estadoId);

    public Guid AnimalImportado(string caravana = "200", Guid? estadoId = null, Guid? rodeoId = null, string? json = null)
        => Base.Animal(rodeoId ?? RodeoId, caravana, "Importado", estadoId: estadoId, datosOriginalesJson: json);

    /// <summary>Registra un tacto y exige que salga bien; devuelve el Id del tacto.</summary>
    public async Task<Guid> TactoAsync(Guid animalId, Guid estadoNuevoId, int? dias = null)
    {
        var r = await Servicio<ITrabajoCampoService>()
            .RegistrarTactoAsync(new RegistrarTactoCommand(animalId, estadoNuevoId, dias, null));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    public async Task<Guid> AnularTactoAsync(Guid tactoId)
    {
        var r = await Servicio<ITrabajoCampoService>().AnularTactoAsync(new AnularTactoCommand(tactoId));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    // ---------- Atajos del flujo de campo (spec 003): todo entra por la fachada ICampoService, como en la app ----------

    public ICampoService Campo => Servicio<ICampoService>();

    /// <summary>Lee una caravana y exige que el servicio responda sin error (aunque el resultado sea un aviso o "no encontrada").</summary>
    public async Task<ResultadoLecturaDto> LeerAsync(string codigo, Guid? rodeoId = null)
    {
        var r = await Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(rodeoId ?? RodeoId, codigo));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    /// <summary>Crea la vaca importada y la marca leyéndola; devuelve su Id y el resultado de la lectura.</summary>
    public async Task<(Guid AnimalId, ResultadoLecturaDto Lectura)> VacaMarcadaAsync(string caravana, Guid? estadoId = null)
    {
        var id = AnimalImportado(caravana, estadoId);
        var lectura = await LeerAsync(caravana);
        Assert.Equal(TipoResultadoLectura.Marcada, lectura.Tipo);
        return (id, lectura);
    }

    public async Task<EstadoTrabajoDto> EstadoAsync(Guid? rodeoId = null)
    {
        var r = await Campo.ObtenerEstadoTrabajoAsync(rodeoId ?? RodeoId);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    /// <summary>"Finalizar manga" con "Coincide" (o con la cantidad real indicada); exige que salga bien.</summary>
    public async Task<ResultadoCierreDto> FinalizarAsync(int? cantidadReal = null, Guid? rodeoId = null)
    {
        var r = await Campo.FinalizarMangaAsync(new FinalizarMangaCommand(rodeoId ?? RodeoId, cantidadReal));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    public Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(TipoDeshacer tipo, Guid id)
        => Campo.DeshacerAsync(new DeshacerCommand(tipo, id));

    /// <summary>Hace fallar a propósito el INSERT en Historial de una acción (trigger de la base): para probar que una falla a mitad no deja rastro.</summary>
    public void ForzarFallaAlGuardarHistorial(string accion)
        => Base.Ejecutar($"CREATE TRIGGER TR_Prueba_Falla_{accion} BEFORE INSERT ON Historial WHEN NEW.Accion = '{accion}' BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");

    /// <summary>Valor de una columna por SQL crudo (texto). Nulo si la columna es NULL.</summary>
    public string? Col(string tabla, Guid id, string columna)
        => Base.Escalar($"SELECT {columna} FROM {tabla} WHERE Id = $id", ("$id", id))?.ToString();

    public long Cuenta(string tabla, string? donde = null)
        => Base.Contar($"SELECT COUNT(*) FROM {tabla}" + (donde is null ? "" : " WHERE " + donde));

    /// <summary>Foto de todo lo que un tacto o una anulación pueden tocar, para comprobar que un rechazo no cambió nada.</summary>
    public string Foto()
        => string.Join("|",
            Cuenta("Tacto"), Cuenta("Historial"), Cuenta("Animal"),
            Base.Escalar("SELECT COALESCE(group_concat(Id || ':' || COALESCE(EstadoReproductivoId,'') || ':' || COALESCE(FechaUltimoTactoUtc,'') || ':' || ActualizadoEnUtc || ':' || Anulado), '') FROM (SELECT * FROM Animal ORDER BY Id)"),
            Base.Escalar("SELECT COALESCE(group_concat(Id || ':' || Anulado), '') FROM (SELECT * FROM Tacto ORDER BY Id)"));

    /// <summary>Foto del estado de campo (marcas, mangas, cierres y lo que ya mira <see cref="Foto"/>): un rechazo no debe cambiarla.</summary>
    public string FotoCampo()
        => Foto() + "|" + string.Join("|",
            Cuenta("Manga"), Cuenta("CierreManga"), Cuenta("MarcaPasada"),
            Base.Escalar("SELECT COALESCE(group_concat(Id || ':' || Anulado), '') FROM (SELECT * FROM MarcaPasada ORDER BY Id)"),
            Base.Escalar("SELECT COALESCE(group_concat(Id || ':' || Anulado), '') FROM (SELECT * FROM CierreManga ORDER BY Id)"));

    public void Dispose()
    {
        foreach (var s in _scopes) s.Dispose();
        _proveedor.Dispose();
        Base.Dispose();
    }
}
