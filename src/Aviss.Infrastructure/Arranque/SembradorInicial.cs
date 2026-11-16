using Aviss.Application.Puertos;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Arranque;

/// <summary>
/// Datos iniciales (RF-6, RF-7b). No van como HasData de EF porque HasData se vuelve a aplicar en cada migración como
/// "estado esperado" y podría pisar o reactivar lo que el veterinario cambió. Acá solo se AGREGA lo que falta.
/// </summary>
public sealed class SembradorInicial
{
    private readonly IRelojService _reloj;

    public SembradorInicial(IRelojService reloj) => _reloj = reloj;

    /// <summary>
    /// Agrega los estados iniciales que falten, buscando por <c>Codigo</c>. No modifica ni reactiva los existentes.
    /// Si el nombre o el código de Dairy Comp de uno inicial ya lo usa otro estado (por ejemplo uno que creó el
    /// veterinario), ese estado inicial se omite: el arranque no puede fallar por una unicidad.
    /// </summary>
    public async Task SembrarEstadosAsync(AvissDbContext ctx, CancellationToken ct = default)
    {
        var existentes = await ctx.Estados.AsNoTracking().ToListAsync(ct);
        var codigos = existentes.Select(e => e.Codigo).ToHashSet(StringComparer.Ordinal);
        var nombres = existentes.Select(e => Caravana.CalcularClave(e.Nombre)).ToHashSet(StringComparer.Ordinal);
        var codigosDairyActivos = existentes.Where(e => e.Activo).Select(e => e.CodigoDairyComp).ToHashSet(StringComparer.Ordinal);

        var ahora = _reloj.UtcAhora;
        foreach (var inicial in EstadosIniciales.Todos)
        {
            if (codigos.Contains(inicial.Codigo)) continue;
            if (nombres.Contains(Caravana.CalcularClave(inicial.Nombre))) continue;
            if (codigosDairyActivos.Contains(inicial.CodigoDairyComp)) continue;

            ctx.Estados.Add(EstadoReproductivo.Crear(inicial.Codigo, inicial.Nombre, inicial.CodigoDairyComp, ahora));
            codigos.Add(inicial.Codigo);
            nombres.Add(Caravana.CalcularClave(inicial.Nombre));
            codigosDairyActivos.Add(inicial.CodigoDairyComp);
        }
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>Crea el usuario "Sin asignar" (Id fijo) si no existe. Solo se llama al crear la base.</summary>
    public async Task SembrarUsuarioSinAsignarAsync(AvissDbContext ctx, CancellationToken ct = default)
    {
        if (await ctx.Usuarios.AnyAsync(u => u.Id == Usuario.SinAsignarId, ct)) return;
        ctx.Usuarios.Add(Usuario.CrearSinAsignar(_reloj.UtcAhora));
        await ctx.SaveChangesAsync(ct);
    }
}
