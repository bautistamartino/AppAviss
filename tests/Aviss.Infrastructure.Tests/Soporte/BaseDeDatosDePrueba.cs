using Aviss.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Una base SQLite real en una carpeta temporal única por test, creada con la migración real.
/// Da una conexión "cruda" (sin pasar por la app ni por EF) para golpear la base con SQL directo
/// y comprobar que las reglas valen aunque el intento no venga de la aplicación.
/// </summary>
public sealed class BaseDeDatosDePrueba : IDisposable
{
    private SqliteConnection? _cruda;
    private long _orden;

    public string Directorio { get; }
    public string RutaDb => Path.Combine(Directorio, "aviss.db");
    public FakeReloj Reloj { get; } = new();

    public BaseDeDatosDePrueba(bool migrar = true)
    {
        Directorio = Path.Combine(Path.GetTempPath(), "aviss-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Directorio);
        if (migrar)
        {
            using var ctx = NuevoContexto();
            ctx.Database.Migrate();
        }
    }

    /// <summary>Un contexto de EF con la misma configuración que usará la app (interceptor incluido).</summary>
    public AvissDbContext NuevoContexto()
    {
        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, RutaDb);
        return new AvissDbContext(opciones.Options);
    }

    /// <summary>Conexión directa a la base, salteando EF y la app.</summary>
    public SqliteConnection Cruda
    {
        get
        {
            if (_cruda is null)
            {
                _cruda = new SqliteConnection($"Data Source={RutaDb};Pooling=False");
                _cruda.Open();
                Ejecutar("PRAGMA foreign_keys = ON");
            }
            return _cruda;
        }
    }

    /// <summary>Cierra la conexión directa (y las del pool) para poder copiar, mover o restaurar el archivo; se vuelve a abrir sola al usarla.</summary>
    public void CerrarCruda()
    {
        _cruda?.Dispose();
        _cruda = null;
        SqliteConnection.ClearAllPools();
    }

    public int Ejecutar(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var cmd = Cruda.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    public object? Escalar(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var cmd = Cruda.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public long Contar(string sql, params (string Nombre, object? Valor)[] parametros)
        => Convert.ToInt64(Escalar(sql, parametros));

    public static string Fecha(DateTime utc) => ConvertidorFechaUtc.ATexto(utc);

    // ---- Altas por SQL crudo (solo columnas obligatorias; el resto, por parámetros) ----

    public Guid Establecimiento(string nombre = "Tambo Uno")
    {
        var id = Guid.NewGuid();
        Ejecutar("INSERT INTO Establecimiento (Id, Nombre, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $n, 1, $f, $f)",
            ("$id", id), ("$n", nombre), ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid Rodeo(Guid? establecimientoId = null, string hash = "", int largoBlob = 3, string estado = "Abierto")
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Rodeo (Id, EstablecimientoId, Nombre, FechaImportacionUtc, ArchivoOrigenNombre, ArchivoOrigenHash,
                       ArchivoOrigenBlob, PerfilExcel, Estado, Activo, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $e, 'Rodeo', $f, 'rodeo.xls', $h, $b, 'DairyComp', $est, 1, $f, $f)",
            ("$id", id), ("$e", establecimientoId ?? Establecimiento()), ("$f", Fecha(Reloj.UtcAhora)),
            ("$h", hash.Length == 0 ? new string('a', 64) : hash), ("$b", new byte[largoBlob]), ("$est", estado));
        return id;
    }

    /// <summary>
    /// Un estado del catálogo. Con solo los argumentos de siempre usa las columnas viejas, así sirve también sobre una base
    /// anterior a "EventosDelCatalogo" (sin Tipo, EsPrincipal, Orden ni ClaveEstandar). Con <c>tipo</c>, <c>clave</c>,
    /// <c>principal</c> u <c>orden</c> (spec 005) exige la base nueva.
    /// </summary>
    public Guid Estado(string codigo, string nombre, string codigoDairyComp, bool activo = true,
        string? tipo = null, string? clave = null, bool principal = false, int orden = 0, DateTime? creadoEnUtc = null,
        string? pasos = null, Guid? destinoId = null)
    {
        var id = Guid.NewGuid();
        var creado = Fecha(creadoEnUtc ?? Reloj.UtcAhora);
        if (tipo is null && clave is null && !principal && orden == 0 && pasos is null && destinoId is null)
        {
            Ejecutar("INSERT INTO EstadoReproductivo (Id, Codigo, Nombre, CodigoDairyComp, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $c, $n, $d, $a, $f, $f)",
                ("$id", id), ("$c", codigo), ("$n", nombre), ("$d", codigoDairyComp), ("$a", activo ? 1 : 0), ("$f", creado));
            return id;
        }
        // Las columnas de pasos y destino (spec 005, segunda vuelta) solo se nombran si se piden, así el helper sigue
        // sirviendo sobre una base anterior a "EventosConPasos".
        var conPasos = pasos is not null || destinoId is not null;
        Ejecutar($@"INSERT INTO EstadoReproductivo (Id, Codigo, Nombre, CodigoDairyComp, Activo, Tipo, ClaveEstandar, EsPrincipal, Orden, CreadoEnUtc, ActualizadoEnUtc{(conPasos ? ", Pasos, EstadoDestinoId" : "")})
                   VALUES ($id, $c, $n, $d, $a, $t, $k, $p, $o, $f, $f{(conPasos ? ", $ps, $dest" : "")})",
            ("$id", id), ("$c", codigo), ("$n", nombre), ("$d", codigoDairyComp), ("$a", activo ? 1 : 0),
            ("$t", tipo ?? "Estado"), ("$k", clave), ("$p", principal ? 1 : 0), ("$o", orden), ("$f", creado),
            ("$ps", pasos), ("$dest", destinoId));
        return id;
    }

    /// <summary>
    /// Migra la base hasta la migración indicada (por ejemplo "QuitarVeterinario", la versión anterior a la spec 005 integrada) o hasta la
    /// última si es nulo. Sirve para armar una base de una versión vieja, cargarle datos a mano y después actualizarla.
    /// </summary>
    public void MigrarHasta(string? destino)
    {
        using (var ctx = NuevoContexto())
        {
            var migrador = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(ctx);
            if (destino is null) migrador.Migrate(); else migrador.Migrate(destino);
        }
        // Una conexión cruda abierta antes de migrar puede quedar con el esquema viejo cacheado: se cierra.
        _cruda?.Dispose();
        _cruda = null;
        SqliteConnection.ClearAllPools();
    }

    public Guid Animal(Guid rodeoId, string caravana, string origen = "AltaEnCampo", string? idElectronico = null,
        bool anulado = false, Guid? estadoId = null, string? datosOriginalesJson = null)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, IdElectronico, EstadoReproductivoId, Origen, Anulado, AnuladoEnUtc,
                       DatosOriginalesJson, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $r, $c, $ie, $es, $o, $an, $anf, $j, $f, $f)",
            ("$id", id), ("$r", rodeoId), ("$c", caravana), ("$ie", idElectronico), ("$es", estadoId), ("$o", origen),
            ("$an", anulado ? 1 : 0), ("$anf", anulado ? Fecha(Reloj.UtcAhora) : null),
            ("$j", datosOriginalesJson ?? (origen == "Importado" ? "{}" : null)), ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid Tacto(Guid animalId, Guid? estadoNuevoId, string tipo = "Normal", Guid? tactoAnuladoId = null,
        int? diasPrenez = null, DateTime? fechaHora = null, long? orden = null, bool anulado = false,
        Guid? eventoId = null, int? diasPreniezInformados = null, int? diasAborto = null,
        Guid? evento2Id = null, string? observaciones2 = null)
    {
        var id = Guid.NewGuid();
        // Las columnas de evento y días (spec 005, segunda vuelta) solo se nombran si se piden (base anterior a "EventosConPasos").
        var conEvento = eventoId is not null || diasPreniezInformados is not null || diasAborto is not null;
        // Lo mismo con el evento 2 (spec 011): solo se nombra si se pide (base anterior a "DosEventosPorTacto").
        var conEvento2 = evento2Id is not null || observaciones2 is not null;
        Ejecutar($@"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, DiasPrenez,
                       Tipo, TactoAnuladoId, Anulado, CreadoEnUtc, ActualizadoEnUtc{(conEvento ? ", EventoId, DiasPreniezInformados, DiasAborto" : "")}{(conEvento2 ? ", Evento2Id, Observaciones2" : "")})
                   VALUES ($id, $a, $fh, $o, $en, $d, $t, $ta, $an, $f, $f{(conEvento ? ", $ev, $dp, $da" : "")}{(conEvento2 ? ", $ev2, $ob2" : "")})",
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$o", orden ?? ++_orden),
            ("$en", estadoNuevoId), ("$d", diasPrenez),
            ("$t", tipo), ("$ta", tactoAnuladoId), ("$an", anulado ? 1 : 0), ("$f", Fecha(Reloj.UtcAhora)),
            ("$ev", eventoId), ("$dp", diasPreniezInformados), ("$da", diasAborto),
            ("$ev2", evento2Id), ("$ob2", observaciones2));
        return id;
    }

    // ---- Mangas, cierres y marcas por SQL crudo (spec 003) ----

    public Guid Manga(Guid rodeoId, int numero, DateTime? creadaEn = null)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Manga (Id, RodeoId, Numero, CreadoEnUtc) VALUES ($id, $r, $n, $f)",
            ("$id", id), ("$r", rodeoId), ("$n", numero), ("$f", Fecha(creadaEn ?? Reloj.UtcAhora)));
        return id;
    }

    /// <summary>Un cierre de manga. Si se pasa <c>confirmada</c>, la diferencia se calcula (confirmada - aviss).</summary>
    public Guid CierreManga(Guid mangaId, string tipo = "Manual", int aviss = 0, int? confirmada = null,
        bool anulado = false, DateTime? fechaHora = null)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia,
                       Anulado, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $m, $t, $fh, $a, $c, $df, $an, $f, $f)",
            ("$id", id), ("$m", mangaId), ("$t", tipo), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$a", aviss),
            ("$c", confirmada), ("$df", confirmada is int c ? c - aviss : null),
            ("$an", anulado ? 1 : 0), ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid MarcaPasada(Guid animalId, Guid mangaId, DateTime? fechaHora = null, long? orden = null,
        bool anulada = false)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion,
                       Anulado, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $a, $m, $fh, $o, $an, $f, $f)",
            ("$id", id), ("$a", animalId), ("$m", mangaId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)),
            ("$o", orden ?? ++_orden), ("$an", anulada ? 1 : 0),
            ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    // ---- Esperas y rechazos por SQL crudo (spec 004) ----

    /// <summary>
    /// Una espera. "Pendiente" (por defecto) no lleva datos de resolución; "Resuelta" lleva el registro que la resolvió;
    /// "Salteada" no lleva registro (así lo exige CK_Espera_Resolucion).
    /// </summary>
    public Guid Espera(Guid animalId, string estado = "Pendiente", Guid? registroResolucionId = null,
        DateTime? fechaHora = null)
    {
        var id = Guid.NewGuid();
        var cerrada = estado != "Pendiente";
        Ejecutar(@"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, Estado, ResueltaEnUtc,
                       RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $a, $fh, $e, $re, $rr, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)),
            ("$e", estado), ("$re", cerrada ? Fecha(Reloj.UtcAhora) : null),
            ("$rr", estado == "Resuelta" ? registroResolucionId ?? Guid.NewGuid() : null),
            ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid Rechazo(Guid animalId, string? observaciones = null, DateTime? fechaHora = null,
        long? orden = null, bool anulado = false, Guid? eventoId = null, int? diasAborto = null,
        Guid? evento2Id = null, string? observaciones2 = null)
    {
        var id = Guid.NewGuid();
        var columnaEvento = (eventoId is null ? "" : ", EventoId") + (diasAborto is null ? "" : ", DiasAborto")
            + (evento2Id is null ? "" : ", Evento2Id") + (observaciones2 is null ? "" : ", Observaciones2");
        var valorEvento = (eventoId is null ? "" : ", $ev") + (diasAborto is null ? "" : ", $da")
            + (evento2Id is null ? "" : ", $ev2") + (observaciones2 is null ? "" : ", $ob2");
        var parametros = new List<(string, object?)>
        {
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$o", orden ?? ++_orden),
            ("$ob", observaciones), ("$an", anulado ? 1 : 0),
            ("$ae", anulado ? Fecha(Reloj.UtcAhora) : null),
            ("$f", Fecha(Reloj.UtcAhora)), ("$ev", eventoId), ("$da", diasAborto), ("$ev2", evento2Id), ("$ob2", observaciones2)
        };
        // $ev se manda siempre (un parámetro sin usar no molesta a SQLite); la columna solo se nombra si hay evento, así
        // el helper sigue sirviendo sobre una base anterior a "EventosDelCatalogo".
        Ejecutar($@"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Observaciones,
                       Anulado, AnuladoEnUtc, CreadoEnUtc, ActualizadoEnUtc{columnaEvento})
                   VALUES ($id, $a, $fh, $o, $ob, $an, $ae, $f, $f{valorEvento})",
            parametros.ToArray());
        return id;
    }

    // ---- Constancias de exportación por SQL crudo (spec 007) ----

    public Guid Exportacion(Guid rodeoId, DateTime? fechaUtc = null, string nombre = "rodeo_aviss_20261124_1200.csv",
        string formato = "Csv", string? hash = null, int filas = 10, int conEvento = 4)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Exportacion (Id, RodeoId, FechaUtc, ArchivoNombre, Formato, Hash, CantidadFilas, CantidadVacasConEvento, CreadoEnUtc)
                   VALUES ($id, $r, $fh, $n, $fo, $h, $f, $c, $cr)",
            ("$id", id), ("$r", rodeoId), ("$fh", Fecha(fechaUtc ?? Reloj.UtcAhora)), ("$n", nombre), ("$fo", formato),
            ("$h", hash ?? new string('b', 64)), ("$f", filas), ("$c", conEvento), ("$cr", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public bool ExisteTrigger(string nombre)
        => Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = $n", ("$n", nombre)) == 1;

    public Guid Historial(string accion = "Alta")
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Historial (Id, Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo, FechaHoraUtc, Accion, CreadoEnUtc)
                   VALUES ($id, 'Animal', $ent, 'Alta', NULL, 'x', $f, $a, $f)",
            ("$id", id), ("$ent", Guid.NewGuid()), ("$f", Fecha(Reloj.UtcAhora)), ("$a", accion));
        return id;
    }

    public void Dispose()
    {
        _cruda?.Dispose();
        SqliteConnection.ClearAllPools(); // en Windows el archivo queda bloqueado mientras el pool tenga conexiones
        try { Directory.Delete(Directorio, recursive: true); } catch (IOException) { /* limpieza best-effort */ }
    }
}
