using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Repositorios;
using Aviss.Infrastructure.Servicios;
using Aviss.Infrastructure.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aviss.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra todo lo de Infrastructure. Reloj y dispositivo son singleton; el DbContext, los repositorios, la unidad de
    /// trabajo y los servicios son "scoped": cada operación abre su propio scope (un contexto y una transacción).
    /// </summary>
    public static IServiceCollection AddAvissInfrastructure(this IServiceCollection servicios, RutasAviss rutas)
    {
        servicios.AddSingleton(rutas);
        servicios.AddSingleton<IRelojService, RelojSistema>();
        servicios.AddSingleton<IDispositivoService, DispositivoService>();
        // La zona horaria real; la app de pruebas o la plataforma pueden registrar otra antes (TryAdd).
        servicios.TryAddSingleton<IZonaHoraria, ZonaHorariaSistema>();

        servicios.AddDbContext<AvissDbContext>(opciones => ConfiguracionDb.Configurar(opciones, rutas.BaseDeDatos));
        servicios.AddScoped<IUnitOfWork, UnitOfWork>();

        servicios.AddScoped<IEstablecimientoRepository, EstablecimientoRepository>();
        servicios.AddScoped<IRodeoRepository, RodeoRepository>();
        servicios.AddScoped<IUsuarioRepository, UsuarioRepository>();
        servicios.AddScoped<IEstadoReproductivoRepository, EstadoReproductivoRepository>();
        servicios.AddScoped<IAnimalRepository, AnimalRepository>();
        servicios.AddScoped<ITactoRepository, TactoRepository>();
        servicios.AddScoped<IHistorialRepository, HistorialRepository>();
        servicios.AddScoped<IMangaRepository, MangaRepository>();
        servicios.AddScoped<IMarcaPasadaRepository, MarcaPasadaRepository>();
        servicios.AddScoped<IRegistroCampoRepository, RegistroCampoRepository>();

        servicios.AddScoped<IEstablecimientoService, EstablecimientoService>();
        servicios.AddScoped<IRodeoService, RodeoService>();
        servicios.AddScoped<IUsuarioService, UsuarioService>();
        servicios.AddScoped<ICatalogoEstadosService, CatalogoEstadosService>();
        // AnimalService también se pide por su clase: el alta con marca reutiliza su núcleo dentro de su propia transacción.
        servicios.AddScoped<AnimalService>();
        servicios.AddScoped<IAnimalService>(p => p.GetRequiredService<AnimalService>());
        servicios.AddScoped<ITrabajoCampoService, TrabajoCampoService>();

        // Flujo de campo (spec 003): servicios por operación (scoped) y la fachada singleton que los usa (ICampoService).
        servicios.AddScoped<CalculadorAvance>();
        servicios.AddScoped<ArmadorFicha>();
        servicios.AddScoped<Marcador>();
        servicios.AddScoped<MarcaPasadaService>();
        servicios.AddScoped<MangaService>();
        servicios.AddScoped<AltaCampoService>();
        servicios.AddScoped<ConsultaCampoService>();
        servicios.AddSingleton<ICampoService, CampoService>();

        // Importación: singleton (guarda la vista previa en memoria) que abre un scope por operación. Necesita un IExcelProfile (AddAvissExcel).
        servicios.AddSingleton<IImportacionService, ImportacionService>();

        // Arranque y copias de seguridad: no usan el DbContext del contenedor (trabajan sobre rutas de archivo), así que son singleton.
        // TryAdd: si la plataforma (Android, con StatFs) ya registró su propio proveedor antes, ese gana; este es el de respaldo.
        servicios.TryAddSingleton<IEspacioLibreProvider, EspacioLibreProvider>();
        servicios.AddSingleton<IMigrador, MigradorEf>();
        servicios.AddSingleton<SembradorInicial>();
        servicios.AddSingleton<IBackupService, BackupService>();
        servicios.AddSingleton<ArranqueResultado>();
        servicios.AddSingleton<IArranqueResultado>(p => p.GetRequiredService<ArranqueResultado>());
        servicios.AddSingleton<IArranqueService, ArranqueService>();
        return servicios;
    }
}
