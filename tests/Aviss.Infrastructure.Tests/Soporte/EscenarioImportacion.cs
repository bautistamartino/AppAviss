using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain.Catalogo;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Sistema;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Copia de seguridad "de prueba": delega en el BackupService real, pero deja que el test mire (o frene) el momento
/// exacto en que se pide la copia, o la haga fallar.
/// </summary>
public sealed class BackupEspia : IBackupService
{
    private readonly BackupService _real;
    private readonly FakeReloj _reloj;
    public BackupEspia(BackupService real, FakeReloj reloj) => (_real, _reloj) = (real, reloj);

    public List<MotivoBackup> Pedidos { get; } = new();

    /// <summary>Se ejecuta cuando llega el pedido, ANTES de copiar. Si devuelve un resultado, se usa en vez de copiar.</summary>
    public Func<MotivoBackup, Task<Resultado?>>? Antes { get; set; }

    public async Task<Resultado> CrearAsync(MotivoBackup motivo, CancellationToken ct = default)
    {
        Pedidos.Add(motivo);
        if (Antes is not null && await Antes(motivo) is { } reemplazo) return reemplazo;
        var r = await _real.CrearAsync(motivo, ct);
        // El nombre de cada copia lleva la hora: con el reloj quieto dos copias seguidas chocarian. Avanza un segundo.
        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        return r;
    }
}

/// <summary>
/// El contenedor real de la app (AddAvissInfrastructure) sobre una base SQLite de prueba, con un perfil de Excel a elección,
/// reloj falso, espacio libre falso y una copia de seguridad espía. Trae los seis estados del catálogo cargados.
/// </summary>
public sealed class EscenarioImportacion : IDisposable
{
    private readonly ServiceProvider _proveedor;

    public BaseDeDatosDePrueba Base { get; }
    public FakeReloj Reloj => Base.Reloj;
    public FakeEspacioLibre Espacio { get; } = new();
    public BackupEspia Backup { get; }
    public RutasAviss Rutas { get; }

    /// <param name="migrar">false = carpeta vacía: la base la crea el arranque real (con el MigradorEf real).</param>
    public EscenarioImportacion(IExcelProfile perfil, bool migrar = true)
    {
        Base = new BaseDeDatosDePrueba(migrar);
        Rutas = new RutasAviss(Base.Directorio);
        Backup = new BackupEspia(new BackupService(Rutas, Base.Reloj, Espacio), Base.Reloj);

        var servicios = new ServiceCollection();
        servicios.AddAvissInfrastructure(Rutas);
        // La última registración gana: se reemplazan las piezas que el test controla.
        servicios.AddSingleton<IRelojService>(Base.Reloj);
        servicios.AddSingleton<IEspacioLibreProvider>(Espacio);
        servicios.AddSingleton<IBackupService>(Backup);
        servicios.AddSingleton(perfil);
        servicios.AddSingleton<IExcelProfile>(perfil);
        _proveedor = servicios.BuildServiceProvider();

        if (migrar)
            foreach (var e in EstadosIniciales.Todos)
                Base.Estado(e.Codigo, e.Nombre, e.CodigoDairyComp);
    }

    public IImportacionService Importacion => _proveedor.GetRequiredService<IImportacionService>();
    public IArranqueService Arranque => _proveedor.GetRequiredService<IArranqueService>();

    /// <summary>La fachada de campo (spec 003) con un scope nuevo: sirve para leer animales ya importados (spec 010).</summary>
    public ICampoService Campo => _proveedor.CreateScope().ServiceProvider.GetRequiredService<ICampoService>();

    public long Cuenta(string tabla, string? donde = null)
        => Base.Contar($"SELECT COUNT(*) FROM {tabla}" + (donde is null ? "" : " WHERE " + donde));

    public void Dispose()
    {
        _proveedor.Dispose();
        Base.Dispose();
    }
}
