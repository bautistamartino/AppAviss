using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// El migrador REAL (las migraciones de verdad) pero con un interceptor que hace fallar un comando: así se prueba el arranque
/// con una actualización que se corta a mitad de la migración "QuitarVeterinario", sin migraciones inventadas.
/// En cada intento el contador del interceptor arranca de cero.
/// </summary>
public sealed class MigradorConInterceptor : IMigrador
{
    private readonly MigradorEf _real = new();
    private readonly InterceptorDeComandos _interceptor;

    public MigradorConInterceptor(InterceptorDeComandos interceptor) => _interceptor = interceptor;

    public int Intentos { get; private set; }

    public IReadOnlyList<string> Conocidas() => _real.Conocidas();
    public IReadOnlyList<string> Aplicadas(string rutaBase) => _real.Aplicadas(rutaBase);

    public async Task AplicarAsync(string rutaBase, CancellationToken ct = default)
    {
        Intentos++;
        _interceptor.Textos.Clear();
        try
        {
            var opciones = new DbContextOptionsBuilder<AvissDbContext>();
            ConfiguracionDb.Configurar(opciones, rutaBase);
            opciones.AddInterceptors(_interceptor);
            using var ctx = new AvissDbContext(opciones.Options);
            await ctx.Database.MigrateAsync(ct);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
