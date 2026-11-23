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

    private (Guid rodeo, Guid usuario, Guid animal) Base()
    {
        var rodeo = _db.Rodeo();
        return (rodeo, _db.Usuario(), _db.Animal(rodeo, "100"));
    }

    private string Ahora(int minutos = 1) => BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddMinutes(minutos));

    // ---------- Espera ----------

    [Fact]
    public void Espera_pendiente_puede_resolverse_una_sola_vez_y_despues_no_cambia()
    {
        var (_, usuario, animal) = Base();
        var espera = _db.Espera(animal, usuario);
        var registro = Guid.NewGuid();

        Assert.Equal(1, _db.Ejecutar(@"UPDATE Espera SET Estado = 'Resuelta', ResueltaEnUtc = $f, ResueltaPorUsuarioId = $u,
                RegistroResolucionId = $r, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora()), ("$u", usuario), ("$r", registro)));

        // Cerrada: no vuelve a pendiente, no se resuelve de nuevo ni se saltea.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Pendiente', ResueltaEnUtc = NULL, ResueltaPorUsuarioId = NULL, RegistroResolucionId = NULL WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', RegistroResolucionId = NULL WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET RegistroResolucionId = $r WHERE Id = $id", ("$id", espera), ("$r", Guid.NewGuid())));
        Assert.Equal("Resuelta", _db.Escalar("SELECT Estado FROM Espera WHERE Id = $id", ("$id", espera)));
    }

    [Fact]
    public void Espera_pendiente_puede_saltearse_sin_registro()
    {
        var (_, usuario, animal) = Base();
        var espera = _db.Espera(animal, usuario);
        Assert.Equal(1, _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f, ResueltaPorUsuarioId = $u, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora()), ("$u", usuario)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET Estado = 'Resuelta', RegistroResolucionId = $r WHERE Id = $id", ("$id", espera), ("$r", Guid.NewGuid())));
    }

    [Fact]
    public void Espera_no_se_borra_ni_se_modifica_fuera_de_pendiente_a_cerrada()
    {
        var (_, usuario, animal) = Base();
        var espera = _db.Espera(animal, usuario);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera"));
        // Cambiar datos de la creación, con o sin cerrar.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET FechaHoraUtc = $f WHERE Id = $id", ("$id", espera), ("$f", Ahora(30))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET UsuarioId = $u WHERE Id = $id", ("$id", espera), ("$u", _db.Usuario("Otro"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET AnimalId = $a WHERE Id = $id", ("$id", espera), ("$a", _db.Animal(_db.Rodeo(), "5"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET DispositivoId = $d WHERE Id = $id", ("$id", espera), ("$d", Guid.NewGuid())));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(@"UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f, ResueltaPorUsuarioId = $u, FechaHoraUtc = $f WHERE Id = $id",
            ("$id", espera), ("$f", Ahora()), ("$u", usuario)));
        // Pendiente que solo cambia ActualizadoEnUtc tampoco: la única modificación es cerrarla.
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET ActualizadoEnUtc = $f WHERE Id = $id", ("$id", espera), ("$f", Ahora())));
    }

    [Fact]
    public void Una_cerrada_no_se_borra()
    {
        var (_, usuario, animal) = Base();
        var espera = _db.Espera(animal, usuario, "Salteada");
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Espera WHERE Id = $id", ("$id", espera)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Espera SET UsuarioId = UsuarioId WHERE Id = $id", ("$id", espera)));
    }

    [Fact]
    public void Una_vaca_tiene_una_sola_espera_pendiente_pero_puede_repetirla_tras_cerrarla()
    {
        var (rodeo, usuario, animal) = Base();
        _db.Espera(animal, usuario);
        Assert.Throws<SqliteException>(() => _db.Espera(animal, usuario));

        // Otra vaca, sí.
        _db.Espera(_db.Animal(rodeo, "101"), usuario);

        // Cerrada la primera, puede ponerse de nuevo; y conviven varias cerradas.
        _db.Ejecutar("UPDATE Espera SET Estado = 'Salteada', ResueltaEnUtc = $f, ResueltaPorUsuarioId = $u WHERE AnimalId = $a",
            ("$a", animal), ("$f", Ahora()), ("$u", usuario));
        _db.Espera(animal, usuario);
        _db.Espera(animal, usuario, "Salteada");
        _db.Espera(animal, usuario, "Resuelta");
        Assert.Equal(5, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    [Fact]
    public void Espera_con_datos_incoherentes_se_rechaza()
    {
        var (_, usuario, animal) = Base();
        void Intentar(string estado, string? enUtc, Guid? por, Guid? registro)
            => Assert.Throws<SqliteException>(() => _db.Ejecutar(
                @"INSERT INTO Espera (Id, AnimalId, FechaHoraUtc, UsuarioId, DispositivoId, Estado, ResueltaEnUtc, ResueltaPorUsuarioId,
                      RegistroResolucionId, CreadoEnUtc, ActualizadoEnUtc)
                  VALUES ($id, $a, $f, $u, $d, $e, $re, $rp, $rr, $f, $f)",
                ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora()), ("$u", usuario), ("$d", Guid.NewGuid()),
                ("$e", estado), ("$re", enUtc), ("$rp", por), ("$rr", registro)));

        Intentar("Inventada", null, null, null);
        Intentar("Pendiente", Ahora(), usuario, null);            // pendiente con datos de resolución
        Intentar("Pendiente", null, null, Guid.NewGuid());
        Intentar("Resuelta", Ahora(), usuario, null);             // resuelta sin registro
        Intentar("Resuelta", null, null, Guid.NewGuid());         // resuelta sin quién ni cuándo
        Intentar("Salteada", Ahora(), usuario, Guid.NewGuid());   // salteada con registro
        Intentar("Salteada", null, null, null);                   // salteada sin quién ni cuándo
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    [Fact]
    public void Espera_con_animal_o_usuario_inexistente_se_rechaza()
    {
        var (_, usuario, animal) = Base();
        Assert.Throws<SqliteException>(() => _db.Espera(Guid.NewGuid(), usuario));
        Assert.Throws<SqliteException>(() => _db.Espera(animal, Guid.NewGuid()));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Espera"));
    }

    // ---------- Rechazo ----------

    [Fact]
    public void Rechazo_solo_acepta_pasar_Anulado_de_0_a_1()
    {
        var (_, usuario, animal) = Base();
        var rechazo = _db.Rechazo(animal, usuario, "cojera");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Observaciones = 'otra' WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET OrdenInsercion = 999 WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET FechaHoraUtc = $f WHERE Id = $id", ("$id", rechazo), ("$f", Ahora(60))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET UsuarioId = $u WHERE Id = $id", ("$id", rechazo), ("$u", _db.Usuario("Otro"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET AnimalId = $a WHERE Id = $id", ("$id", rechazo), ("$a", _db.Animal(_db.Rodeo(), "5"))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, AnuladoPorUsuarioId = $u, Observaciones = 'otra' WHERE Id = $id",
            ("$id", rechazo), ("$f", Ahora()), ("$u", usuario)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo"));

        Assert.Equal(1, _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, AnuladoPorUsuarioId = $u, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", rechazo), ("$f", Ahora()), ("$u", usuario)));
        Assert.Equal("cojera", _db.Escalar("SELECT Observaciones FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
    }

    [Fact]
    public void Un_rechazo_anulado_no_vuelve_a_vigente_ni_cambia()
    {
        var (_, usuario, animal) = Base();
        var rechazo = _db.Rechazo(animal, usuario, anulado: true);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 0, AnuladoEnUtc = NULL, AnuladoPorUsuarioId = NULL WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 1 WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET AnuladoEnUtc = $f WHERE Id = $id", ("$id", rechazo), ("$f", Ahora(9))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rechazo WHERE Id = $id", ("$id", rechazo)));
    }

    [Fact]
    public void Anular_un_rechazo_sin_quien_y_cuando_se_rechaza()
    {
        var (_, usuario, animal) = Base();
        var rechazo = _db.Rechazo(animal, usuario);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 1 WHERE Id = $id", ("$id", rechazo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id", ("$id", rechazo), ("$f", Ahora())));
        // Y no se puede insertar uno sin anular que ya traiga datos de anulación.
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, UsuarioId, DispositivoId, Anulado, AnuladoEnUtc, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, 777, $u, $d, 0, $f, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora()), ("$u", usuario), ("$d", Guid.NewGuid())));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            @"INSERT INTO Rechazo (Id, AnimalId, FechaHoraUtc, OrdenInsercion, UsuarioId, DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
              VALUES ($id, $a, $f, 778, $u, $d, 2, $f, $f)",
            ("$id", Guid.NewGuid()), ("$a", animal), ("$f", Ahora()), ("$u", usuario), ("$d", Guid.NewGuid())));
    }

    [Fact]
    public void Una_vaca_puede_tener_varios_rechazos_vigentes()
    {
        var (_, usuario, animal) = Base();
        _db.Rechazo(animal, usuario, "uno");
        _db.Rechazo(animal, usuario, "dos");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Rechazo WHERE AnimalId = $a AND Anulado = 0", ("$a", animal)));
    }

    [Fact]
    public void El_orden_de_guardado_de_los_rechazos_es_unico()
    {
        var (_, usuario, animal) = Base();
        _db.Rechazo(animal, usuario, orden: 50);
        Assert.Throws<SqliteException>(() => _db.Rechazo(animal, usuario, orden: 50));
    }

    [Fact]
    public void Rechazo_con_animal_o_usuario_inexistente_se_rechaza()
    {
        var (_, usuario, animal) = Base();
        Assert.Throws<SqliteException>(() => _db.Rechazo(Guid.NewGuid(), usuario));
        Assert.Throws<SqliteException>(() => _db.Rechazo(animal, Guid.NewGuid()));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }

    [Fact]
    public void Un_animal_con_espera_o_rechazo_no_se_puede_borrar()
    {
        var (_, usuario, animal) = Base();
        _db.Espera(animal, usuario);
        var otro = _db.Animal(_db.Rodeo(), "7");
        _db.Rechazo(otro, usuario);
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
        var id = _db.Historial(_db.Usuario(), accion);
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
        => Assert.NotEqual(Guid.Empty, _db.Historial(_db.Usuario(), accion));

    [Fact]
    public void El_historial_rechaza_otra_accion()
        => Assert.Throws<SqliteException>(() => _db.Historial(_db.Usuario(), "esperacreada")); // distingue mayúsculas

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
