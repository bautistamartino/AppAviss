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

    public Guid Usuario(string nombre = "Vet", string? matricula = null, Guid? id = null, bool activo = true)
    {
        var uid = id ?? Guid.NewGuid();
        Ejecutar("INSERT INTO Usuario (Id, Nombre, Matricula, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $n, $m, $a, $f, $f)",
            ("$id", uid), ("$n", nombre), ("$m", matricula), ("$a", activo ? 1 : 0), ("$f", Fecha(Reloj.UtcAhora)));
        return uid;
    }

    /// <summary>
    /// Un estado del catálogo. Con solo los argumentos de siempre usa las columnas viejas, así sirve también sobre una base
    /// anterior a "EventosDelCatalogo" (sin Tipo, EsPrincipal, Orden ni ClaveEstandar). Con <c>tipo</c>, <c>clave</c>,
    /// <c>principal</c> u <c>orden</c> (spec 005) exige la base nueva.
    /// </summary>
    public Guid Estado(string codigo, string nombre, string codigoDairyComp, bool activo = true,
        string? tipo = null, string? clave = null, bool principal = false, int orden = 0, DateTime? creadoEnUtc = null)
    {
        var id = Guid.NewGuid();
        var creado = Fecha(creadoEnUtc ?? Reloj.UtcAhora);
        if (tipo is null && clave is null && !principal && orden == 0)
        {
            Ejecutar("INSERT INTO EstadoReproductivo (Id, Codigo, Nombre, CodigoDairyComp, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $c, $n, $d, $a, $f, $f)",
                ("$id", id), ("$c", codigo), ("$n", nombre), ("$d", codigoDairyComp), ("$a", activo ? 1 : 0), ("$f", creado));
            return id;
        }
        Ejecutar(@"INSERT INTO EstadoReproductivo (Id, Codigo, Nombre, CodigoDairyComp, Activo, Tipo, ClaveEstandar, EsPrincipal, Orden, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $c, $n, $d, $a, $t, $k, $p, $o, $f, $f)",
            ("$id", id), ("$c", codigo), ("$n", nombre), ("$d", codigoDairyComp), ("$a", activo ? 1 : 0),
            ("$t", tipo ?? "Estado"), ("$k", clave), ("$p", principal ? 1 : 0), ("$o", orden), ("$f", creado));
        return id;
    }

    /// <summary>
    /// Migra la base hasta la migración indicada (por ejemplo "EsperaYRechazo", la versión anterior a la spec 005) o hasta la
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

    public Guid Tacto(Guid animalId, Guid usuarioId, Guid? estadoNuevoId, string tipo = "Normal", Guid? tactoAnuladoId = null,
        int? diasPrenez = null, DateTime? fechaHora = null, long? orden = null, bool anulado = false)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, DiasPrenez, UsuarioId, DispositivoId,
                       Tipo, TactoAnuladoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $a, $fh, $o, $en, $d, $u, $disp, $t, $ta, $an, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$o", orden ?? ++_orden),
            ("$en", estadoNuevoId), ("$d", diasPrenez), ("$u", usuarioId), ("$disp", Guid.NewGuid()),
            ("$t", tipo), ("$ta", tactoAnuladoId), ("$an", anulado ? 1 : 0), ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    // ---- Mangas, cierres y marcas por SQL crudo (spec 003) ----

    public Guid Manga(Guid rodeoId, int numero, Guid usuarioId, DateTime? creadaEn = null)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Manga (Id, RodeoId, Numero, AbiertaPorUsuarioId, DispositivoId, CreadoEnUtc)
                   VALUES ($id, $r, $n, $u, $d, $f)",
            ("$id", id), ("$r", rodeoId), ("$n", numero), ("$u", usuarioId), ("$d", Guid.NewGuid()),
            ("$f", Fecha(creadaEn ?? Reloj.UtcAhora)));
        return id;
    }

    /// <summary>Un cierre de manga. Si se pasa <c>confirmada</c>, la diferencia se calcula (confirmada - aviss).</summary>
    public Guid CierreManga(Guid mangaId, Guid usuarioId, string tipo = "Manual", int aviss = 0, int? confirmada = null,
        bool anulado = false, DateTime? fechaHora = null)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia,
                       UsuarioId, DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $m, $t, $fh, $a, $c, $df, $u, $d, $an, $f, $f)",
            ("$id", id), ("$m", mangaId), ("$t", tipo), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$a", aviss),
            ("$c", confirmada), ("$df", confirmada is int c ? c - aviss : null), ("$u", usuarioId), ("$d", Guid.NewGuid()),
            ("$an", anulado ? 1 : 0), ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid MarcaPasada(Guid animalId, Guid mangaId, Guid usuarioId, DateTime? fechaHora = null, long? orden = null,
        bool anulada = false)
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion, UsuarioId, DispositivoId,
                       Anulado, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $a, $m, $fh, $o, $u, $d, $an, $f, $f)",
            ("$id", id), ("$a", animalId), ("$m", mangaId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)),
            ("$o", orden ?? ++_orden), ("$u", usuarioId), ("$d", Guid.NewGuid()), ("$an", anulada ? 1 : 0),
            ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    // ---- Esperas y rechazos por SQL crudo (spec 004) ----

    /// <summary>
    /// Una espera. "Pendiente" (por defecto) no lleva datos de resolución; "Resuelta" lleva el registro que la resolvió;
    /// "Salteada" no lleva registro (así lo exige CK_Espera_Resolucion).
    /// </summary>
    public Guid Espera(Guid animalId, Guid usuarioId, string estado = "Pendiente", Guid? registroResolucionId = null,
        DateTime? fechaHora = null)
    {
        var id = Guid.NewGuid();
        var cerrada = estado != "Pendiente";
        Ejecutar(@"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, UsuarioId, DispositivoId, Estado, ResueltaEnUtc,
                       ResueltaPorUsuarioId, RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
                   VALUES ($id, $a, $fh, $u, $d, $e, $re, $rp, $rr, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$u", usuarioId),
            ("$d", Guid.NewGuid()), ("$e", estado), ("$re", cerrada ? Fecha(Reloj.UtcAhora) : null),
            ("$rp", cerrada ? usuarioId : null), ("$rr", estado == "Resuelta" ? registroResolucionId ?? Guid.NewGuid() : null),
            ("$f", Fecha(Reloj.UtcAhora)));
        return id;
    }

    public Guid Rechazo(Guid animalId, Guid usuarioId, string? observaciones = null, DateTime? fechaHora = null,
        long? orden = null, bool anulado = false, Guid? eventoId = null)
    {
        var id = Guid.NewGuid();
        var columnaEvento = eventoId is null ? "" : ", EventoId";
        var valorEvento = eventoId is null ? "" : ", $ev";
        var parametros = new List<(string, object?)>
        {
            ("$id", id), ("$a", animalId), ("$fh", Fecha(fechaHora ?? Reloj.UtcAhora)), ("$o", orden ?? ++_orden),
            ("$ob", observaciones), ("$u", usuarioId), ("$d", Guid.NewGuid()), ("$an", anulado ? 1 : 0),
            ("$ae", anulado ? Fecha(Reloj.UtcAhora) : null), ("$ap", anulado ? usuarioId : null),
            ("$f", Fecha(Reloj.UtcAhora)), ("$ev", eventoId)
        };
        // $ev se manda siempre (un parámetro sin usar no molesta a SQLite); la columna solo se nombra si hay evento, así
        // el helper sigue sirviendo sobre una base anterior a "EventosDelCatalogo".
        Ejecutar($@"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Observaciones, UsuarioId, DispositivoId,
                       Anulado, AnuladoEnUtc, AnuladoPorUsuarioId, CreadoEnUtc, ActualizadoEnUtc{columnaEvento})
                   VALUES ($id, $a, $fh, $o, $ob, $u, $d, $an, $ae, $ap, $f, $f{valorEvento})",
            parametros.ToArray());
        return id;
    }

    public bool ExisteTrigger(string nombre)
        => Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = $n", ("$n", nombre)) == 1;

    public Guid Historial(Guid usuarioId, string accion = "Alta")
    {
        var id = Guid.NewGuid();
        Ejecutar(@"INSERT INTO Historial (Id, Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo, FechaHoraUtc, UsuarioId, Accion, CreadoEnUtc)
                   VALUES ($id, 'Animal', $ent, 'Alta', NULL, 'x', $f, $u, $a, $f)",
            ("$id", id), ("$ent", Guid.NewGuid()), ("$f", Fecha(Reloj.UtcAhora)), ("$u", usuarioId), ("$a", accion));
        return id;
    }

    public void Dispose()
    {
        _cruda?.Dispose();
        SqliteConnection.ClearAllPools(); // en Windows el archivo queda bloqueado mientras el pool tenga conexiones
        try { Directory.Delete(Directorio, recursive: true); } catch (IOException) { /* limpieza best-effort */ }
    }
}
