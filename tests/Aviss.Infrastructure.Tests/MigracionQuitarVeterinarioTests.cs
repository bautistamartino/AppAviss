using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006: la migración "QuitarVeterinario" sobre una base creada por la versión anterior (con Usuario, datos de cada tabla,
/// triggers e índices). Cubre RF-11 (no se pierde nada), RF-13 (los triggers de inmutabilidad siguen) y RF-14 (unicidades,
/// CHECK, índices y claves foráneas). Las fallas a mitad de migración están en <see cref="MigracionQuitarVeterinarioFallasTests"/>.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class MigracionQuitarVeterinarioTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    private EsquemaAnterior.Poblado BaseVieja()
    {
        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        return new EsquemaAnterior(_db).Poblar();
    }

    // Hasta "QuitarVeterinario": lo que prueban estos tests es esa migración. La cadena completa (con EventosDelCatalogo, spec 005)
    // se compara contra el modelo en "El_esquema_migrado_coincide_con_el_que_crea_EF_desde_el_modelo".
    private void Migrar() => EsquemaAnterior.MigrarHasta(_db, "QuitarVeterinario");

    /// <summary>Base vieja con datos ya migrada; devuelve lo creado.</summary>
    private EsquemaAnterior.Poblado BaseMigrada()
    {
        var p = BaseVieja();
        Migrar();
        return p;
    }

    private long _ordenNuevo = 900_000;
    private long Orden() => ++_ordenNuevo;

    private static void Rechaza(Action accion) => Assert.Throws<SqliteException>(accion);

    private string FechaOtra() => BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddDays(1));

    // ---------- RF-11: no se pierde nada ----------

    [Fact]
    public void Conserva_todos_los_datos()
    {
        var p = BaseVieja();
        var antes = EsquemaAnterior.Instantanea(_db);
        Assert.NotEmpty(antes["Tacto"]);
        Assert.NotEmpty(antes["Espera"]);

        Migrar();
        var despues = EsquemaAnterior.Instantanea(_db);

        foreach (var (tabla, filas) in antes)
            Assert.True(filas.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió al migrar.");

        // El Excel original: byte por byte y con su hash (el guardado y el recalculado coinciden con los de antes).
        var blob = (byte[])_db.Escalar("SELECT ArchivoOrigenBlob FROM Rodeo WHERE Id = $id", ("$id", p.Rodeo1))!;
        Assert.Equal(p.BlobRodeo1, blob);
        Assert.Equal(p.HashRodeo1, _db.Escalar("SELECT ArchivoOrigenHash FROM Rodeo WHERE Id = $id", ("$id", p.Rodeo1)));
        Assert.Equal(p.HashRodeo1, Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant());

        // Se conserva el vínculo entre la espera resuelta y el registro que la resolvió, y la anulación del tacto.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera WHERE Id = $id AND Estado = 'Resuelta' AND RegistroResolucionId IS NOT NULL", ("$id", p.EsperaResuelta)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $id AND TactoAnuladoId = $t", ("$id", p.Anulacion), ("$t", p.TactoAnulado)));
    }

    [Fact]
    public void Descarta_solo_quien_y_tablet()
    {
        BaseVieja();
        Migrar();

        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        foreach (var tabla in new[] { "Tacto", "MarcaPasada", "Manga", "CierreManga", "Rechazo", "Espera", "Historial" })
        foreach (var columna in new[] { "UsuarioId", "AbiertaPorUsuarioId", "AnuladoPorUsuarioId", "ResueltaPorUsuarioId", "DispositivoId" })
            Assert.Equal(0, _db.Contar($"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = $c", ("$c", columna)));

        // Los nombres de los veterinarios no quedan en ningún valor de ninguna tabla.
        var volcado = EsquemaAnterior.VolcadoDeTexto(_db);
        foreach (var nombre in EsquemaAnterior.NombresDeVeterinarios) Assert.DoesNotContain(nombre, volcado);
        Assert.DoesNotContain("Sin asignar", volcado);
        Assert.DoesNotContain("MP-123", volcado);
    }

    [Fact]
    public void Queda_registrada_en_el_historial_de_migraciones()
    {
        BaseMigrada();
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_QuitarVeterinario'"));
    }

    /// <summary>
    /// Lo mismo que la prueba de rendimiento pero con pocas vacas, para que el generador de volumen y la migración sobre muchas
    /// filas se prueben siempre (la de 20.000 vacas es de la categoría Rendimiento y no corre en cada tarea).
    /// </summary>
    [Fact]
    public void Migra_un_volumen_chico_con_el_mismo_generador_que_la_prueba_de_rendimiento()
    {
        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        var (vacas, registros) = VolumenAnterior.Poblar(_db, 300);
        Assert.Equal(300, vacas);
        Assert.Equal(300 * 6, registros);
        var antes = EsquemaAnterior.Instantanea(_db);
        Assert.Equal(300 * VolumenAnterior.TactosPorVaca, antes["Tacto"].Count);

        Migrar();
        var despues = EsquemaAnterior.Instantanea(_db);

        foreach (var (tabla, filas) in antes)
            Assert.True(filas.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió al migrar.");
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    // ---------- RF-13: triggers de inmutabilidad, en filas viejas y en filas nuevas ----------

    /// <summary>Una fila de cada tabla inmutable (más una espera ya resuelta y una pendiente).</summary>
    private sealed record Filas(object Tacto, object Marca, object Cierre, object Rechazo, object EsperaPendiente,
        object EsperaResuelta, object Manga, object Historial);

    private object Una(string sql) => _db.Escalar(sql) ?? throw new InvalidOperationException("La base vieja no tiene la fila esperada: " + sql);

    /// <summary>Filas que ya existían antes de migrar (las que creó <see cref="EsquemaAnterior.Poblar"/>).</summary>
    private Filas FilasViejas() => new(
        Una("SELECT Id FROM Tacto WHERE Tipo = 'Normal' AND Anulado = 0 ORDER BY OrdenInsercion LIMIT 1"),
        Una("SELECT Id FROM MarcaPasada WHERE Anulado = 0 ORDER BY OrdenInsercion LIMIT 1"),
        Una("SELECT Id FROM CierreManga WHERE Anulado = 0 ORDER BY Id LIMIT 1"),
        Una("SELECT Id FROM Rechazo WHERE Anulado = 0 ORDER BY OrdenInsercion LIMIT 1"),
        Una("SELECT Id FROM Espera WHERE Estado = 'Pendiente' LIMIT 1"),
        Una("SELECT Id FROM Espera WHERE Estado = 'Resuelta' LIMIT 1"),
        Una("SELECT Id FROM Manga ORDER BY Numero LIMIT 1"),
        Una("SELECT Id FROM Historial LIMIT 1"));

    /// <summary>Filas creadas DESPUÉS de migrar, con la base ya nueva.</summary>
    private Filas FilasNuevas(EsquemaAnterior.Poblado p)
    {
        var estado = Guid.Parse((string)Una("SELECT Id FROM EstadoReproductivo LIMIT 1"));
        var animal1 = _db.Animal(p.Rodeo1, "N-100");
        var animal2 = _db.Animal(p.Rodeo1, "N-101");
        var manga = _db.Manga(p.Rodeo1, 10);
        return new Filas(
            _db.Tacto(animal1, estado, orden: Orden()),
            _db.MarcaPasada(animal1, manga, orden: Orden()),
            _db.CierreManga(manga),
            _db.Rechazo(animal1, orden: Orden()),
            _db.Espera(animal1),
            _db.Espera(animal2, "Resuelta"),
            manga,
            _db.Historial());
    }

    [Theory]
    [InlineData("vieja")]
    [InlineData("nueva")]
    public void Triggers_de_inmutabilidad_en_filas_viejas_y_nuevas(string origen)
    {
        var p = BaseMigrada();
        var f = origen == "vieja" ? FilasViejas() : FilasNuevas(p);
        var otra = FechaOtra();
        var ahora = BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora);

        // Historial: ni modificar ni borrar.
        Rechaza(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'y' WHERE Id = $id", ("$id", f.Historial)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", f.Historial)));

        // Manga: ni modificar ni borrar.
        Rechaza(() => _db.Ejecutar("UPDATE Manga SET Numero = 99 WHERE Id = $id", ("$id", f.Manga)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Manga WHERE Id = $id", ("$id", f.Manga)));

        // Tacto: no se borra ni se modifica; solo pasa de vigente a anulado, una vez y sin volver atrás.
        Rechaza(() => _db.Ejecutar("UPDATE Tacto SET Observaciones = 'x' WHERE Id = $id", ("$id", f.Tacto)));
        Rechaza(() => _db.Ejecutar("UPDATE Tacto SET DiasPrenez = 10 WHERE Id = $id", ("$id", f.Tacto)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Tacto WHERE Id = $id", ("$id", f.Tacto)));
        Assert.Equal(1, _db.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $id", ("$id", f.Tacto)));
        Rechaza(() => _db.Ejecutar("UPDATE Tacto SET Anulado = 0 WHERE Id = $id", ("$id", f.Tacto)));

        // Marca de pasada.
        Rechaza(() => _db.Ejecutar("UPDATE MarcaPasada SET FechaHoraUtc = $f WHERE Id = $id", ("$id", f.Marca), ("$f", otra)));
        Rechaza(() => _db.Ejecutar("DELETE FROM MarcaPasada WHERE Id = $id", ("$id", f.Marca)));
        Assert.Equal(1, _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 1 WHERE Id = $id", ("$id", f.Marca)));
        Rechaza(() => _db.Ejecutar("UPDATE MarcaPasada SET Anulado = 0 WHERE Id = $id", ("$id", f.Marca)));

        // Cierre de manga.
        Rechaza(() => _db.Ejecutar("UPDATE CierreManga SET CantidadAviss = 99 WHERE Id = $id", ("$id", f.Cierre)));
        Rechaza(() => _db.Ejecutar("DELETE FROM CierreManga WHERE Id = $id", ("$id", f.Cierre)));
        Assert.Equal(1, _db.Ejecutar("UPDATE CierreManga SET Anulado = 1 WHERE Id = $id", ("$id", f.Cierre)));
        Rechaza(() => _db.Ejecutar("UPDATE CierreManga SET Anulado = 0 WHERE Id = $id", ("$id", f.Cierre)));

        // Rechazo: anular exige la fecha de anulación (CHECK), y no se puede volver atrás.
        Rechaza(() => _db.Ejecutar("UPDATE Rechazo SET Observaciones = 'x' WHERE Id = $id", ("$id", f.Rechazo)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", f.Rechazo)));
        Assert.Equal(1, _db.Ejecutar("UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id", ("$id", f.Rechazo), ("$f", ahora)));
        Rechaza(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 0, AnuladoEnUtc = NULL WHERE Id = $id", ("$id", f.Rechazo)));

        // Espera: una pendiente se resuelve o se saltea UNA vez; una ya cerrada no vuelve a cambiar; nunca se borra.
        Rechaza(() => _db.Ejecutar("UPDATE Espera SET FechaHoraUtc = $f WHERE Id = $id", ("$id", f.EsperaPendiente), ("$f", otra)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", f.EsperaPendiente)));
        Assert.Equal(1, _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f WHERE Id = $id", ("$id", f.EsperaPendiente), ("$f", ahora)));
        Rechaza(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Resuelta', ResueltaEnUtc = $f, RegistroResolucionId = $g WHERE Id = $id",
            ("$id", f.EsperaPendiente), ("$f", ahora), ("$g", Guid.NewGuid())));
        Rechaza(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', RegistroResolucionId = NULL WHERE Id = $id", ("$id", f.EsperaResuelta)));
        Rechaza(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", f.EsperaResuelta)));
    }

    [Fact]
    public void Quedan_los_triggers_de_inmutabilidad()
    {
        BaseMigrada();
        foreach (var t in TriggersEsperados)
            Assert.True(_db.ExisteTrigger(t), t);
        Assert.False(_db.ExisteTrigger("TR_Usuario_NoBorrar"));
        Assert.False(_db.ExisteTrigger("TR_Usuario_SinAsignarProtegido"));
    }

    /// <summary>Todos los triggers de la base nueva: los 15 de las tablas reconstruidas y los de las que no se tocaron.</summary>
    private static readonly string[] TriggersEsperados =
    {
        "TR_Historial_NoModificar", "TR_Historial_NoBorrar", "TR_Tacto_NoModificar", "TR_Tacto_NoBorrar",
        "TR_MarcaPasada_NoModificar", "TR_MarcaPasada_NoBorrar", "TR_MarcaPasada_MismoRodeo", "TR_Manga_NoModificar", "TR_Manga_NoBorrar",
        "TR_CierreManga_NoModificar", "TR_CierreManga_NoBorrar", "TR_Rechazo_NoModificar", "TR_Rechazo_NoBorrar",
        "TR_Espera_NoModificar", "TR_Espera_NoBorrar",
        "TR_Animal_NoBorrar", "TR_Animal_Inmutables", "TR_Establecimiento_NoBorrar", "TR_Rodeo_NoBorrar", "TR_EstadoReproductivo_NoBorrar"
    };

    [Fact]
    public void La_marca_de_pasada_de_otro_rodeo_sigue_rechazada()
    {
        var p = BaseMigrada();
        var otroRodeo = _db.Rodeo(_db.Establecimiento("Tambo Otro"), new string('c', 64));
        var mangaOtra = _db.Manga(otroRodeo, 1);
        Rechaza(() => _db.MarcaPasada(p.Animal, mangaOtra, orden: Orden()));   // TR_MarcaPasada_MismoRodeo
    }

    // ---------- RF-14: unicidades, CHECK, índices y esquema ----------

    [Fact]
    public void Garantias_de_unicidad()
    {
        var p = BaseMigrada();

        // Una caravana no se repite en el rodeo.
        Rechaza(() => _db.Animal(p.Rodeo1, "1"));
        // Una marca vigente por vaca y manga (una anulada del mismo par sí entra).
        Rechaza(() => _db.MarcaPasada(p.Animal, p.Manga1, orden: Orden()));
        _db.MarcaPasada(p.Animal, p.Manga1, orden: Orden(), anulada: true);
        // Un cierre vigente por manga.
        Rechaza(() => _db.CierreManga(p.Manga1));
        // Un número de manga por rodeo.
        Rechaza(() => _db.Manga(p.Rodeo1, 1));
        // Una sola espera pendiente por vaca.
        var animalConEspera = Guid.Parse((string)Una("SELECT AnimalId FROM Espera WHERE Estado = 'Pendiente'"));
        Rechaza(() => _db.Espera(animalConEspera));
        // Una sola anulación por tacto.
        var animalDelAnulado = Guid.Parse((string)_db.Escalar("SELECT AnimalId FROM Tacto WHERE Id = $id", ("$id", p.TactoAnulado))!);
        Rechaza(() => _db.Tacto(animalDelAnulado, null, "Anulacion", p.TactoAnulado, orden: Orden()));
        // El orden de guardado no se repite (tactos, marcas y rechazos).
        var ordenTacto = (long)_db.Escalar("SELECT OrdenInsercion FROM Tacto LIMIT 1")!;
        Rechaza(() => _db.Tacto(p.Animal, Guid.Parse((string)Una("SELECT Id FROM EstadoReproductivo LIMIT 1")), orden: ordenTacto));
        var ordenMarca = (long)_db.Escalar("SELECT OrdenInsercion FROM MarcaPasada LIMIT 1")!;
        var animalSinMarca = _db.Animal(p.Rodeo1, "U-1");
        Rechaza(() => _db.MarcaPasada(animalSinMarca, p.Manga3, orden: ordenMarca));
        var ordenRechazo = (long)_db.Escalar("SELECT OrdenInsercion FROM Rechazo LIMIT 1")!;
        Rechaza(() => _db.Rechazo(animalSinMarca, orden: ordenRechazo));
    }

    [Fact]
    public void Los_CHECK_viejos_siguen_aceptando_los_datos_migrados_y_las_filas_nuevas_de_cada_estado()
    {
        var p = BaseMigrada();

        // Lo que ya estaba sigue: una espera de cada estado, rechazos vigente y anulado, la anulación, los tres tipos de cierre.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera WHERE Estado = 'Pendiente'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera WHERE Estado = 'Resuelta'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Espera WHERE Estado = 'Salteada'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Anulado = 0"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Anulado = 1 AND AnuladoEnUtc IS NOT NULL"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Tipo = 'Anulacion'"));
        foreach (var tipo in new[] { "Manual", "TerminoElTacto", "Automatico" })
            Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM CierreManga WHERE Tipo = $t", ("$t", tipo)));

        // Y las filas nuevas de cada estado entran con los CHECK nuevos.
        _db.Espera(_db.Animal(p.Rodeo1, "C-1"));
        _db.Espera(_db.Animal(p.Rodeo1, "C-2"), "Resuelta");
        _db.Espera(_db.Animal(p.Rodeo1, "C-3"), "Salteada");
        var animal = _db.Animal(p.Rodeo1, "C-4");
        _db.Rechazo(animal, "vigente", orden: Orden());
        _db.Rechazo(animal, "anulado", orden: Orden(), anulado: true);
    }

    [Fact]
    public void Los_CHECK_nuevos_y_los_viejos_rechazan_lo_invalido()
    {
        var p = BaseMigrada();
        var estado = Guid.Parse((string)Una("SELECT Id FROM EstadoReproductivo LIMIT 1"));
        var animal = _db.Animal(p.Rodeo1, "K-1");
        var ahora = BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora);

        void Espera(string estadoEspera, string? resueltaEn, object? registro) => _db.Ejecutar(
            @"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, Estado, ResueltaEnUtc, RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, $e, $re, $rr, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", ahora), ("$e", estadoEspera), ("$re", resueltaEn), ("$rr", registro));

        // CK_Espera_Resolucion (nuevo): sin los campos de quién, la resolución se define solo por fecha y registro.
        Rechaza(() => Espera("Resuelta", null, Guid.NewGuid()));       // resuelta sin fecha
        Rechaza(() => Espera("Resuelta", ahora, null));                // resuelta sin registro
        Rechaza(() => Espera("Salteada", ahora, Guid.NewGuid()));      // salteada con registro
        Rechaza(() => Espera("Salteada", null, null));                 // salteada sin fecha
        Rechaza(() => Espera("Pendiente", ahora, null));               // pendiente con fecha
        Rechaza(() => Espera("Pendiente", null, Guid.NewGuid()));      // pendiente con registro
        Rechaza(() => Espera("Otra", null, null));                     // estado desconocido

        // CK_Rechazo_Anulacion (nuevo).
        void Rechazo(int anulado, string? anuladoEn) => _db.Ejecutar(
            @"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Anulado, AnuladoEnUtc, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, $o, $an, $ae, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", ahora), ("$o", Orden()), ("$an", anulado), ("$ae", anuladoEn));
        Rechaza(() => Rechazo(1, null));      // anulado sin fecha
        Rechaza(() => Rechazo(0, ahora));     // vigente con fecha de anulación
        Rechaza(() => Rechazo(2, ahora));     // valor fuera de 0 / 1

        // CHECK que ya existían y se volvieron a escribir en la reconstrucción.
        Rechaza(() => _db.Tacto(animal, estado, tipo: "Otro", orden: Orden()));                  // CK_Tacto_Tipo
        Rechaza(() => _db.Tacto(animal, null, orden: Orden()));                                  // CK_Tacto_EstadoNuevo (Normal sin estado)
        Rechaza(() => _db.Tacto(animal, null, "Anulacion", null, orden: Orden()));               // CK_Tacto_Anulacion
        Rechaza(() => _db.Tacto(animal, estado, diasPrenez: 301, orden: Orden()));               // CK_Tacto_Dias
        Rechaza(() => _db.Manga(p.Rodeo1, 0));                                                   // CK_Manga_Numero
        Rechaza(() => _db.CierreManga(_db.Manga(p.Rodeo1, 20), tipo: "Otro"));                   // CK_CierreManga_Tipo
        Rechaza(() => _db.Ejecutar(
            @"INSERT INTO CierreManga (Id, MangaId, Tipo, FechaHoraUtc, CantidadAviss, CantidadConfirmada, Diferencia, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $m, 'Manual', $f, 2, 5, 0, 0, $f, $f)",                              // CK_CierreManga_Diferencia (5 - 2 <> 0)
            ("$id", Guid.NewGuid()), ("$m", p.Manga3), ("$f", ahora)));
        Rechaza(() => _db.Ejecutar(
            @"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $m, $f, $o, 2, $f, $f)",                                        // CK_MarcaPasada_Anulado
            ("$id", Guid.NewGuid()), ("$a", animal), ("$m", p.Manga3), ("$f", ahora), ("$o", Orden())));
    }

    [Fact]
    public void Historial_acepta_las_quince_acciones_y_rechaza_otra()
    {
        BaseMigrada();

        // Las quince acciones ya estaban en la base vieja (una fila de cada una) y siguen.
        Assert.Equal(15, EsquemaAnterior.AccionesDeHistorial.Length);
        Assert.Equal(15, _db.Contar("SELECT COUNT(DISTINCT Accion) FROM Historial"));

        // Y se pueden volver a guardar en la base nueva.
        foreach (var accion in EsquemaAnterior.AccionesDeHistorial) _db.Historial(accion);
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Historial WHERE Accion = 'RechazoAnulado'"));

        Rechaza(() => _db.Historial("AccionInventada"));
    }

    [Fact]
    public void Indices_recreados_con_sus_nombres_y_filtros()
    {
        BaseMigrada();
        var esperados = new Dictionary<string, string[]>
        {
            ["Tacto"] = new[]
            {
                "IX_Tacto_AnimalId_FechaHoraUtc_OrdenInsercion", "IX_Tacto_AnimalId_TactoAnuladoId", "IX_Tacto_EstadoAnteriorId",
                "IX_Tacto_EstadoNuevoId", "IX_Tacto_FechaHoraUtc", "IX_Tacto_OrdenInsercion", "IX_Tacto_TactoAnuladoId"
            },
            ["MarcaPasada"] = new[]
            {
                "IX_MarcaPasada_AnimalId_FechaHoraUtc_OrdenInsercion", "IX_MarcaPasada_FechaHoraUtc",
                "IX_MarcaPasada_MangaId_AnimalId", "IX_MarcaPasada_OrdenInsercion"
            },
            ["Manga"] = new[] { "IX_Manga_RodeoId_Numero" },
            ["CierreManga"] = new[] { "IX_CierreManga_MangaId" },
            ["Rechazo"] = new[] { "IX_Rechazo_AnimalId_FechaHoraUtc_OrdenInsercion", "IX_Rechazo_OrdenInsercion" },
            ["Espera"] = new[] { "IX_Espera_AnimalId" },
            ["Historial"] = new[] { "IX_Historial_Entidad_EntidadId" },
        };
        foreach (var (tabla, nombres) in esperados)
        {
            var reales = new List<string>();
            using (var cmd = _db.Cruda.CreateCommand())
            {
                cmd.CommandText = $"SELECT name FROM pragma_index_list('{tabla}') WHERE origin = 'c' ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) reales.Add(r.GetString(0));
            }
            Assert.Equal(nombres.OrderBy(n => n, StringComparer.Ordinal), reales);
        }

        // Los parciales conservan su filtro y su unicidad; los de usuario ya no existen.
        void Parcial(string nombre, string filtro) => Assert.Equal(1, _db.Contar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $n AND sql LIKE 'CREATE UNIQUE INDEX%' AND sql LIKE $f",
            ("$n", nombre), ("$f", "%WHERE " + filtro)));
        Parcial("IX_Tacto_TactoAnuladoId", "TactoAnuladoId IS NOT NULL");
        Parcial("IX_MarcaPasada_MangaId_AnimalId", "Anulado = 0");
        Parcial("IX_CierreManga_MangaId", "Anulado = 0");
        Parcial("IX_Espera_AnimalId", "Estado = 'Pendiente'");
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE '%Usuario%'"));
    }

    [Fact]
    public void El_esquema_migrado_coincide_con_el_que_crea_EF_desde_el_modelo()
    {
        BaseMigrada();
        EsquemaAnterior.MigrarHasta(_db, null);   // y las migraciones posteriores (EventosDelCatalogo)

        // El oráculo: una base creada directamente desde el modelo actual (sin migraciones).
        using var oraculo = new BaseDeDatosDePrueba(migrar: false);
        using (var ctx = oraculo.NuevoContexto()) ctx.Database.EnsureCreated();
        SqliteConnection.ClearAllPools();

        var migrada = DescripcionEsquema.Describir(_db.Cruda);
        var delModelo = DescripcionEsquema.Describir(oraculo.Cruda);

        Assert.Equal(delModelo.Keys, migrada.Keys);
        foreach (var tabla in delModelo.Keys)
        {
            var faltan = delModelo[tabla].Except(migrada[tabla]).ToList();
            var sobran = migrada[tabla].Except(delModelo[tabla]).ToList();
            Assert.True(faltan.Count == 0 && sobran.Count == 0,
                $"Tabla {tabla}. Faltan en la migrada: [{string.Join("; ", faltan)}]. Sobran en la migrada: [{string.Join("; ", sobran)}].");
        }

        // Los triggers no los crea EnsureCreated: se comparan contra la lista explícita.
        Assert.Equal(TriggersEsperados.Concat(new[] { "TR_Rechazo_SoloEvento", "TR_Tacto_EstadoNoSoloEvento" }).OrderBy(t => t, StringComparer.Ordinal),
            DescripcionEsquema.Triggers(_db.Cruda));
    }

    // ---------- RF-14: claves foráneas ----------

    [Fact]
    public void Claves_foraneas_activas()
    {
        BaseVieja();
        Migrar();

        // Ninguna referencia rota.
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));

        // La conexión que usa la app las exige.
        using (var ctx = _db.NuevoContexto())
        {
            ctx.Database.OpenConnection();
            using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        Assert.Throws<SqliteException>(() => _db.Tacto(Guid.NewGuid(), Guid.NewGuid()));   // animal y estado inexistentes

        // La FK compuesta de Tacto (AnimalId, TactoAnuladoId) -> Tacto (AnimalId, Id) sigue existiendo. Ojo: una FK compuesta
        // aparece como DOS filas en pragma_foreign_key_list (una por columna).
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('Tacto') WHERE \"table\" = 'Tacto'"));
        // Y su clave alternativa (AnimalId, Id), la que esa FK necesita, también.
        Assert.Equal(1, _db.Contar(@"SELECT COUNT(*) FROM pragma_index_list('Tacto') il
            WHERE il.origin = 'u' AND il.""unique"" = 1 AND (SELECT group_concat(name) FROM (SELECT name FROM pragma_index_info(il.name) ORDER BY seqno)) = 'AnimalId,Id'"));
        // Las hijas de Manga apuntan a "Manga" (no a una tabla temporal).
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('CierreManga') WHERE \"table\" = 'Manga'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_list('MarcaPasada') WHERE \"table\" = 'Manga'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE sql LIKE '%_nueva%'"));
    }

    [Fact]
    public void Una_referencia_rota_en_cualquier_tabla_reconstruida_se_rechaza()
    {
        var p = BaseMigrada();
        var estado = Guid.Parse((string)Una("SELECT Id FROM EstadoReproductivo LIMIT 1"));
        var inexistente = Guid.NewGuid();
        var animal = _db.Animal(p.Rodeo1, "F-1");

        Rechaza(() => _db.Tacto(inexistente, estado, orden: Orden()));
        Rechaza(() => _db.Rechazo(inexistente, orden: Orden()));
        Rechaza(() => _db.Espera(inexistente));
        Rechaza(() => _db.Manga(inexistente, 1));
        Rechaza(() => _db.CierreManga(inexistente));
        Rechaza(() => _db.MarcaPasada(animal, inexistente, orden: Orden()));
        // La FK compuesta de Tacto: anular un tacto de OTRA vaca.
        Rechaza(() => _db.Tacto(animal, null, "Anulacion", p.TactoAnulado, orden: Orden()));
    }

    [Fact]
    public void Ningun_comando_de_la_migracion_apaga_foreign_keys()
    {
        BaseVieja();
        var interceptor = new InterceptorDeComandos();
        var opciones = new DbContextOptionsBuilder<Aviss.Infrastructure.Persistencia.AvissDbContext>();
        Aviss.Infrastructure.Persistencia.ConfiguracionDb.Configurar(opciones, _db.RutaDb);
        opciones.AddInterceptors(interceptor);
        using (var ctx = new Aviss.Infrastructure.Persistencia.AvissDbContext(opciones.Options))
            ctx.GetService<IMigrator>().Migrate();
        SqliteConnection.ClearAllPools();

        Assert.True(interceptor.Textos.Count > 50, "El interceptor tendría que haber visto los comandos de la migración.");
        // Se difiere la comprobación (vale solo dentro de la transacción) pero NO se apaga la exigencia (RF-14).
        Assert.Contains(interceptor.Textos, t => t.Contains("defer_foreign_keys", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(interceptor.Textos, t => Regex.IsMatch(t, @"PRAGMA\s+foreign_keys", RegexOptions.IgnoreCase));
        Assert.DoesNotContain(interceptor.Textos, t => Regex.IsMatch(t, @"foreign_keys\s*=\s*(OFF|0|FALSE|NO)\b", RegexOptions.IgnoreCase));
    }
}
