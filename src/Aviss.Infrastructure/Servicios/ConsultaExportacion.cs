using Aviss.Application.Campo;
using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Servicios;

/// <summary>
/// Arma, para un rodeo, los valores que cada vaca lleva en las columnas nuevas del archivo de vuelta (spec 007, RF-6 a RF-12 y RF-25).
/// Sin consultar vaca por vaca (nada de N+1: 20.000 vacas): trae TRES listas por lote (animales, tactos y rechazos del rodeo) más el
/// catálogo, y las une en memoria. Las consultas van dentro de UNA transacción de lectura: con SQLite en WAL eso es una instantánea,
/// así que un tacto que se guarde mientras tanto desde otra pantalla no mezcla momentos, y tampoco frena ese guardado.
/// </summary>
public sealed class ConsultaExportacion : IConsultaExportacion
{
    private readonly AvissDbContext _db;
    private readonly IZonaHoraria _zona;

    public ConsultaExportacion(AvissDbContext db, IZonaHoraria zona)
    {
        _db = db;
        _zona = zona;
    }

    // Lo que se lee de la base, sin tipos anónimos para poder pasarlo entre métodos.
    private sealed record AnimalLeido(Guid Id, string NumeroCaravana, string? DatosOriginalesJson);
    private sealed record TactoLeido(Guid AnimalId, DateTime FechaHoraUtc, long OrdenInsercion, Guid? EstadoNuevoId, Guid? EventoId, int? DiasPreniezInformados, int? DiasAborto);
    private sealed record RechazoLeido(Guid AnimalId, DateTime FechaHoraUtc, long OrdenInsercion, Guid? EventoId, int? DiasAborto);
    private sealed record EstadoLeido(Guid Id, string Nombre, string? ClaveEstandar);
    private sealed record Lectura(List<AnimalLeido> Animales, List<TactoLeido> Tactos, List<RechazoLeido> Rechazos, Dictionary<Guid, EstadoLeido> Estados);

    public async Task<DatosExportacion> ObtenerAsync(Guid rodeoId, CancellationToken ct = default)
    {
        var lectura = await LeerEnUnaInstantaneaAsync(rodeoId, ct);
        var estados = lectura.Estados;

        // ---- Todo lo que sigue es en memoria ----
        var infoAnimal = lectura.Animales.ToDictionary(a => a.Id, a => (
            Clave: Caravana.CalcularClave(a.NumeroCaravana),
            EntraEnTacto: ReglasCvet.EntraEnTacto(ConstructorFicha.LeerCvet(a.DatosOriginalesJson))));

        string NombreDe(Guid? id) => id is Guid g && estados.TryGetValue(g, out var e) ? e.Nombre : "";

        var registros = new List<RegistroExportable>(lectura.Tactos.Count + lectura.Rechazos.Count);
        foreach (var t in lectura.Tactos)
        {
            // El evento es el elegido (Preñada fecha previa, Aborto) o, si no hay, el propio estado en que quedó la vaca.
            // En un Aborto, el resultado es el estado final (Vacía, Inseminada, Seca o Fresca).
            ResultadoAborto? resultado = null;
            if (t.DiasAborto is not null && t.EstadoNuevoId is Guid final && estados.TryGetValue(final, out var estadoFinal))
                resultado = ReglasResultadoAborto.DesdeClaveEstado(estadoFinal.ClaveEstandar);
            registros.Add(new RegistroExportable(infoAnimal[t.AnimalId].Clave, TipoRegistroExportable.Tacto, t.FechaHoraUtc,
                t.OrdenInsercion, NombreDe(t.EventoId ?? t.EstadoNuevoId), t.DiasPreniezInformados, t.DiasAborto, resultado));
        }
        foreach (var r in lectura.Rechazos)
        {
            // Rechazo sin evento = el "Rechazo" fijo de la aplicación (el mismo nombre que usa la ficha).
            var nombre = r.EventoId is null ? ArmadorListaEventos.EventoRechazo.Nombre : NombreDe(r.EventoId);
            // Un Aborto cuyo resultado fue Rechazo guarda sus días en el rechazo: ahí el resultado es siempre Rechazo.
            ResultadoAborto? resultado = r.DiasAborto is not null ? ResultadoAborto.Rechazo : null;
            registros.Add(new RegistroExportable(infoAnimal[r.AnimalId].Clave, TipoRegistroExportable.Rechazo, r.FechaHoraUtc,
                r.OrdenInsercion, nombre, null, r.DiasAborto, resultado));
        }

        var zona = _zona.Zona;
        var jornada = JornadaDeTacto.Filtrar(registros, zona);

        var cvetCero = infoAnimal.Values.Where(a => !a.EntraEnTacto).Select(a => a.Clave).ToHashSet();
        var valores = new Dictionary<string, ValoresExportacion>();
        var cvetCeroConRegistro = 0;
        foreach (var porVaca in jornada.Registros.GroupBy(r => r.CaravanaClave))
        {
            var elegido = SelectorRegistroExportable.Elegir(porVaca);
            if (elegido is null) continue;
            if (cvetCero.Contains(porVaca.Key)) { cvetCeroConRegistro++; continue; }   // CVET 0: sale vacía y se cuenta (RF-11)
            valores[porVaca.Key] = ArmadorValoresExportacion.Armar(elegido, zona);
        }

        return new DatosExportacion(valores, jornada.Dia, lectura.Animales.Count, valores.Count, cvetCeroConRegistro);
    }

    /// <summary>
    /// Las cuatro lecturas dentro de una transacción DIFERIDA. EF abre por defecto un BEGIN IMMEDIATE (pide el bloqueo de escritura),
    /// que frenaría el guardado de un tacto en otra pantalla mientras se exporta (RF-25); un BEGIN diferido solo toma la instantánea
    /// en la primera lectura y deja escribir a los demás. Si alguien ya abrió una transacción en este contexto, se lee dentro de esa.
    /// </summary>
    private async Task<Lectura> LeerEnUnaInstantaneaAsync(Guid rodeoId, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is not null) return await LeerAsync(rodeoId, ct);

        var conexion = (SqliteConnection)_db.Database.GetDbConnection();
        var abrioLaConexion = conexion.State != System.Data.ConnectionState.Open;
        if (abrioLaConexion) await _db.Database.OpenConnectionAsync(ct);   // por EF: así corre el interceptor (WAL, claves foráneas)
        try
        {
            using var transaccion = conexion.BeginTransaction(deferred: true);
            await _db.Database.UseTransactionAsync(transaccion, ct);
            try
            {
                var lectura = await LeerAsync(rodeoId, ct);
                transaccion.Commit();
                return lectura;
            }
            finally
            {
                await _db.Database.UseTransactionAsync(null, ct);
            }
        }
        finally
        {
            if (abrioLaConexion) await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<Lectura> LeerAsync(Guid rodeoId, CancellationToken ct)
    {
        var animales = await _db.Animales.AsNoTracking()
            .Where(a => a.RodeoId == rodeoId && !a.Anulado)
            .Select(a => new AnimalLeido(a.Id, a.NumeroCaravana, a.DatosOriginalesJson))
            .ToListAsync(ct);

        // Solo los tactos Normal vigentes: las anulaciones y los tactos anulados no cuentan (RF-12).
        var tactos = await (from t in _db.Tactos.AsNoTracking()
                            join a in _db.Animales on t.AnimalId equals a.Id
                            where a.RodeoId == rodeoId && !a.Anulado && t.Tipo == TipoTacto.Normal && !t.Anulado
                            select new TactoLeido(t.AnimalId, t.FechaHoraUtc, t.OrdenInsercion, t.EstadoNuevoId, t.EventoId, t.DiasPreniezInformados, t.DiasAborto))
            .ToListAsync(ct);

        var rechazos = await (from r in _db.Rechazos.AsNoTracking()
                              join a in _db.Animales on r.AnimalId equals a.Id
                              where a.RodeoId == rodeoId && !a.Anulado && !r.Anulado
                              select new RechazoLeido(r.AnimalId, r.FechaHoraUtc, r.OrdenInsercion, r.EventoId, r.DiasAborto))
            .ToListAsync(ct);

        var estados = (await _db.Estados.AsNoTracking()
                .Select(e => new EstadoLeido(e.Id, e.Nombre, e.ClaveEstandar))
                .ToListAsync(ct))
            .ToDictionary(e => e.Id);

        return new Lectura(animales, tactos, rechazos, estados);
    }
}
