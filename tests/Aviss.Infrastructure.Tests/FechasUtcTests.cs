using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>Fechas en UTC, identificadores globales, orden de tactos y mapeo completo de las entidades (RF-17, RF-18).</summary>
public sealed class FechasUtcTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    private DateTime Ahora => _db.Reloj.UtcAhora;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void El_instante_leido_es_identico_y_de_Kind_Utc()
    {
        // fracción de segundo a propósito: tiene que sobrevivir completa
        var instante = new DateTime(2026, 11, 16, 15, 30, 45, DateTimeKind.Utc).AddTicks(1234567);
        _db.Reloj.Fijar(instante);

        Guid id;
        using (var ctx = _db.NuevoContexto())
        {
            var est = Establecimiento.Crear("Tambo", null, _db.Reloj.UtcAhora);
            id = est.Id;
            ctx.Add(est);
            ctx.SaveChanges();
        }
        using (var ctx = _db.NuevoContexto())
        {
            var leido = ctx.Set<Establecimiento>().Single(e => e.Id == id);
            Assert.Equal(instante, leido.CreadoEnUtc);
            Assert.Equal(DateTimeKind.Utc, leido.CreadoEnUtc.Kind);
            Assert.Equal(instante, leido.ActualizadoEnUtc);
        }
    }

    [Fact]
    public void La_fecha_se_guarda_como_texto_ISO_de_ancho_fijo_con_Z()
    {
        _db.Reloj.Fijar(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var id = _db.Establecimiento();
        var texto = (string?)_db.Escalar("SELECT CreadoEnUtc FROM Establecimiento WHERE Id = $id", ("$id", id));
        Assert.Equal("2026-01-02T03:04:05.0000000Z", texto);
    }

    [Fact]
    public void El_convertidor_rechaza_fechas_que_no_son_UTC()
    {
        Assert.Throws<ArgumentException>(() => ConvertidorFechaUtc.ATexto(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => ConvertidorFechaUtc.ATexto(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void El_convertidor_siempre_devuelve_Kind_Utc()
    {
        var fecha = ConvertidorFechaUtc.ADateTime("2026-11-16T12:00:00.0000000Z");
        Assert.Equal(DateTimeKind.Utc, fecha.Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), fecha);
    }

    [Fact]
    public void Todas_las_tablas_tienen_CreadoEnUtc_y_las_editables_ActualizadoEnUtc()
    {
        var sinActualizado = new HashSet<string> { "Historial", "Manga" }; // solo agregar: ni siquiera se anulan
        var tablas = new List<string>();
        using (var cmd = _db.Cruda.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '\\_\\_%' ESCAPE '\\' AND name NOT LIKE 'sqlite_%'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tablas.Add(r.GetString(0));
        }

        Assert.Equal(11, tablas.Count); // spec 004 suma Espera y Rechazo; la spec 006 quita Usuario
        foreach (var tabla in tablas)
        {
            Assert.Equal(1, _db.Contar($"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = 'CreadoEnUtc'"));
            var esperado = sinActualizado.Contains(tabla) ? 0 : 1;
            Assert.Equal(esperado, _db.Contar($"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = 'ActualizadoEnUtc'"));
        }
    }

    [Fact]
    public void ActualizadoEnUtc_cambia_al_editar()
    {
        Guid id;
        using (var ctx = _db.NuevoContexto())
        {
            var est = Establecimiento.Crear("Tambo", null, Ahora);
            id = est.Id;
            ctx.Add(est);
            ctx.SaveChanges();
        }
        _db.Reloj.Avanzar(TimeSpan.FromHours(2));
        var despues = Ahora;
        using (var ctx = _db.NuevoContexto())
        {
            var est = ctx.Set<Establecimiento>().Single(e => e.Id == id);
            est.DarDeBaja(despues);
            ctx.SaveChanges();
        }
        using (var ctx = _db.NuevoContexto())
        {
            var est = ctx.Set<Establecimiento>().Single(e => e.Id == id);
            Assert.Equal(despues, est.ActualizadoEnUtc);
            Assert.True(est.CreadoEnUtc < est.ActualizadoEnUtc);
            Assert.False(est.Activo);
        }
    }

    [Fact]
    public void Los_ids_son_guid_distintos()
    {
        var ids = Enumerable.Range(0, 50).Select(_ => _db.Establecimiento($"T{Guid.NewGuid()}")).ToList();
        Assert.Equal(50, ids.Distinct().Count());
        Assert.Equal(50, _db.Contar("SELECT COUNT(DISTINCT Id) FROM Establecimiento"));
    }

    [Fact]
    public void Dos_tactos_con_la_misma_fecha_se_ordenan_por_orden_de_insercion()
    {
        var rodeo = _db.Rodeo();
        var animal = _db.Animal(rodeo, "1");
        var estado = _db.Estado("SECA", "SECA", "SECA");
        var fecha = Ahora;
        var segundo = _db.Tacto(animal, estado, fechaHora: fecha, orden: 20);
        var primero = _db.Tacto(animal, estado, fechaHora: fecha, orden: 10);

        var ids = new List<Guid>();
        using var cmd = _db.Cruda.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Tacto ORDER BY FechaHoraUtc, OrdenInsercion";
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetGuid(0));
        Assert.Equal(new[] { primero, segundo }, ids);
    }

    [Fact]
    public void Todas_las_entidades_se_guardan_y_se_leen_de_vuelta_por_EF()
    {
        var est = Establecimiento.Crear("Tambo Sur", "notas", Ahora);
        var rodeo = Rodeo.Crear(est.Id, "Rodeo 1", Ahora, "r.xls", new string('b', 64), new byte[] { 1, 2, 3 }, "DairyComp", Ahora);
        var estado = EstadoReproductivo.Crear("PRENADA", "PREÑADA", "PREÑADA", Ahora);
        var animal = Animal.CrearImportado(rodeo.Id, Caravana.Crear("ñ12"), "982 000", estado.Id, "obs", "{\"ID\":\"ñ12\"}", Ahora);
        var tacto = Tacto.Registrar(animal.Id, Ahora, 1, null, estado.Id, 45, "ok", Ahora);
        var historial = Historial.Crear("Tacto", tacto.Id, "Estado", null, estado.Id.ToString(), Ahora, AccionHistorial.Tacto);

        using (var ctx = _db.NuevoContexto())
        {
            ctx.AddRange(est, rodeo, estado);
            ctx.SaveChanges();
            ctx.AddRange(animal);
            ctx.SaveChanges();
            ctx.AddRange(tacto, historial);
            ctx.SaveChanges();
        }

        using (var ctx = _db.NuevoContexto())
        {
            Assert.Equal("notas", ctx.Set<Establecimiento>().Single().Notas);
            var r = ctx.Set<Rodeo>().Single();
            Assert.Equal(new byte[] { 1, 2, 3 }, r.ArchivoOrigenBlob);
            Assert.Equal(EstadoRodeo.Abierto, r.Estado);
            var e = ctx.Set<EstadoReproductivo>().Single();
            Assert.Equal("PREÑADA", e.Nombre);
            var a = ctx.Set<Animal>().Single();
            Assert.Equal("982000", a.IdElectronico);
            Assert.Equal(OrigenAnimal.Importado, a.Origen);
            Assert.Equal(estado.Id, a.EstadoReproductivoId);
            var t = ctx.Set<Tacto>().Single();
            Assert.Equal(45, t.DiasPrenez);
            Assert.Null(t.EstadoAnteriorId);
            Assert.Equal(TipoTacto.Normal, t.Tipo);
            Assert.Equal(AccionHistorial.Tacto, ctx.Set<Historial>().Single().Accion);
        }

        // La columna generada la calcula la base (EF no la escribe) y coincide con la regla de C#
        Assert.Equal("PREÑADA", _db.Escalar("SELECT NombreClave FROM EstadoReproductivo"));
        Assert.Equal("Ñ12", _db.Escalar("SELECT NumeroCaravanaClave FROM Animal"));
    }

    [Fact]
    public void Un_tacto_de_anulacion_con_estado_nuevo_vacio_se_guarda_por_EF()
    {
        var est = Establecimiento.Crear("T", null, Ahora);
        var rodeo = Rodeo.Crear(est.Id, "R", Ahora, "r.xls", new string('b', 64), new byte[] { 1 }, "DairyComp", Ahora);
        var estado = EstadoReproductivo.Crear("SECA", "SECA", "SECA", Ahora);
        var animal = Animal.CrearAltaEnCampo(rodeo.Id, Caravana.Crear("1"), null, null, Ahora);
        var original = Tacto.Registrar(animal.Id, Ahora, 1, null, estado.Id, null, null, Ahora);
        var anulacion = Tacto.CrearAnulacion(original, estado.Id, Ahora, 2, Ahora);

        using var ctx = _db.NuevoContexto();
        ctx.AddRange(est, rodeo, estado);
        ctx.SaveChanges();
        ctx.Add(animal);
        ctx.SaveChanges();
        ctx.Add(original);
        ctx.SaveChanges();
        ctx.Add(anulacion);
        original.MarcarAnulado(Ahora);
        ctx.SaveChanges(); // el trigger permite 0 -> 1 con ActualizadoEnUtc

        Assert.Equal(1L, _db.Escalar("SELECT Anulado FROM Tacto WHERE Id = $id", ("$id", original.Id)));
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }
}
