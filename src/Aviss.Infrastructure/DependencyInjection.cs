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

        servicios.AddDbContext<AvissDbContext>(opciones => ConfiguracionDb.Configurar(opciones, rutas.BaseDeDatos));
        servicios.AddScoped<IUnitOfWork, UnitOfWork>();

        servicios.AddScoped<IEstablecimientoRepository, EstablecimientoRepository>();
        servicios.AddScoped<IRodeoRepository, RodeoRepository>();
        servicios.AddScoped<IUsuarioRepository, UsuarioRepository>();
        servicios.AddScoped<IEstadoReproductivoRepository, EstadoReproductivoRepository>();
        servicios.AddScoped<IAnimalRepository, AnimalRepository>();
        servicios.AddScoped<ITactoRepository, TactoRepository>();
        servicios.AddScoped<IHistorialRepository, HistorialRepository>();

        servicios.AddScoped<IEstablecimientoService, EstablecimientoService>();
        servicios.AddScoped<IRodeoService, RodeoService>();
        servicios.AddScoped<IUsuarioService, UsuarioService>();
        servicios.AddScoped<ICatalogoEstadosService, CatalogoEstadosService>();
        servicios.AddScoped<IAnimalService, AnimalService>();
        servicios.AddScoped<ITrabajoCampoService, TrabajoCampoService>();

        // Importación: singleton (guarda la vista previa en memoria) que abre un scope por operación. Necesita un IExcelProfile (AddAvissExcel).
        servicios.AddSingleton<IImportacionService, ImportacionService>();

        // Arranque y copias de seguridad: no usan el DbContext del contenedor (trabajan sobre rutas de archivo), así que son singleton.
        servicios.AddSingleton<IEspacioLibreProvider, EspacioLibreProvider>();
        servicios.AddSingleton<IMigrador, MigradorEf>();
        servicios.AddSingleton<SembradorInicial>();
        servicios.AddSingleton<IBackupService, BackupService>();
        servicios.AddSingleton<ArranqueResultado>();
        servicios.AddSingleton<IArranqueResultado>(p => p.GetRequiredService<ArranqueResultado>());
        servicios.AddSingleton<IArranqueService, ArranqueService>();
        return servicios;
    }
}
