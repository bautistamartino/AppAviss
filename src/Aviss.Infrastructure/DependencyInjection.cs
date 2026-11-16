using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
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

        // El arranque (migraciones, backups) se registra en la tarea T4.
        return servicios;
    }
}
