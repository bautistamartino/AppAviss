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
    /// Agrega los eventos estándar que falten o adopta uno ya existente (spec 005, RF-19 y RF-20). Para cada estándar:
    /// <list type="number">
    /// <item>si algún estado ya tiene su <c>ClaveEstandar</c>, está listo (no se toca);</item>
    /// <item>si no, se busca un estado SIN clave con el mismo <c>Codigo</c>, o con el mismo nombre, o con el mismo código
    /// de Dairy Comp (sin distinguir mayúsculas, activos o no; si hay varios, el más antiguo) y se lo ADOPTA: recibe
    /// clave, tipo, principal y orden, pero conserva nombre, códigos y si está activo (no se reactiva);</item>
    /// <item>si no hay candidato se crea; y si crear chocara con una unicidad (nombre o código ya tomado por otro), se
    /// omite ese estándar. El arranque no puede fallar por esto.</item>
    /// </list>
    /// Un mismo estado no se adopta para dos estándares. Todo va en un solo <c>SaveChanges</c> (una transacción): están
    /// todos o ninguno, y es idempotente, así que si la app se corta el próximo arranque termina el trabajo.
    /// </summary>
    public async Task SembrarEstadosAsync(AvissDbContext ctx, CancellationToken ct = default)
    {
        // Con seguimiento de cambios: adoptar modifica filas existentes.
        var existentes = await ctx.Estados.ToListAsync(ct);
        var codigos = existentes.Select(e => e.Codigo).ToHashSet(StringComparer.Ordinal);
        var nombres = existentes.Select(e => Caravana.CalcularClave(e.Nombre)).ToHashSet(StringComparer.Ordinal);
        var codigosDairyActivos = existentes.Where(e => e.Activo).Select(e => Caravana.CalcularClave(e.CodigoDairyComp.Trim()))
            .ToHashSet(StringComparer.Ordinal);

        var ahora = _reloj.UtcAhora;
        foreach (var inicial in EstadosIniciales.Todos)
        {
            if (existentes.Any(e => e.ClaveEstandar == inicial.Codigo)) continue;

            var nombreClave = Caravana.CalcularClave(inicial.Nombre);
            var dairyClave = Caravana.CalcularClave(inicial.CodigoDairyComp.Trim());
            var sinClave = existentes.Where(e => e.ClaveEstandar is null).OrderBy(e => e.CreadoEnUtc).ToList();
            var candidato =
                sinClave.FirstOrDefault(e => e.Codigo == inicial.Codigo)
                ?? sinClave.FirstOrDefault(e => Caravana.CalcularClave(e.Nombre) == nombreClave)
                ?? sinClave.FirstOrDefault(e => Caravana.CalcularClave(e.CodigoDairyComp.Trim()) == dairyClave);

            if (candidato is not null)
            {
                candidato.AdoptarComoEstandar(inicial, ahora);
                continue;
            }

            if (codigos.Contains(inicial.Codigo)) continue;
            if (nombres.Contains(nombreClave)) continue;
            if (codigosDairyActivos.Contains(dairyClave)) continue;

            var nuevo = EstadoReproductivo.CrearEstandar(inicial, ahora);
            ctx.Estados.Add(nuevo);
            existentes.Add(nuevo);
            codigos.Add(inicial.Codigo);
            nombres.Add(nombreClave);
            codigosDairyActivos.Add(dairyClave);
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
