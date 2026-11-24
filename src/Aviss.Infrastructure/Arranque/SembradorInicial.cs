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
    /// <summary>Clave del evento cuya aparición marca "la primera vez" de la segunda vuelta de la spec 005 (ver el renombre).</summary>
    private const string ClaveFechaPrevia = "PRENADA_FECHA_PREVIA";
    private const string ClavePrenada = "PRENADA";

    private readonly IRelojService _reloj;

    public SembradorInicial(IRelojService reloj) => _reloj = reloj;

    /// <summary>
    /// Agrega los eventos estándar que falten, adopta uno ya existente y deja los estándar al día (spec 005, RF-19, RF-20,
    /// RF-26). Para cada estándar, en el orden de <see cref="EstadosIniciales.Todos"/>:
    /// <list type="number">
    /// <item>si algún estado ya tiene su <c>ClaveEstandar</c>, se RECONCILIA: tipo, principal, orden, pasos y destino se
    /// igualan a la tabla (no toca nombre, códigos ni si está activo, y no escribe nada si ya coincide);</item>
    /// <item>si no, se busca un estado SIN clave con el mismo <c>Codigo</c>, o con el mismo nombre, o con el mismo código
    /// de Dairy Comp (sin distinguir mayúsculas, activos o no; si hay varios, el más antiguo) y se lo ADOPTA: recibe
    /// clave, tipo, principal, orden, pasos y destino, pero conserva nombre, códigos y si está activo (no se reactiva);</item>
    /// <item>si no hay candidato se crea; y si crear chocara con una unicidad (nombre o código ya tomado por otro), se
    /// omite ese estándar. El arranque no puede fallar por esto.</item>
    /// </list>
    /// RENOMBRE (RF-26): PREÑADA pasa a llamarse "Preñada fecha correcta", aunque el nombre se hubiera cambiado a mano,
    /// salvo que otro evento (activo o no) ya use ese nombre (entonces se omite sin fallar). Se aplica UNA SOLA VEZ por
    /// instalación, en la misma operación que crea (o adopta) "Preñada fecha previa": supuesto del plan (pregunta Q1),
    /// porque si se aplicara en cada arranque pisaría un nombre que el veterinario cambie después.
    /// Un mismo estado no se adopta para dos estándares. Todo va en un solo <c>SaveChanges</c> (una transacción): están
    /// todos o ninguno, y es idempotente, así que si la app se corta el próximo arranque termina el trabajo.
    /// </summary>
    public async Task SembrarEstadosAsync(AvissDbContext ctx, CancellationToken ct = default)
    {
        // Con seguimiento de cambios: adoptar, reconciliar y renombrar modifican filas existentes.
        var existentes = await ctx.Estados.ToListAsync(ct);
        var codigos = existentes.Select(e => e.Codigo).ToHashSet(StringComparer.Ordinal);
        var nombres = existentes.Select(e => Caravana.CalcularClave(e.Nombre)).ToHashSet(StringComparer.Ordinal);
        var codigosDairyActivos = existentes.Where(e => e.Activo).Select(e => Caravana.CalcularClave(e.CodigoDairyComp.Trim()))
            .ToHashSet(StringComparer.Ordinal);

        // "Primera vez" de la segunda vuelta: todavía no hay fila de fecha previa. Se mira ANTES de crearla.
        var primeraVez = !existentes.Any(e => e.ClaveEstandar == ClaveFechaPrevia);

        var ahora = _reloj.UtcAhora;
        var porClave = new Dictionary<string, EstadoReproductivo>(StringComparer.Ordinal);   // los estándar ya resueltos

        foreach (var inicial in EstadosIniciales.Todos)
        {
            // Un evento con destino (fecha previa → PREÑADA) necesita que el destino exista: si no está, se omite sin fallar.
            Guid? destinoId = null;
            if (inicial.DestinoClave is not null)
            {
                if (!porClave.TryGetValue(inicial.DestinoClave, out var destino)) continue;
                destinoId = destino.Id;
            }

            var fila = existentes.FirstOrDefault(e => e.ClaveEstandar == inicial.Codigo);
            if (fila is not null)
            {
                fila.Reconciliar(inicial, destinoId, ahora);
            }
            else
            {
                var nombreClave = Caravana.CalcularClave(inicial.Nombre);
                var dairyClave = Caravana.CalcularClave(inicial.CodigoDairyComp.Trim());
                var sinClave = existentes.Where(e => e.ClaveEstandar is null).OrderBy(e => e.CreadoEnUtc).ToList();
                var candidato =
                    sinClave.FirstOrDefault(e => e.Codigo == inicial.Codigo)
                    ?? sinClave.FirstOrDefault(e => Caravana.CalcularClave(e.Nombre) == nombreClave)
                    ?? sinClave.FirstOrDefault(e => Caravana.CalcularClave(e.CodigoDairyComp.Trim()) == dairyClave);

                if (candidato is not null)
                {
                    candidato.AdoptarComoEstandar(inicial, ahora, destinoId);
                    fila = candidato;
                }
                else if (!codigos.Contains(inicial.Codigo) && !nombres.Contains(nombreClave) && !codigosDairyActivos.Contains(dairyClave))
                {
                    fila = EstadoReproductivo.CrearEstandar(inicial, ahora, destinoId);
                    ctx.Estados.Add(fila);
                    existentes.Add(fila);
                    codigos.Add(inicial.Codigo);
                    nombres.Add(nombreClave);
                    codigosDairyActivos.Add(dairyClave);
                }
                // Si no, crear chocaría con una unicidad: se omite este estándar.
            }

            if (fila is null) continue;
            porClave[inicial.Codigo] = fila;

        }

        // El renombre va DESPUES del bucle y solo si en esta misma pasada se creó (o adoptó) la fila de fecha previa: así queda atado a
        // ese hecho y se hace una sola vez. Si la fila no se pudo crear (choque de nombre o código), no se renombra nada y no se
        // reintenta a ciegas en cada arranque pisando un nombre que el veterinario haya cambiado después.
        if (primeraVez && porClave.ContainsKey(ClaveFechaPrevia) && porClave.TryGetValue(ClavePrenada, out var prenada))
            RenombrarPrenada(prenada, existentes, nombres, ahora);

        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// PREÑADA pasa a "Preñada fecha correcta". Se omite sin fallar si otro evento (activo o no) ya usa ese nombre, con la
    /// misma comparación de repetidos del catálogo (sin distinguir mayúsculas). Conserva Id, clave, códigos y todo lo demás.
    /// </summary>
    private static void RenombrarPrenada(
        EstadoReproductivo fila, List<EstadoReproductivo> existentes, HashSet<string> nombres, DateTime ahora)
    {
        var nuevo = EstadosIniciales.NombrePrenadaFechaCorrecta;
        if (fila.Nombre == nuevo) return;

        var nuevaClave = Caravana.CalcularClave(nuevo);
        if (existentes.Any(e => e != fila && Caravana.CalcularClave(e.Nombre) == nuevaClave)) return;

        nombres.Remove(Caravana.CalcularClave(fila.Nombre));
        fila.Modificar(nuevo, fila.CodigoDairyComp, ahora);
        nombres.Add(nuevaClave);
    }
}
