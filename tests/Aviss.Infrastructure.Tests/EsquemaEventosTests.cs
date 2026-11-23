using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Reglas de los eventos del catálogo que la base garantiza por sí sola (spec 005, RF-1, RF-2, RF-5, RF-16, RF-17).
/// Todo con SQL crudo, salteando la app.
/// </summary>
public sealed class EsquemaEventosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Un_estado_creado_sin_las_columnas_nuevas_queda_como_estado_no_principal_sin_clave()
    {
        var id = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG");

        Assert.Equal("Estado", _db.Escalar("SELECT Tipo FROM EstadoReproductivo WHERE Id = $id", ("$id", id)));
        Assert.Equal(0, _db.Contar("SELECT EsPrincipal FROM EstadoReproductivo WHERE Id = $id", ("$id", id)));
        Assert.Equal(0, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", id)));
        Assert.Null(_db.Escalar("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = $id", ("$id", id)));
    }

    [Theory]
    [InlineData("Tipo", "'Otro'")]
    [InlineData("EsPrincipal", "2")]
    [InlineData("Orden", "-1")]
    [InlineData("ClaveEstandar", "''")]
    [InlineData("ClaveEstandar", "'con espacio'")]
    [InlineData("ClaveEstandar", "'minuscula'")]
    public void Los_CHECK_de_las_columnas_nuevas_rechazan_valores_invalidos(string columna, string valor)
    {
        var id = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG");
        Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE EstadoReproductivo SET {columna} = {valor} WHERE Id = $id", ("$id", id)));
    }

    [Fact]
    public void La_clave_estandar_es_unica_pero_los_creados_a_mano_sin_clave_pueden_ser_varios()
    {
        _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK", principal: true, orden: 2);
        _db.Estado("A", "A", "A");
        _db.Estado("B", "B", "B"); // dos sin clave: permitido

        Assert.Throws<SqliteException>(() => _db.Estado("OK2", "OK2", "OK2", clave: "OK"));
    }

    // ---------- Triggers de inserción ----------

    private (Guid animal, Guid usuario) Base()
    {
        var rodeo = _db.Rodeo();
        return (_db.Animal(rodeo, "100"), _db.Usuario());
    }

    [Fact]
    public void Un_rechazo_acepta_evento_nulo_o_un_solo_evento_y_rechaza_un_estado()
    {
        var (animal, usuario) = Base();
        var soloEvento = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        var estado = _db.Estado("SECA", "SECA", "SECA");

        _db.Rechazo(animal, usuario);                                   // el Rechazo fijo: EventoId nulo
        _db.Rechazo(animal, usuario, eventoId: soloEvento);             // un solo evento del catálogo
        Assert.Throws<SqliteException>(() => _db.Rechazo(animal, usuario, eventoId: estado));
        Assert.Throws<SqliteException>(() => _db.Rechazo(animal, usuario, eventoId: Guid.NewGuid())); // ni existe (FK)
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Rechazo"));
    }

    [Fact]
    public void Un_tacto_normal_no_puede_apuntar_a_un_solo_evento_pero_si_a_un_estado()
    {
        var (animal, usuario) = Base();
        var soloEvento = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "SoloEvento", clave: "ABORTO");
        var estado = _db.Estado("SECA", "SECA", "SECA");

        _db.Tacto(animal, usuario, estado);
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, usuario, soloEvento));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }

    [Fact]
    public void La_anulacion_de_un_tacto_viejo_hacia_un_solo_evento_adoptado_no_se_bloquea()
    {
        // La anulación no deja la vaca en ese estado: es un tacto de tipo Anulacion, el trigger solo mira los Normal.
        var (animal, usuario) = Base();
        var estado = _db.Estado("ABORTO", "Aborto", "ABORTO");
        var original = _db.Tacto(animal, usuario, estado);                       // hecho cuando todavía era un estado
        _db.Ejecutar("UPDATE EstadoReproductivo SET Tipo = 'SoloEvento' WHERE Id = $id", ("$id", estado)); // adoptado

        _db.Tacto(animal, usuario, null, tipo: "Anulacion", tactoAnuladoId: original);
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Tacto"));
    }

    // ---------- Inmutabilidad de Rechazo con EventoId ----------

    [Fact]
    public void No_se_puede_cambiar_el_evento_de_un_rechazo_ni_al_anularlo()
    {
        var (animal, usuario) = Base();
        var ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "SoloEvento", clave: "ABORTO");
        var rechazo = _db.Rechazo(animal, usuario, eventoId: ok);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET EventoId = $e WHERE Id = $id", ("$id", rechazo), ("$e", aborto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET EventoId = NULL WHERE Id = $id", ("$id", rechazo)));
        // Anular y de paso cambiar el evento: tampoco.
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, AnuladoPorUsuarioId = $u, EventoId = $e WHERE Id = $id",
            ("$id", rechazo), ("$e", aborto), ("$u", usuario), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora))));

        // Anular a secas sigue permitido y conserva el evento.
        Assert.Equal(1, _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, AnuladoPorUsuarioId = $u WHERE Id = $id",
            ("$id", rechazo), ("$u", usuario), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora))));
        Assert.Equal(ok.ToString().ToUpperInvariant(), _db.Escalar("SELECT EventoId FROM Rechazo WHERE Id = $id", ("$id", rechazo))?.ToString()?.ToUpperInvariant());
    }

    [Fact]
    public void Un_evento_del_catalogo_con_rechazos_no_se_puede_borrar()
    {
        var (animal, usuario) = Base();
        var ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        _db.Rechazo(animal, usuario, eventoId: ok);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", ok))); // FK Restrict
    }
}
