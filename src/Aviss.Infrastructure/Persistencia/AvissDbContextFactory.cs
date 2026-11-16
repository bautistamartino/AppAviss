using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aviss.Infrastructure.Persistencia;

/// <summary>
/// Solo para <c>dotnet ef</c> (tiempo de diseño): le dice a la herramienta cómo crear el contexto sin arrancar la app MAUI.
/// La base que usa no se crea ni se toca al generar migraciones.
/// </summary>
public sealed class AvissDbContextFactory : IDesignTimeDbContextFactory<AvissDbContext>
{
    public AvissDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, Path.Combine(Path.GetTempPath(), "aviss-diseno.db"));
        return new AvissDbContext(opciones.Options);
    }
}
