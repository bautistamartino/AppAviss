using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Lo que la base garantiza por sí sola sobre los eventos con pasos (spec 005, segunda vuelta: RF-23, RF-24, RF-25, RF-26,
/// RF-28, RF-29, RF-30). Todo con SQL crudo, salteando la app: las reglas valen aunque el intento no venga de ella.
/// </summary>
public sealed class EsquemaPasosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    // Los eventos, creados a mano con las mismas columnas que dejaría el sembrador.
    private readonly Guid _vacia, _prenada, _insemin, _seca, _fresca, _noinsem, _fechaPrevia, _aborto, _ok, _comun;
    private readonly Guid _animal;

    public EsquemaPasosTests()
    {
        _vacia = _db.Estado("VACIA", "VACIA", "VACIA", tipo: "Estado", clave: "VACIA");
        _prenada = _db.Estado("PRENADA", "Preñada fecha correcta", "PREÑADA", tipo: "Estado", clave: "PRENADA");
        _insemin = _db.Estado("INSEMIN", "INSEMIN", "INSEMIN", tipo: "Estado", clave: "INSEMIN");
        _seca = _db.Estado("SECA", "SECA", "SECA", tipo: "Estado", clave: "SECA");
        _fresca = _db.Estado("FRESCA", "FRESCA", "FRESCA", tipo: "Estado", clave: "FRESCA");
        _noinsem = _db.Estado("NOINSEM", "NOINSEM", "NOINSEM", tipo: "Estado", clave: "NOINSEM");
        _fechaPrevia = _db.Estado("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", tipo: "Estado",
            clave: "PRENADA_FECHA_PREVIA", pasos: "DiasPreniez", destinoId: _prenada);
        _aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
        _ok = _db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
        _comun = _db.Estado("PROSTAG", "PROSTAG", "PROSTAG");
        _animal = _db.Animal(_db.Rodeo(), "100");
    }

    // ---------- Columnas de EstadoReproductivo ----------

    [Theory]
    [InlineData("''")]
    [InlineData("'Dias Aborto'")]      // espacio
    [InlineData("'Dias1'")]            // dígito
    [InlineData("'Dias;Aborto'")]
    public void El_CHECK_de_Pasos_rechaza_valores_invalidos(string valor)
        => Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE EstadoReproductivo SET Pasos = {valor} WHERE Id = $id", ("$id", _comun)));

    [Fact]
    public void Pasos_admite_nulo_y_una_lista_de_nombres_separados_por_coma_sin_fijar_los_valores()
    {
        _db.Ejecutar("UPDATE EstadoReproductivo SET Pasos = 'UnPasoFuturo,OtroMas' WHERE Id = $id", ("$id", _comun));
        _db.Ejecutar("UPDATE EstadoReproductivo SET Pasos = NULL WHERE Id = $id", ("$id", _comun));
    }

    [Fact]
    public void El_destino_tiene_que_ser_un_estado_existente()
    {
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE EstadoReproductivo SET EstadoDestinoId = $d WHERE Id = $id", ("$id", _comun), ("$d", Guid.NewGuid())));
        // Un estado que es destino de otro no se puede borrar (FK Restrict).
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", _prenada)));
    }

    // ---------- Rangos y combinaciones de Tacto ----------

    [Theory]
    [InlineData(0)]
    [InlineData(401)]
    [InlineData(-3)]
    public void Los_dias_de_preniez_fuera_de_1_a_400_se_rechazan(int dias)
        => Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _prenada, eventoId: _fechaPrevia, diasPreniezInformados: dias));

    [Theory]
    [InlineData(1)]
    [InlineData(130)]
    [InlineData(400)]
    public void Los_dias_de_preniez_de_1_a_400_se_aceptan_en_Preniada_fecha_previa(int dias)
    {
        _db.Tacto(_animal, _prenada, eventoId: _fechaPrevia, diasPreniezInformados: dias);
        Assert.Equal(dias, _db.Contar("SELECT DiasPreniezInformados FROM Tacto WHERE EventoId = $e", ("$e", _fechaPrevia)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(401)]
    public void Los_dias_de_aborto_fuera_de_0_a_400_se_rechazan(int dias)
        => Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _aborto, diasAborto: dias));

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void Los_dias_de_aborto_0_y_400_se_aceptan(int dias)
    {
        _db.Tacto(_animal, _vacia, eventoId: _aborto, diasAborto: dias);
        Assert.Equal(dias, _db.Contar("SELECT DiasAborto FROM Tacto WHERE EventoId = $e", ("$e", _aborto)));
    }

    [Fact]
    public void Un_tacto_no_informa_dias_de_preniez_y_de_aborto_a_la_vez()
        => Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _aborto, diasPreniezInformados: 10, diasAborto: 10));

    [Fact]
    public void Los_dias_necesitan_un_evento()
    {
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, diasAborto: 10));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _prenada, diasPreniezInformados: 10));
    }

    [Fact]
    public void Una_anulacion_no_lleva_evento_ni_dias()
    {
        var original = _db.Tacto(_animal, _vacia, eventoId: _aborto, diasAborto: 30);
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, null, tipo: "Anulacion", tactoAnuladoId: original, eventoId: _aborto));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, null, tipo: "Anulacion", tactoAnuladoId: original, eventoId: _aborto, diasAborto: 30));
        _db.Tacto(_animal, null, tipo: "Anulacion", tactoAnuladoId: original);   // a secas, sí
    }

    // ---------- Validez del evento (trigger de inserción) ----------

    [Fact]
    public void Un_tacto_no_puede_apuntar_a_un_solo_evento()
        => Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _ok));

    [Fact]
    public void Preniada_fecha_previa_exige_destino_preniada_y_dias()
    {
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _fechaPrevia, diasPreniezInformados: 100));   // destino equivocado
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _prenada, eventoId: _fechaPrevia));                              // faltan los días
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _prenada, eventoId: _fechaPrevia, diasAborto: 10));             // días de otro tipo
        _db.Tacto(_animal, _prenada, eventoId: _fechaPrevia, diasPreniezInformados: 100);
    }

    [Fact]
    public void Los_dias_solo_se_aceptan_si_el_evento_los_pide()
    {
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _comun, diasAborto: 10));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _aborto, diasPreniezInformados: 10));
        _db.Tacto(_animal, _vacia, eventoId: _comun);   // un evento sin pasos puede ir como evento, sin días
    }

    [Fact]
    public void Un_aborto_exige_dias_y_termina_solo_en_Vacia_Inseminada_Seca_o_Fresca()
    {
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _vacia, eventoId: _aborto));                       // faltan los días
        foreach (var final in new[] { _vacia, _insemin, _seca, _fresca })
            _db.Tacto(_animal, final, eventoId: _aborto, diasAborto: 20);
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _prenada, eventoId: _aborto, diasAborto: 20));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _noinsem, eventoId: _aborto, diasAborto: 20));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _comun, eventoId: _aborto, diasAborto: 20));
        Assert.Equal(4, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE EventoId = $e", ("$e", _aborto)));
    }

    [Fact]
    public void Un_evento_con_pasos_o_con_destino_nunca_es_el_estado_en_que_queda_la_vaca()
    {
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _aborto));
        Assert.Throws<SqliteException>(() => _db.Tacto(_animal, _fechaPrevia));
        _db.Tacto(_animal, _prenada);   // un tacto común a un estado común sigue como siempre
    }

    // ---------- Inmutabilidad ----------

    [Theory]
    [InlineData("EventoId = NULL")]
    [InlineData("DiasAborto = 31")]
    [InlineData("DiasAborto = NULL")]
    [InlineData("DiasPreniezInformados = 5")]
    public void Un_tacto_guardado_no_cambia_el_evento_ni_los_dias_ni_al_anularlo(string cambio)
    {
        var t = _db.Tacto(_animal, _vacia, eventoId: _aborto, diasAborto: 30);

        Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Tacto SET {cambio} WHERE Id = $id", ("$id", t)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar($"UPDATE Tacto SET Anulado = 1, {cambio} WHERE Id = $id", ("$id", t)));

        // Anular a secas sigue permitido y conserva evento y días.
        Assert.Equal(1, _db.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $id", ("$id", t)));
        Assert.Equal(30, _db.Contar("SELECT DiasAborto FROM Tacto WHERE Id = $id", ("$id", t)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Tacto WHERE Id = $id AND EventoId = $e", ("$id", t), ("$e", _aborto)));
    }

    [Fact]
    public void Un_evento_del_catalogo_con_tactos_no_se_puede_borrar()
    {
        _db.Tacto(_animal, _vacia, eventoId: _aborto, diasAborto: 30);
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", _aborto)));
    }

    // ---------- Rechazo con días de aborto ----------

    [Fact]
    public void Un_Aborto_con_resultado_Rechazo_guarda_los_dias_y_no_se_modifica()
    {
        var r = _db.Rechazo(_animal, eventoId: _aborto, diasAborto: 45);

        Assert.Equal(45, _db.Contar("SELECT DiasAborto FROM Rechazo WHERE Id = $id", ("$id", r)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET DiasAborto = 46 WHERE Id = $id", ("$id", r)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Rechazo SET DiasAborto = NULL WHERE Id = $id", ("$id", r)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar(
            "UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f, DiasAborto = 46 WHERE Id = $id",
            ("$id", r), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora))));
        Assert.Equal(1, _db.Ejecutar("UPDATE Rechazo SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id",
            ("$id", r), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora))));
        Assert.Equal(45, _db.Contar("SELECT DiasAborto FROM Rechazo WHERE Id = $id", ("$id", r)));
    }

    [Fact]
    public void Un_rechazo_con_dias_solo_vale_para_el_Aborto()
    {
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _ok, diasAborto: 10));       // solo evento común
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _comun, diasAborto: 10));    // estado cualquiera
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _fechaPrevia, diasAborto: 10));
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, diasAborto: 10));                      // el Rechazo fijo: sin evento
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _aborto));                   // un Aborto nuevo exige días
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _aborto, diasAborto: 401));
        Assert.Throws<SqliteException>(() => _db.Rechazo(_animal, eventoId: _aborto, diasAborto: -1));
        _db.Rechazo(_animal, eventoId: _aborto, diasAborto: 0);
        _db.Rechazo(_animal, eventoId: _ok);                                                             // lo de siempre sigue igual
        _db.Rechazo(_animal);
    }

    [Fact]
    public void Un_Aborto_viejo_sin_dias_en_una_base_sin_Aborto_estandar_sigue_aceptando_solo_eventos()
    {
        // Compatibilidad con la primera vuelta: un solo evento (por ejemplo un Aborto que todavía es SoloEvento) sin días.
        var viejo = _db.Estado("ABORTO_VIEJO", "Aborto viejo", "ABORTO VIEJO", tipo: "SoloEvento", clave: "ABORTO_VIEJO");
        _db.Rechazo(_animal, eventoId: viejo);
    }
}
