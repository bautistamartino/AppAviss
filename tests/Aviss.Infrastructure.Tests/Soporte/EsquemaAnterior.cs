using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// La base tal como la dejaban las specs 001 a 004 (con la tabla Usuario y las columnas de "quién" y "qué tablet").
/// Como el modelo actual ya no conoce a Usuario, todo se arma con SQL crudo sobre una base migrada hasta una migración
/// vieja. Sirve para probar migraciones sobre bases creadas por la versión anterior (spec 006).
/// </summary>
public sealed class EsquemaAnterior
{
    private readonly BaseDeDatosDePrueba _db;
    private long _orden;

    public EsquemaAnterior(BaseDeDatosDePrueba db) => _db = db;

    /// <summary>Migra la base (creada con migrar: false) hasta la migración indicada; null = hasta la última.</summary>
    public static void MigrarHasta(BaseDeDatosDePrueba db, string? destino)
    {
        using var ctx = db.NuevoContexto();
        var migrador = ctx.GetService<IMigrator>();
        if (destino is null) migrador.Migrate(); else migrador.Migrate(destino);
        SqliteConnection.ClearAllPools();
    }

    private string F(DateTime? d = null) => BaseDeDatosDePrueba.Fecha(d ?? _db.Reloj.UtcAhora);

    // ---------- Altas con el esquema viejo ----------

    public Guid Usuario(string nombre = "Vet", string? matricula = null, Guid? id = null, bool activo = true)
    {
        var uid = id ?? Guid.NewGuid();
        _db.Ejecutar("INSERT INTO Usuario (Id, Nombre, Matricula, Activo, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $n, $m, $a, $f, $f)",
            ("$id", uid), ("$n", nombre), ("$m", matricula), ("$a", activo ? 1 : 0), ("$f", F()));
        return uid;
    }

    public Guid Tacto(Guid animalId, Guid usuarioId, Guid? estadoNuevoId, string tipo = "Normal", Guid? tactoAnuladoId = null,
        int? diasPrenez = null, DateTime? fechaHora = null, bool anulado = false, Guid? estadoAnteriorId = null, string? observaciones = null)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar(@"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoAnteriorId, EstadoNuevoId, DiasPrenez, Observaciones,
                           UsuarioId, DispositivoId, Tipo, TactoAnuladoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                       VALUES ($id, $a, $fh, $o, $ea, $en, $d, $ob, $u, $disp, $t, $ta, $an, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", F(fechaHora)), ("$o", ++_orden), ("$ea", estadoAnteriorId), ("$en", estadoNuevoId),
            ("$d", diasPrenez), ("$ob", observaciones), ("$u", usuarioId), ("$disp", Guid.NewGuid()), ("$t", tipo),
            ("$ta", tactoAnuladoId), ("$an", anulado ? 1 : 0), ("$f", F()));
        return id;
    }

    public Guid Manga(Guid rodeoId, int numero, Guid usuarioId)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar("INSERT INTO Manga (Id, RodeoId, Numero, AbiertaPorUsuarioId, DispositivoId, CreadoEnUtc) VALUES ($id, $r, $n, $u, $d, $f)",
            ("$id", id), ("$r", rodeoId), ("$n", numero), ("$u", usuarioId), ("$d", Guid.NewGuid()), ("$f", F()));
        return id;
    }

    public Guid CierreManga(Guid mangaId, Guid usuarioId, string tipo = "Manual", int aviss = 0, int? confirmada = null, bool anulado = false)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar(@"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia,
                           UsuarioId, DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                       VALUES ($id, $m, $t, $fh, $a, $c, $df, $u, $d, $an, $f, $f)",
            ("$id", id), ("$m", mangaId), ("$t", tipo), ("$fh", F()), ("$a", aviss), ("$c", confirmada),
            ("$df", confirmada is int c ? c - aviss : null), ("$u", usuarioId), ("$d", Guid.NewGuid()), ("$an", anulado ? 1 : 0), ("$f", F()));
        return id;
    }

    public Guid MarcaPasada(Guid animalId, Guid mangaId, Guid usuarioId, bool anulada = false)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar(@"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion, UsuarioId, DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                       VALUES ($id, $a, $m, $fh, $o, $u, $d, $an, $f, $f)",
            ("$id", id), ("$a", animalId), ("$m", mangaId), ("$fh", F()), ("$o", ++_orden), ("$u", usuarioId), ("$d", Guid.NewGuid()),
            ("$an", anulada ? 1 : 0), ("$f", F()));
        return id;
    }

    /// <summary>Espera "Pendiente", "Resuelta" (con el registro que la resolvió) o "Salteada".</summary>
    public Guid Espera(Guid animalId, Guid usuarioId, string estado = "Pendiente", Guid? registroResolucionId = null)
    {
        var id = Guid.NewGuid();
        var cerrada = estado != "Pendiente";
        _db.Ejecutar(@"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, UsuarioId, DispositivoId, Estado, ResueltaEnUtc, ResueltaPorUsuarioId,
                           RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
                       VALUES ($id, $a, $fh, $u, $d, $e, $re, $rp, $rr, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", F()), ("$u", usuarioId), ("$d", Guid.NewGuid()), ("$e", estado),
            ("$re", cerrada ? F() : null), ("$rp", cerrada ? usuarioId : null),
            ("$rr", estado == "Resuelta" ? registroResolucionId ?? Guid.NewGuid() : null), ("$f", F()));
        return id;
    }

    public Guid Rechazo(Guid animalId, Guid usuarioId, string? observaciones = null, bool anulado = false)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar(@"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Observaciones, UsuarioId, DispositivoId,
                           Anulado, AnuladoEnUtc, AnuladoPorUsuarioId, CreadoEnUtc, ActualizadoEnUtc)
                       VALUES ($id, $a, $fh, $o, $ob, $u, $d, $an, $ae, $ap, $f, $f)",
            ("$id", id), ("$a", animalId), ("$fh", F()), ("$o", ++_orden), ("$ob", observaciones), ("$u", usuarioId),
            ("$d", Guid.NewGuid()), ("$an", anulado ? 1 : 0), ("$ae", anulado ? F() : null), ("$ap", anulado ? usuarioId : null), ("$f", F()));
        return id;
    }

    public Guid Historial(Guid usuarioId, string accion = "Alta", string entidad = "Animal", Guid? entidadId = null)
    {
        var id = Guid.NewGuid();
        _db.Ejecutar(@"INSERT INTO Historial (Id, Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo, FechaHoraUtc, UsuarioId, Accion, CreadoEnUtc)
                       VALUES ($id, $en, $ent, 'Campo', NULL, 'x', $f, $u, $a, $f)",
            ("$id", id), ("$en", entidad), ("$ent", entidadId ?? Guid.NewGuid()), ("$f", F()), ("$u", usuarioId), ("$a", accion));
        return id;
    }

    // ---------- Una base vieja con "de todo" (RF-11 y los casos del plan) ----------

    /// <summary>Id fijo del usuario "Sin asignar" de la versión anterior.</summary>
    public static readonly Guid SinAsignarId = new("00000000-0000-0000-0000-000000000001");

    public static readonly string[] AccionesDeHistorial =
    {
        "Alta", "Edicion", "Tacto", "AnulacionTacto", "AnulacionAlta", "MarcaPasada", "AnulacionMarca", "MangaAbierta",
        "MangaCerrada", "AnulacionCierre", "EsperaCreada", "EsperaResuelta", "EsperaSalteada", "RechazoCreado", "RechazoAnulado"
    };

    /// <summary>Los nombres de los veterinarios cargados por <see cref="Poblar"/>: no deben quedar en ningún valor tras migrar.</summary>
    public static readonly string[] NombresDeVeterinarios = { "Dra. Zulema Ñandú", "Dr. Bartolomé Quiroga", "Vet Anselmo Peralta" };

    /// <summary>Ids útiles de lo creado por <see cref="Poblar"/>.</summary>
    public sealed record Poblado(Guid Rodeo1, Guid Manga1, Guid Manga2, Guid Manga3, Guid Animal, Guid TactoAnulado, Guid Anulacion,
        Guid Rechazo, Guid EsperaResuelta, byte[] BlobRodeo1, string HashRodeo1);

    /// <summary>Llena la base vieja con un caso de cada tipo de fila de cada tabla (3 veterinarios, una baja y "Sin asignar").</summary>
    public Poblado Poblar()
    {
        var sinAsignar = Usuario("Sin asignar", id: SinAsignarId);
        var v1 = Usuario(NombresDeVeterinarios[0], "MP-123");
        var v2 = Usuario(NombresDeVeterinarios[1], "MP-456");
        var v3 = Usuario(NombresDeVeterinarios[2], activo: false);

        var est1 = _db.Establecimiento("Tambo Norte");
        var est2 = _db.Establecimiento("Tambo Sur");
        var blob = Enumerable.Range(0, 300).Select(i => (byte)(i * 7 % 251)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant();
        var rodeo1 = _db.Rodeo(est1, hash, largoBlob: 1);
        _db.Ejecutar("UPDATE Rodeo SET ArchivoOrigenBlob = $b WHERE Id = $id", ("$b", blob), ("$id", rodeo1)); // un blob real para comparar byte por byte
        _db.Rodeo(est2, new string('b', 64), largoBlob: 5, estado: "Exportado");

        var seca = _db.Estado("SECA", "SECA", "SECA");
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA");

        var animales = new List<Guid>();
        for (var i = 1; i <= 40; i++)
            animales.Add(_db.Animal(rodeo1, i.ToString(), i % 4 == 0 ? "AltaEnCampo" : "Importado", estadoId: i % 2 == 0 ? seca : prenada,
                idElectronico: i % 3 == 0 ? "982000" + i : null, datosOriginalesJson: i % 4 == 0 ? null : "{\"ID\":\"" + i + "\"}"));
        _db.Animal(rodeo1, "900", "AltaEnCampo", anulado: true);

        // Tactos: normales (con días y observación), uno anulado con su anulación.
        Tacto(animales[0], v1, prenada, diasPrenez: 45, estadoAnteriorId: seca, observaciones: "ok");
        var tAnulado = Tacto(animales[1], v2, seca, anulado: true, estadoAnteriorId: prenada);
        var anulacion = Tacto(animales[1], v2, prenada, tipo: "Anulacion", tactoAnuladoId: tAnulado, estadoAnteriorId: seca);
        Tacto(animales[2], sinAsignar, seca);
        var tacto3 = Tacto(animales[3], v3, prenada, diasPrenez: 90);

        // Mangas: una cerrada (cierre vigente), una con cierre anulado y otro vigente, una abierta.
        var manga1 = Manga(rodeo1, 1, v1);
        var manga2 = Manga(rodeo1, 2, v2);
        var manga3 = Manga(rodeo1, 3, v1);
        CierreManga(manga1, v1, "Manual", aviss: 3, confirmada: 4);
        CierreManga(manga2, v2, "TerminoElTacto", aviss: 2, confirmada: 2, anulado: true);
        CierreManga(manga2, v2, "Automatico", aviss: 2);
        MarcaPasada(animales[0], manga1, v1);
        MarcaPasada(animales[1], manga1, v1, anulada: true);
        MarcaPasada(animales[1], manga1, v2);
        MarcaPasada(animales[2], manga2, v2);
        MarcaPasada(animales[4], manga3, v1);

        // Rechazos y esperas de los tres estados.
        var rechazo = Rechazo(animales[5], v1, "cojera");
        Rechazo(animales[6], v2, "sucia", anulado: true);
        Espera(animales[7], v1);
        var esperaResuelta = Espera(animales[8], v2, "Resuelta", tacto3);
        Espera(animales[9], v3, "Salteada");

        // Una fila de historial por cada acción.
        foreach (var a in AccionesDeHistorial) Historial(v1, a);
        Historial(sinAsignar, "Alta");

        return new Poblado(rodeo1, manga1, manga2, manga3, animales[0], tAnulado, anulacion, rechazo, esperaResuelta, blob, hash);
    }

    // ---------- Instantánea de todo lo que debe sobrevivir a la migración (RF-11) ----------

    /// <summary>Tablas de datos de trabajo y sus columnas en el esquema NUEVO (un subconjunto del viejo).</summary>
    public static readonly (string Tabla, string[] Columnas)[] TablasDeTrabajo =
    {
        ("Establecimiento", new[] { "Id", "Nombre", "Notas", "Activo", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("EstadoReproductivo", new[] { "Id", "Codigo", "Nombre", "CodigoDairyComp", "Activo", "NombreClave", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Rodeo", new[] { "Id", "EstablecimientoId", "Nombre", "FechaImportacionUtc", "ArchivoOrigenNombre", "ArchivoOrigenHash", "ArchivoOrigenBlob", "PerfilExcel", "Estado", "Activo", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Animal", new[] { "Id", "RodeoId", "NumeroCaravana", "IdElectronico", "EstadoReproductivoId", "FechaUltimoTactoUtc", "Observaciones", "Origen", "Anulado", "AnuladoEnUtc", "DatosOriginalesJson", "NumeroCaravanaClave", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Tacto", new[] { "Id", "AnimalId", "FechaHoraUtc", "OrdenInsercion", "EstadoAnteriorId", "EstadoNuevoId", "DiasPrenez", "Observaciones", "Tipo", "TactoAnuladoId", "Anulado", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("MarcaPasada", new[] { "Id", "AnimalId", "MangaId", "FechaHoraUtc", "OrdenInsercion", "Anulado", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Rechazo", new[] { "Id", "AnimalId", "FechaHoraUtc", "OrdenInsercion", "Observaciones", "Anulado", "AnuladoEnUtc", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Espera", new[] { "Id", "AnimalId", "FechaHoraUtc", "Estado", "ResueltaEnUtc", "RegistroResolucionId", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Manga", new[] { "Id", "RodeoId", "Numero", "CreadoEnUtc" }),
        ("CierreManga", new[] { "Id", "MangaId", "Tipo", "FechaHoraUtc", "CantidadAviss", "CantidadConfirmada", "Diferencia", "Anulado", "CreadoEnUtc", "ActualizadoEnUtc" }),
        ("Historial", new[] { "Id", "Accion", "Campo", "CreadoEnUtc", "Entidad", "EntidadId", "FechaHoraUtc", "ValorAnterior", "ValorNuevo" }),
    };

    /// <summary>
    /// Foto de cada tabla: una línea de texto por fila (ordenadas por Id) con las columnas que existen en el esquema NUEVO.
    /// Los BLOB se resumen con su SHA-256. Sirve igual antes y después de migrar, porque lo nuevo es un subconjunto de lo viejo.
    /// Incluye además el orden de lectura por animal (fecha, hora y orden de guardado) de tactos, marcas y rechazos.
    /// </summary>
    public static Dictionary<string, List<string>> Instantanea(BaseDeDatosDePrueba db)
    {
        var foto = new Dictionary<string, List<string>>();
        foreach (var (tabla, columnas) in TablasDeTrabajo)
        {
            var filas = new List<string>();
            using var cmd = db.Cruda.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", columnas)} FROM {tabla} ORDER BY Id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var partes = new string[r.FieldCount];
                for (var i = 0; i < r.FieldCount; i++)
                    partes[i] = r.IsDBNull(i) ? "<null>"
                        : r.GetValue(i) is byte[] b ? "blob:" + Convert.ToHexString(SHA256.HashData(b))
                        : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
                filas.Add(string.Join("|", partes));
            }
            foto[tabla] = filas;
        }

        foreach (var tabla in new[] { "Tacto", "MarcaPasada", "Rechazo" })
        {
            var orden = new List<string>();
            using var cmd = db.Cruda.CreateCommand();
            cmd.CommandText = $"SELECT AnimalId, Id FROM {tabla} ORDER BY AnimalId, FechaHoraUtc, OrdenInsercion";
            using var r = cmd.ExecuteReader();
            while (r.Read()) orden.Add(r.GetString(0) + ">" + r.GetString(1));
            foto["orden:" + tabla] = orden;
        }
        return foto;
    }

    /// <summary>Un volcado de texto de TODAS las tablas y columnas (para buscar un nombre en cualquier valor de la base).</summary>
    public static string VolcadoDeTexto(BaseDeDatosDePrueba db)
    {
        var sb = new StringBuilder();
        var tablas = new List<string>();
        using (var cmd = db.Cruda.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tablas.Add(r.GetString(0));
        }
        foreach (var t in tablas)
        {
            using var cmd = db.Cruda.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{t}\"";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                for (var i = 0; i < r.FieldCount; i++)
                    if (!r.IsDBNull(i) && r.GetValue(i) is not byte[])
                        sb.Append(Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }
}
