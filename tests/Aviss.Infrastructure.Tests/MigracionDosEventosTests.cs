using Aviss.Application.Puertos;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Backups;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 011 (RF-13, RF-23, constitución §7): una base de la versión anterior (con la migración "Exportaciones", con tactos,
/// rechazos, anulaciones e historial) se actualiza con la migración REAL "DosEventosPorTacto" a través del arranque: copia
/// previa, ninguna fila cambia (el evento 2 queda vacío), la lista COMPLETA de triggers es la misma salvo el texto de los
/// cuatro que se recrean, la base rechaza con SQL directo cada caso del RF-13 y, si la migración falla a medias, todo vuelve atrás.
/// </summary>
[Collection("Migraciones")] // comparten ClearAllPools y archivos: de a una
public sealed class MigracionDosEventosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    private readonly FakeReloj _reloj = new();
    private readonly FakeEspacioLibre _espacio = new();
    private readonly RutasAviss _rutas;

    public MigracionDosEventosTests() => _rutas = new RutasAviss(_db.Directorio);
    public void Dispose() => _db.Dispose();

    private ArranqueService Servicio()
        => new(_rutas, new MigradorEf(), new BackupService(_rutas, _reloj, _espacio), new SembradorInicial(_reloj),
            new ArranqueResultado(), new LimpiezaDeRastros(_rutas, new LimpiezaPreferenciasNula()));

    private string[] Backups() => Directory.Exists(_rutas.DirectorioBackups)
        ? Directory.GetFiles(_rutas.DirectorioBackups, "*.db") : [];

    private sealed record Datos(Guid Animal, Guid Seca, Guid Aborto, Guid Tacto, Guid TactoAnulado, Guid Anulacion, Guid Rechazo, Guid RechazoAborto);

    /// <summary>La base como la dejó la spec 007 (migración "Exportaciones"): tactos, una anulación, rechazos e historial.</summary>
    private Datos BaseAnterior()
    {
        _db.MigrarHasta("Exportaciones");

        var vacia = _db.Estado("VACIA", "VACIA", "VACIA", tipo: "Estado", clave: "VACIA");
        var seca = _db.Estado("SECA", "SECA", "SECA", tipo: "Estado", clave: "SECA");
        var ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
        var animal = _db.Animal(_db.Rodeo(), "100", "Importado", estadoId: seca);
        var tacto = _db.Tacto(animal, vacia);
        var tactoAnulado = _db.Tacto(animal, seca, anulado: true);
        var anulacion = _db.Tacto(animal, vacia, tipo: "Anulacion", tactoAnuladoId: tactoAnulado);
        _db.Tacto(animal, vacia, eventoId: aborto, diasAborto: 20);
        var rechazo = _db.Rechazo(animal, "motivo", eventoId: ok);
        var rechazoAborto = _db.Rechazo(animal, eventoId: aborto, diasAborto: 30);
        _db.Rechazo(animal, "anulado", anulado: true);
        _db.Historial("Tacto");
        _db.Historial("RechazoCreado");

        _db.CerrarCruda();   // el arranque necesita el archivo sin conexiones abiertas
        return new Datos(animal, seca, aborto, tacto, tactoAnulado, anulacion, rechazo, rechazoAborto);
    }

    /// <summary>Una huella de todo lo que había antes: cada fila de las tablas existentes, con sus columnas anteriores.</summary>
    private string FotoDeLoExistente()
        => string.Join("|",
            _db.Escalar("SELECT group_concat(Id || ':' || EstadoNuevoId || ':' || COALESCE(EventoId,'') || ':' || COALESCE(DiasAborto,'') || ':' || Anulado || ':' || Tipo || ':' || COALESCE(TactoAnuladoId,'') || ':' || COALESCE(Observaciones,''), ';') FROM (SELECT * FROM Tacto ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || COALESCE(EventoId,'') || ':' || COALESCE(DiasAborto,'') || ':' || Anulado || ':' || COALESCE(Observaciones,''), ';') FROM (SELECT * FROM Rechazo ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || Accion || ':' || ValorNuevo, ';') FROM (SELECT * FROM Historial ORDER BY Id)"),
            _db.Escalar("SELECT group_concat(Id || ':' || NumeroCaravana || ':' || COALESCE(EstadoReproductivoId,''), ';') FROM (SELECT * FROM Animal ORDER BY Id)"));

    /// <summary>Todos los triggers de la base con su texto, por nombre.</summary>
    private Dictionary<string, string> TriggersConTexto()
    {
        var d = new Dictionary<string, string>();
        using var cmd = _db.Cruda.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger'";
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    private static readonly string[] LosCuatro =
        ["TR_Rechazo_NoModificar", "TR_Rechazo_SoloEvento", "TR_Tacto_EventoValido", "TR_Tacto_NoModificar"];

    [Fact]
    public async Task Actualizar_hace_la_copia_previa_deja_todo_igual_con_el_evento_2_vacio_y_solo_cambian_los_cuatro_triggers()
    {
        var d = BaseAnterior();
        var foto = FotoDeLoExistente();
        var triggersAntes = TriggersConTexto();
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'EventoId'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'Evento2Id'"));
        _db.CerrarCruda();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.BaseActualizada, r.Tipo);
        Assert.True(r.PuedeAbrir);

        // La copia previa existe, está sana y es la base VIEJA (sin las columnas nuevas).
        var copia = Assert.Single(Backups());
        Assert.EndsWith("-previa-actualizacion.db", copia);
        using (var cx = new SqliteConnection($"Data Source={copia};Pooling=False;Mode=ReadOnly"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'Evento2Id'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // Las columnas e índices nuevos existen en las dos tablas.
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name IN ('Evento2Id','Observaciones2')"));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Rechazo') WHERE name IN ('Evento2Id','Observaciones2')"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Tacto_Evento2Id'"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Rechazo_Evento2Id'"));

        // Nada de lo existente cambió y todo quedó con el evento 2 vacío.
        Assert.Equal(foto, FotoDeLoExistente());
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Evento2Id IS NOT NULL OR Observaciones2 IS NOT NULL"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Evento2Id IS NOT NULL OR Observaciones2 IS NOT NULL"));
        Assert.Equal(4, _db.Contar("SELECT COUNT(*) FROM Tacto"));
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Historial"));

        // LISTA COMPLETA de triggers antes y después: no desaparece ninguno, no aparece ninguno y solo cambia el texto de los cuatro.
        var triggersDespues = TriggersConTexto();
        Assert.Equal(triggersAntes.Keys.OrderBy(k => k, StringComparer.Ordinal), triggersDespues.Keys.OrderBy(k => k, StringComparer.Ordinal));
        var cambiaron = triggersAntes.Keys.Where(k => triggersAntes[k] != triggersDespues[k]).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(LosCuatro, cambiaron);
        // Los que NO se tocan, por nombre (los tres que nombra la spec y los de Historial).
        foreach (var intacto in new[] { "TR_Tacto_EstadoNoSoloEvento", "TR_Tacto_NoBorrar", "TR_Rechazo_NoBorrar", "TR_Historial_NoModificar", "TR_Historial_NoBorrar" })
            Assert.Equal(triggersAntes[intacto], triggersDespues[intacto]);

        // Lo viejo sigue protegido: los registros de antes siguen siendo inmutables y se anulan como siempre.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET DiasAborto = 5 WHERE Id = $r", ("$r", d.RechazoAborto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Observaciones2 = 'x' WHERE Id = $t", ("$t", d.Tacto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Tacto WHERE Id = $t", ("$t", d.Tacto)));
        Assert.Equal(1, _db.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $t", ("$t", d.Tacto)));
    }

    [Fact]
    public async Task Dos_arranques_seguidos_no_cambian_nada()
    {
        BaseAnterior();
        await Servicio().IniciarAsync();
        var foto = FotoDeLoExistente();
        var triggers = TriggersConTexto();
        _db.CerrarCruda();
        _reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.Correcto, r.Tipo);
        Assert.Single(Backups());   // no se hizo otra copia
        Assert.Equal(foto, FotoDeLoExistente());
        Assert.Equal(triggers, TriggersConTexto());
    }

    [Fact]
    public async Task Si_la_migracion_falla_a_medias_la_base_queda_como_estaba()
    {
        BaseAnterior();
        // Saboteo: un índice con el nombre de uno de los que crea la migración la hace fallar DESPUÉS de haber agregado las
        // columnas de Tacto y su índice. EF aplica la migración en una transacción: todo tiene que volver atrás.
        _db.Ejecutar("CREATE INDEX IX_Rechazo_Evento2Id ON Animal (Id);");
        var huellaEsquema = InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos);
        var foto = FotoDeLoExistente();
        _db.CerrarCruda();

        var r = await Servicio().IniciarAsync();

        Assert.Equal(TipoResultadoArranque.ActualizacionFallidaRestaurada, r.Tipo);
        Assert.False(r.PuedeAbrir);
        Assert.Equal(huellaEsquema, InspeccionSqlite.HuellaEsquema(_rutas.BaseDeDatos));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_table_info('Tacto') WHERE name = 'Evento2Id'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%DosEventosPorTacto'"));
        Assert.Equal(foto, FotoDeLoExistente());
    }

    [Fact]
    public void El_modelo_no_tiene_cambios_pendientes_respecto_de_las_migraciones()
    {
        _db.MigrarHasta(null);
        using var ctx = _db.NuevoContexto();
        // Lo mismo que "dotnet ef migrations has-pending-model-changes": el snapshot de la última migración coincide con el modelo.
        Assert.False(ctx.Database.HasPendingModelChanges());
    }

    [Fact]
    public void El_esquema_migrado_coincide_con_el_que_crea_EF_desde_el_modelo()
    {
        _db.MigrarHasta(null);
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
        // Y las piezas del evento 2 están, con su nombre.
        Assert.Contains(migrada["Tacto"], h => h.StartsWith("ck:CK_Tacto_Evento2Distinto|"));
        Assert.Contains(migrada["Tacto"], h => h.StartsWith("idx:IX_Tacto_Evento2Id|"));
        Assert.Contains(migrada["Rechazo"], h => h.StartsWith("ck:CK_Rechazo_Evento2ConEvento|"));
        Assert.Contains(migrada["Rechazo"], h => h.StartsWith("idx:IX_Rechazo_Evento2Id|"));
    }

    // ---------- La base, con SQL directo, después de migrar (RF-13) ----------

    private sealed record Catalogo(Guid Vacia, Guid Prenada, Guid Seca, Guid Insemin, Guid FechaPrevia, Guid Aborto,
        Guid Ok, Guid Revisar, Guid Prostag, Guid Dib, Guid Animal);

    private Catalogo BaseNueva()
    {
        _db.MigrarHasta(null);
        var vacia = _db.Estado("VACIA", "VACIA", "VACIA", tipo: "Estado", clave: "VACIA");
        var prenada = _db.Estado("PRENADA", "Preñada fecha correcta", "PREÑADA", tipo: "Estado", clave: "PRENADA");
        var seca = _db.Estado("SECA", "SECA", "SECA", tipo: "Estado", clave: "SECA");
        var insemin = _db.Estado("INSEMIN", "INSEMIN", "INSEMIN", tipo: "Estado", clave: "INSEMIN");
        var fechaPrevia = _db.Estado("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", tipo: "Estado",
            clave: "PRENADA_FECHA_PREVIA", pasos: "DiasPreniez", destinoId: prenada);
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
        var ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        var revisar = _db.Estado("REVISAR_NUEVAMENTE", "Revisar Nuevamente", "REVISAR NUEVAMENTE", tipo: "SoloEvento", clave: "REVISAR_NUEVAMENTE");
        var prostag = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG", tipo: "Estado");
        var dib = _db.Estado("DIB", "DIB", "DIB", tipo: "Estado");
        var animal = _db.Animal(_db.Rodeo(), "100");
        return new Catalogo(vacia, prenada, seca, insemin, fechaPrevia, aborto, ok, revisar, prostag, dib, animal);
    }

    [Fact]
    public void Un_tacto_de_un_solo_evento_sigue_insertando_y_uno_de_dos_eventos_tambien()
    {
        var c = BaseNueva();
        _db.Tacto(c.Animal, c.Vacia);                                                       // un evento, como siempre
        _db.Tacto(c.Animal, c.Vacia, eventoId: c.Prostag);                                  // un evento con evento elegido, como siempre
        _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Ok);                                      // VACIA + OK
        _db.Tacto(c.Animal, c.Insemin, evento2Id: c.Prostag, observaciones2: "dosis 2");    // INSEMIN + evento a mano
        _db.Tacto(c.Animal, c.Prostag, evento2Id: c.Ok);                                    // PROSTAG (a mano, cambia el estado) + OK
        _db.Tacto(c.Animal, c.Seca, evento2Id: c.Revisar);
        Assert.Equal(6, _db.Contar("SELECT COUNT(*) FROM Tacto"));
        Assert.Equal(4, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Evento2Id IS NOT NULL"));
    }

    [Fact]
    public void Un_tacto_rechaza_un_evento_2_igual_al_evento_1_real()
    {
        var c = BaseNueva();
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Prostag, evento2Id: c.Prostag));                      // el estado es el evento 1
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, eventoId: c.Prostag, evento2Id: c.Prostag));   // el EventoId es el evento 1
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Vacia));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }

    [Fact]
    public void Un_tacto_rechaza_un_evento_2_con_pasos_con_destino_o_estandar_de_estado()
    {
        var c = BaseNueva();
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Aborto));        // con pasos
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, evento2Id: c.FechaPrevia));   // con pasos y destino
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Prostag, evento2Id: c.Seca));        // estándar de estado
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Prostag, evento2Id: c.Insemin));     // estándar de estado
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Prostag, evento2Id: c.Prenada));     // estándar de estado
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }

    [Fact]
    public void Un_tacto_rechaza_un_evento_2_cuando_el_evento_1_tiene_pasos_o_destino()
    {
        var c = BaseNueva();
        // Aborto (con días) y Preñada fecha previa (con días): el evento 1 con pasos no admite un segundo.
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, eventoId: c.Aborto, diasAborto: 10, evento2Id: c.Ok));
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Prenada, eventoId: c.FechaPrevia, diasPreniezInformados: 100, evento2Id: c.Ok));
        // Aunque no vengan días (los pide otro trigger), el evento 2 tampoco entra.
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, eventoId: c.Aborto, evento2Id: c.Ok));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }

    [Fact]
    public void Una_anulacion_no_lleva_evento_2_ni_observacion_2()
    {
        var c = BaseNueva();
        var original = _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Ok);
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, null, tipo: "Anulacion", tactoAnuladoId: original, evento2Id: c.Ok));
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, null, tipo: "Anulacion", tactoAnuladoId: original, evento2Id: c.Ok, observaciones2: "x"));
        _db.Tacto(c.Animal, null, tipo: "Anulacion", tactoAnuladoId: original);   // a secas, sí
    }

    [Fact]
    public void Una_observacion_2_sin_evento_2_se_rechaza_en_las_dos_tablas()
    {
        var c = BaseNueva();
        Assert.Throws<SqliteException>(() => _db.Tacto(c.Animal, c.Vacia, observaciones2: "suelta"));
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, observaciones2: "suelta"));
    }

    [Fact]
    public void Un_tacto_guardado_no_cambia_el_evento_2_ni_la_observacion_2_ni_al_anularlo()
    {
        var c = BaseNueva();
        var t = _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Ok, observaciones2: "a");
        var sinEvento2 = _db.Tacto(c.Animal, c.Seca);

        foreach (var cambio in new[] { "Evento2Id = NULL", "Observaciones2 = 'b'", "Observaciones2 = NULL" })
        {
            Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Tacto SET {cambio} WHERE Id = $id", ("$id", t)));
            Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Tacto SET Anulado = 1, {cambio} WHERE Id = $id", ("$id", t)));
        }
        // Un tacto sin evento 2 tampoco puede ganarlo después.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Evento2Id = $e WHERE Id = $id", ("$id", sinEvento2), ("$e", c.Ok)));

        // Anular a secas sigue permitido y conserva los dos eventos.
        Assert.Equal(1, _db.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $id", ("$id", t)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $id AND Evento2Id = $e AND Observaciones2 = 'a'", ("$id", t), ("$e", c.Ok)));
    }

    [Fact]
    public void No_hay_tope_de_300_caracteres_en_la_base_se_acepta_una_observacion_de_301()
    {
        var c = BaseNueva();
        var larga = new string('x', 301);
        _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Ok, observaciones2: larga);
        _db.Rechazo(c.Animal, larga, eventoId: c.Ok, evento2Id: c.Prostag, observaciones2: larga);
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM (SELECT Observaciones2 FROM Tacto UNION ALL SELECT Observaciones2 FROM Rechazo) WHERE length(Observaciones2) = 301"));
    }

    [Fact]
    public void Un_registro_de_solo_evento_guarda_el_evento_2_y_sigue_aceptando_un_solo_evento()
    {
        var c = BaseNueva();
        _db.Rechazo(c.Animal);                                                                    // el Rechazo fijo, como siempre
        _db.Rechazo(c.Animal, eventoId: c.Ok);                                                    // un solo evento, como siempre
        _db.Rechazo(c.Animal, eventoId: c.Aborto, diasAborto: 30);                                // Aborto con resultado Rechazo, como siempre
        _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Prostag, observaciones2: "dosis");     // OK + evento a mano
        _db.Rechazo(c.Animal, eventoId: c.Revisar, evento2Id: c.Ok);                              // Revisar + OK
        Assert.Equal(5, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Evento2Id IS NOT NULL"));
    }

    [Fact]
    public void Un_registro_de_solo_evento_rechaza_los_casos_del_RF_13()
    {
        var c = BaseNueva();
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, evento2Id: c.Ok));                                    // el Rechazo fijo no combina
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Aborto, diasAborto: 30, evento2Id: c.Ok)); // un Aborto no combina
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Ok));                    // igual al evento 1
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Aborto));                // con pasos
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.FechaPrevia));           // con pasos y destino
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Vacia));                 // estándar de estado
        Assert.Throws<SqliteException>(() => _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Insemin));               // estándar de estado
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }

    [Fact]
    public void Un_registro_de_solo_evento_guardado_no_cambia_el_evento_2_ni_la_observacion_2()
    {
        var c = BaseNueva();
        var r = _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Prostag, observaciones2: "a");
        var f = BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora);

        foreach (var cambio in new[] { "Evento2Id = NULL", "Observaciones2 = 'b'", "Observaciones2 = NULL" })
        {
            Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Rechazo SET {cambio} WHERE Id = $id", ("$id", r)));
            Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, {cambio} WHERE Id = $id", ("$id", r), ("$f", f)));
        }
        Assert.Equal(1, _db.Ejecutar("UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id", ("$id", r), ("$f", f)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE Id = $id AND Evento2Id = $e AND Observaciones2 = 'a'", ("$id", r), ("$e", c.Prostag)));
    }

    [Fact]
    public void Un_evento_usado_como_evento_2_no_se_puede_borrar_del_catalogo()
    {
        var c = BaseNueva();
        _db.Tacto(c.Animal, c.Vacia, evento2Id: c.Dib);
        _db.Rechazo(c.Animal, eventoId: c.Ok, evento2Id: c.Prostag);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", c.Dib)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", c.Prostag)));
    }
}
