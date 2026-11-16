using Aviss.Application.Comandos;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Arma el contenedor real de la app (AddAvissInfrastructure) sobre una base SQLite de prueba, con el reloj falso.
/// Cada llamada a <see cref="Servicio{T}"/> abre un scope NUEVO (un DbContext nuevo), igual que hará la app con cada
/// operación. Trae datos base listos (usuario, establecimiento, rodeo y dos estados) creados por SQL crudo.
/// </summary>
public sealed class EscenarioConServicios : IDisposable
{
    private readonly ServiceProvider _proveedor;
    private readonly List<IServiceScope> _scopes = new();

    public BaseDeDatosDePrueba Base { get; }
    public FakeReloj Reloj => Base.Reloj;

    public Guid UsuarioId { get; }
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
        _proveedor = servicios.BuildServiceProvider();

        if (conDatosBase)
        {
            UsuarioId = Base.Usuario("Vet Uno");
            EstablecimientoId = Base.Establecimiento();
            RodeoId = Base.Rodeo(EstablecimientoId);
            EstadoSeca = Base.Estado("SECA", "SECA", "SECA");
            EstadoPrenada = Base.Estado("PRENADA", "PREÑADA", "PREÑADA");
        }
    }

    /// <summary>Un servicio con un scope (y DbContext) nuevo.</summary>
    public T Servicio<T>() where T : notnull
    {
        var scope = _proveedor.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public Guid DispositivoId => _proveedor.GetRequiredService<IDispositivoService>().ObtenerId();

    // ---------- Atajos para armar y leer datos ----------

    /// <summary>Como la base guarda los GUID: texto en mayúsculas.</summary>
    public static string Texto(Guid? id) => id?.ToString().ToUpperInvariant() ?? "";

    public Guid AnimalCampo(string caravana = "100", Guid? estadoId = null, Guid? rodeoId = null)
        => Base.Animal(rodeoId ?? RodeoId, caravana, "AltaEnCampo", estadoId: estadoId);

    public Guid AnimalImportado(string caravana = "200", Guid? estadoId = null, Guid? rodeoId = null)
        => Base.Animal(rodeoId ?? RodeoId, caravana, "Importado", estadoId: estadoId);

    /// <summary>Registra un tacto y exige que salga bien; devuelve el Id del tacto.</summary>
    public async Task<Guid> TactoAsync(Guid animalId, Guid estadoNuevoId, int? dias = null, Guid? usuarioId = null)
    {
        var r = await Servicio<ITrabajoCampoService>()
            .RegistrarTactoAsync(new RegistrarTactoCommand(animalId, estadoNuevoId, dias, null, usuarioId ?? UsuarioId));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    public async Task<Guid> AnularTactoAsync(Guid tactoId, Guid? usuarioId = null)
    {
        var r = await Servicio<ITrabajoCampoService>().AnularTactoAsync(new AnularTactoCommand(tactoId, usuarioId ?? UsuarioId));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

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

    public void Dispose()
    {
        foreach (var s in _scopes) s.Dispose();
        _proveedor.Dispose();
        Base.Dispose();
    }
}
