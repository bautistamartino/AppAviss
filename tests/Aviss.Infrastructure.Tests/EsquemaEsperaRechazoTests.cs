using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Reglas de la espera y del rechazo que la base garantiza por sí sola (spec 004, RF-6, RF-12, RF-13).
/// Todo con SQL crudo, salteando la app.
/// </summary>
public sealed class EsquemaEsperaRechazoTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    private (Guid rodeo, Guid animal) Base()
    {
        var rodeo = _db.Rodeo();
        return (rodeo, _db.Animal(rodeo, "100"));
    }

    private string Ahora(int minutos = 1) => BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddMinutes(minutos));

    // ---------- Espera ----------

    [Fact]
    public void Espera_pendiente_puede_resolverse_una_sola_vez_y_despues_no_cambia()
    {
        var (_, animal) = Base();
        var espera = _db.Espera(animal);
        var registro = Guid.NewGuid();

        Assert.Equal(1, _db.Ejecutar(@"UPDATE Espera SET Estado = 'Resuelta', ResueltaEnUtc = $f,
                RegistroResolucionId = $r, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora()), ("$r", registro)));

        // Cerrada: no vuelve a pendiente, no se resuelve de nuevo ni se saltea.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Pendiente', ResueltaEnUtc = NULL, RegistroResolucionId = NULL WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', RegistroResolucionId = NULL WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET RegistroResolucionId = $r WHERE Id = $id", ("$id", espera), ("$r", Guid.NewGuid())));
        Assert.Equal("Resuelta", _db.Escalar("SELECT Estado FROM Espera WHERE Id = $id", ("$id", espera)));
    }

    [Fact]
    public void Espera_pendiente_puede_saltearse_sin_registro()
    {
        var (_, animal) = Base();
        var espera = _db.Espera(animal);
        Assert.Equal(1, _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora())));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Resuelta', RegistroResolucionId = $r WHERE Id = $id", ("$id", espera), ("$r", Guid.NewGuid())));
    }

    [Fact]
    public void Espera_no_se_borra_ni_se_modifica_fuera_de_pendiente_a_cerrada()
    {
        var (_, animal) = Base();
        var espera = _db.Espera(animal);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera"));
        // Cambiar datos de la creación, con o sin cerrar.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET FechaHoraUtc = $f WHERE Id = $id", ("$id", espera), ("$f", Ahora(30))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET AnimalId = $a WHERE Id = $id", ("$id", espera), ("$a", _db.Animal(_db.Rodeo(), "5"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(@"UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f, FechaHoraUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora())));
        // Pendiente que solo cambia ActualizadoEnUtc tampoco: la única modificación es cerrarla.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET ActualizadoEnUtc = $f WHERE Id = $id", ("$id", espera), ("$f", Ahora())));
    }

    [Fact]
    public void Una_cerrada_no_se_borra()
    {
        var (_, animal) = Base();
        var espera = _db.Espera(animal, "Salteada");
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", espera)));
    }

    [Fact]
    public void Una_vaca_tiene_una_sola_espera_pendiente_pero_puede_repetirla_tras_cerrarla()
    {
        var (rodeo, animal) = Base();
        _db.Espera(animal);
        Assert.Throws<SqliteException>(() => _db.Espera(animal));

        // Otra vaca, sí.
        _db.Espera(_db.Animal(rodeo, "101"));

        // Cerrada la primera, puede ponerse de nuevo; y conviven varias cerradas.
        _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f WHERE AnimalId = $a",
            ("$a", animal), ("$f", Ahora()));
        _db.Espera(animal);
        _db.Espera(animal, "Salteada");
        _db.Espera(animal, "Resuelta");
        Assert.Equal(5, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    [Fact]
    public void Espera_con_datos_incoherentes_se_rechaza()
    {
        var (_, animal) = Base();
        void Intentar(string estado, string? enUtc, Guid? registro)
            => Assert.Throws<SqliteException>(() => _db.Ejecutar(
                @"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, Estado, ResueltaEnUtc,
                      RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
                  VALUES ($id, $a, $f, $e, $re, $rr, $f, $f)",
                ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora()),
                ("$e", estado), ("$re", enUtc), ("$rr", registro)));

        Intentar("Inventada", null, null);
        Intentar("Pendiente", Ahora(), null);            // pendiente con datos de resolución
        Intentar("Pendiente", null, Guid.NewGuid());   // pendiente con registro
        Intentar("Resuelta", Ahora(), null);             // resuelta sin registro
        Intentar("Resuelta", null, Guid.NewGuid());   // resuelta sin fecha de resolución
        Intentar("Salteada", Ahora(), Guid.NewGuid());   // salteada con registro
        Intentar("Salteada", null, null);                       // salteada sin fecha de resolución
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    [Fact]
    public void Espera_con_animal_inexistente_se_rechaza()
    {
        Assert.Throws<SqliteException>(() => _db.Espera(Guid.NewGuid()));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    // ---------- Rechazo ----------

    [Fact]
    public void Rechazo_solo_acepta_pasar_Anulado_de_0_a_1()
    {
        var (_, animal) = Base();
        var rechazo = _db.Rechazo(animal, "cojera");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Observaciones = 'otra' WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET OrdenInsercion = 999 WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET FechaHoraUtc = $f WHERE Id = $id", ("$id", rechazo), ("$f", Ahora(60))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET AnimalId = $a WHERE Id = $id", ("$id", rechazo), ("$a", _db.Animal(_db.Rodeo(), "5"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, Observaciones = 'otra' WHERE Id = $id",
            ("$id", rechazo), ("$f", Ahora())));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo"));

        Assert.Equal(1, _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", rechazo), ("$f", Ahora())));
        Assert.Equal("cojera", _db.Escalar("SELECT Observaciones FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
    }

    [Fact]
    public void Un_rechazo_anulado_no_vuelve_a_vigente_ni_cambia()
    {
        var (_, animal) = Base();
        var rechazo = _db.Rechazo(animal, anulado: true);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 0, AnuladoEnUtc = NULL WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 1 WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET AnuladoEnUtc = $f WHERE Id = $id", ("$id", rechazo), ("$f", Ahora(9))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
    }

    [Fact]
    public void Anular_un_rechazo_sin_fecha_de_anulacion_se_rechaza()
    {
        var (_, animal) = Base();
        var rechazo = _db.Rechazo(animal);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 1 WHERE Id = $id", ("$id", rechazo)));
        // Y no se puede insertar uno sin anular que ya traiga datos de anulación.
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Anulado, AnuladoEnUtc, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, 777, 0, $f, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora())));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, 778, 2, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora())));
    }

    [Fact]
    public void Una_vaca_puede_tener_varios_rechazos_vigentes()
    {
        var (_, animal) = Base();
        _db.Rechazo(animal, "uno");
        _db.Rechazo(animal, "dos");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE AnimalId = $a AND Anulado = 0", ("$a", animal)));
    }

    [Fact]
    public void El_orden_de_guardado_de_los_rechazos_es_unico()
    {
        var (_, animal) = Base();
        _db.Rechazo(animal, orden: 50);
        Assert.Throws<SqliteException>(() => _db.Rechazo(animal, orden: 50));
    }

    [Fact]
    public void Rechazo_con_animal_inexistente_se_rechaza()
    {
        var (_, animal) = Base();
        Assert.Throws<SqliteException>(() => _db.Rechazo(Guid.NewGuid()));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }

    [Fact]
    public void Un_animal_con_espera_o_rechazo_no_se_puede_borrar()
    {
        var (_, animal) = Base();
        _db.Espera(animal);
        var otro = _db.Animal(_db.Rodeo(), "7");
        _db.Rechazo(otro);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Animal WHERE Id = $a", ("$a", animal)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Animal WHERE Id = $a", ("$a", otro)));
    }

    // ---------- Historial: las cinco acciones nuevas y los triggers vivos ----------

    [Theory]
    [InlineData("EsperaCreada")]
    [InlineData("EsperaResuelta")]
    [InlineData("EsperaSalteada")]
    [InlineData("RechazoCreado")]
    [InlineData("RechazoAnulado")]
    public void El_historial_acepta_las_acciones_nuevas_y_no_se_puede_modificar_ni_borrar(string accion)
    {
        var id = _db.Historial(accion);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'otro' WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", id)));
    }

    [Theory]
    [InlineData("Alta")]
    [InlineData("Tacto")]
    [InlineData("MarcaPasada")]
    [InlineData("MangaAbierta")]
    [InlineData("AnulacionCierre")]
    public void El_historial_sigue_aceptando_las_acciones_anteriores(string accion)
        => Assert.NotEqual(Guid.Empty, _db.Historial(accion));

    [Fact]
    public void El_historial_rechaza_otra_accion()
        => Assert.Throws<SqliteException>(() => _db.Historial("esperacreada")); // distingue mayúsculas

    [Theory]
    [InlineData("TR_Historial_NoModificar")]
    [InlineData("TR_Historial_NoBorrar")]
    [InlineData("TR_Espera_NoModificar")]
    [InlineData("TR_Espera_NoBorrar")]
    [InlineData("TR_Rechazo_NoModificar")]
    [InlineData("TR_Rechazo_NoBorrar")]
    public void Existen_los_triggers(string nombre)
        => Assert.True(_db.ExisteTrigger(nombre));
}
