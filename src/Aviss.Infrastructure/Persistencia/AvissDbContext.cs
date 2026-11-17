using Aviss.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Persistencia;

public sealed class AvissDbContext : DbContext
{
    public AvissDbContext(DbContextOptions<AvissDbContext> opciones) : base(opciones) { }

    public DbSet<Establecimiento> Establecimientos => Set<Establecimiento>();
    public DbSet<Rodeo> Rodeos => Set<Rodeo>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<EstadoReproductivo> Estados => Set<EstadoReproductivo>();
    public DbSet<Animal> Animales => Set<Animal>();
    public DbSet<Tacto> Tactos => Set<Tacto>();
    public DbSet<Historial> Historiales => Set<Historial>();
    public DbSet<Manga> Mangas => Set<Manga>();
    public DbSet<CierreManga> CierresManga => Set<CierreManga>();
    public DbSet<MarcaPasada> MarcasPasada => Set<MarcaPasada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvissDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Toda fecha (también las que admiten nulo) pasa por el convertidor UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<ConvertidorFechaUtc>();
    }
}
