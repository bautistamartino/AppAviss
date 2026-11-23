using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Reglas que la base garantiza por sí sola. Todo se prueba con SQL crudo, salteando la app (RF-2, 3, 4, 4b, 7b, 11, 12, 15, 16, 24).
/// </summary>
public sealed class EsquemaBaseTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();

    public void Dispose() => _db.Dispose();

    // ---------- Establecimiento y rodeo (RF-1, RF-2) ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Establecimiento_con_nombre_vacio_se_rechaza(string nombre)
        => Assert.Throws<SqliteException>(() => _db.Establecimiento(nombre));

    [Fact]
    public void Rodeo_con_hash_que_no_tiene_64_caracteres_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Rodeo(hash: new string('a', 63)));

    [Fact]
    public void Rodeo_con_archivo_vacio_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Rodeo(largoBlob: 0));

    [Fact]
    public void Rodeo_con_estado_invalido_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Rodeo(estado: "Cerrado"));

    [Fact]
    public void Rodeo_sin_campos_obligatorios_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Ejecutar(
            "INSERT INTO Rodeo (Id, EstablecimientoId, Nombre, CreadoEnUtc, ActualizadoEnUtc) VALUES ($id, $e, 'x', 'a', 'a')",
            ("$id", Guid.NewGuid()), ("$e", _db.Establecimiento())));

    [Fact]
    public void Dos_rodeos_con_la_misma_huella_se_permiten()
    {
        var est = _db.Establecimiento();
        _db.Rodeo(est);
        _db.Rodeo(est);
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Rodeo"));
    }

    [Theory]
    [InlineData("Abierto")]
    [InlineData("Exportado")]
    public void Rodeo_acepta_los_dos_estados(string estado)
        => Assert.NotEqual(Guid.Empty, _db.Rodeo(estado: estado));

    // ---------- Caravana (RF-4) ----------

    [Fact]
    public void Caravana_repetida_en_el_mismo_rodeo_se_rechaza()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, "1234");
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, "1234"));
    }

    [Theory]
    [InlineData("abc", "ABC")]
    [InlineData("ñandú", "ÑANDÚ")]
    [InlineData("Ñ1", "ñ1")]
    public void Caravana_repetida_con_otra_capitalizacion_se_rechaza(string primera, string segunda)
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, primera);
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, segunda));
    }

    [Fact]
    public void Ceros_a_la_izquierda_son_otra_caravana()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, "0123");
        _db.Animal(rodeo, "123");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Animal"));
    }

    [Fact]
    public void Mismo_numero_en_otro_rodeo_es_valido()
    {
        var est = _db.Establecimiento();
        _db.Animal(_db.Rodeo(est), "55");
        _db.Animal(_db.Rodeo(est), "55");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Animal"));
    }

    [Fact]
    public void Animal_anulado_libera_su_numero()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, "77", anulado: true);
        _db.Animal(rodeo, "77");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Animal WHERE NumeroCaravana = '77'"));
    }

    [Fact]
    public void Caravana_de_20_caracteres_se_acepta_y_de_21_se_rechaza()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, new string('9', 20));
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, new string('9', 21)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    public void Caravana_vacia_o_con_espacios_en_los_extremos_se_rechaza(string caravana)
        => Assert.Throws<SqliteException>(() => _db.Animal(_db.Rodeo(), caravana));

    [Theory]
    [InlineData("abc")]
    [InlineData("ñandú")]
    [InlineData("Ñ")]
    [InlineData("0123")]
    [InlineData("áéíóúü")]
    [InlineData("ÁÉÍÓÚÜ")]
    [InlineData("A-b_9")]
    [InlineData("çÇ")]
    public void Clave_de_Caravana_en_C_coincide_con_la_columna_generada(string texto)
    {
        var id = _db.Animal(_db.Rodeo(), texto);
        var clave = (string?)_db.Escalar("SELECT NumeroCaravanaClave FROM Animal WHERE Id = $id", ("$id", id));
        Assert.Equal(Caravana.CalcularClave(texto), clave);
    }

    // ---------- Identificador electrónico (RF-4b) ----------

    [Fact]
    public void Id_electronico_repetido_en_el_rodeo_se_rechaza()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, "1", idElectronico: "982000123");
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, "2", idElectronico: "982000123"));
    }

    [Fact]
    public void Id_electronico_repetido_se_permite_en_otro_rodeo_y_con_anulados()
    {
        var est = _db.Establecimiento();
        var r1 = _db.Rodeo(est);
        _db.Animal(r1, "1", idElectronico: "982000123", anulado: true);
        _db.Animal(r1, "2", idElectronico: "982000123");
        _db.Animal(_db.Rodeo(est), "3", idElectronico: "982000123");
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM Animal"));
    }

    [Fact]
    public void Id_electronico_nulo_puede_repetirse()
    {
        var rodeo = _db.Rodeo();
        _db.Animal(rodeo, "1");
        _db.Animal(rodeo, "2");
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM Animal WHERE IdElectronico IS NULL"));
    }

    [Fact]
    public void Id_electronico_con_espacios_o_de_mas_de_32_se_rechaza()
    {
        var rodeo = _db.Rodeo();
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, "1", idElectronico: "982 000"));
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, "2", idElectronico: new string('1', 33)));
        _db.Animal(rodeo, "3", idElectronico: new string('1', 32));
    }

    // ---------- Catálogo de estados (RF-5) ----------

    [Fact]
    public void Codigo_interno_repetido_se_rechaza()
    {
        _db.Estado("SECA", "SECA", "SECA");
        Assert.Throws<SqliteException>(() => _db.Estado("SECA", "OTRA", "OTRA"));
    }

    [Theory]
    [InlineData("seca")]
    [InlineData("PREÑADA")]
    [InlineData("MUY SECA")]
    [InlineData("")]
    public void Codigo_interno_con_formato_invalido_se_rechaza(string codigo)
        => Assert.Throws<SqliteException>(() => _db.Estado(codigo, "Nombre", "DC"));

    [Fact]
    public void Codigo_interno_valido_con_guion_bajo_y_numeros_se_acepta()
        => _db.Estado("EN_PROCESO_2", "En proceso", "EP2");

    [Theory]
    [InlineData("preñada")]
    [InlineData("PREÑADA")]
    public void Nombre_repetido_con_otra_capitalizacion_o_Ñ_se_rechaza(string segundo)
    {
        _db.Estado("PRENADA", "PREÑADA", "PREÑADA");
        Assert.Throws<SqliteException>(() => _db.Estado("OTRA", segundo, "OTRO"));
    }

    [Fact]
    public void Codigo_de_Dairy_Comp_es_unico_solo_entre_activos()
    {
        _db.Estado("A", "A", "DC1", activo: false);
        _db.Estado("B", "B", "DC1");                                    // el anterior está inactivo: se puede repetir
        Assert.Throws<SqliteException>(() => _db.Estado("C", "C", "DC1")); // ya hay uno activo
        _db.Estado("D", "D", "DC1", activo: false);                     // otro inactivo: sigue valiendo
    }

    // ---------- Historial (RF-11) ----------

    [Fact]
    public void Historial_no_se_puede_modificar_ni_borrar()
    {
        var id = _db.Historial();

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Historial SET ValorNuevo = 'otro' WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial WHERE Id = $id", ("$id", id)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Historial"));

        Assert.Equal("x", _db.Escalar("SELECT ValorNuevo FROM Historial WHERE Id = $id", ("$id", id)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Historial"));
    }

    [Fact]
    public void Historial_con_accion_invalida_se_rechaza()
    {
        Assert.Throws<SqliteException>(() => _db.Historial(accion: "Borrado"));
    }

    // ---------- Tacto (RF-12, 13, 14, 16) ----------

    private (Guid animal, Guid estado) EscenarioTacto()
    {
        var rodeo = _db.Rodeo();
        return (_db.Animal(rodeo, "100"), _db.Estado("SECA", "SECA", "SECA"));
    }

    [Fact]
    public void Tacto_no_se_puede_borrar_ni_modificar()
    {
        var (animal, estado) = EscenarioTacto();
        var tacto = _db.Tacto(animal, estado, diasPrenez: 40);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Tacto WHERE Id = $id", ("$id", tacto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET DiasPrenez = 41 WHERE Id = $id", ("$id", tacto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Observaciones = 'x' WHERE Id = $id", ("$id", tacto)));

        Assert.Equal(40L, _db.Escalar("SELECT DiasPrenez FROM Tacto WHERE Id = $id", ("$id", tacto)));
    }

    [Fact]
    public void Solo_se_permite_pasar_Anulado_de_0_a_1_con_su_fecha_de_modificacion()
    {
        var (animal, estado) = EscenarioTacto();
        var tacto = _db.Tacto(animal, estado);

        var n = _db.Ejecutar("UPDATE Tacto SET Anulado = 1, ActualizadoEnUtc = $f WHERE Id = $id",
            ("$id", tacto), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora.AddMinutes(1))));
        Assert.Equal(1, n);

        // volver a 0 está prohibido
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Anulado = 0 WHERE Id = $id", ("$id", tacto)));
        Assert.Equal(1L, _db.Escalar("SELECT Anulado FROM Tacto WHERE Id = $id", ("$id", tacto)));
    }

    [Fact]
    public void Anular_junto_con_cambiar_otro_dato_se_rechaza()
    {
        var (animal, estado) = EscenarioTacto();
        var tacto = _db.Tacto(animal, estado, diasPrenez: 40);

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Anulado = 1, DiasPrenez = 99 WHERE Id = $id", ("$id", tacto)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Anulado = 1, Observaciones = 'x' WHERE Id = $id", ("$id", tacto)));
        Assert.Equal(0L, _db.Escalar("SELECT Anulado FROM Tacto WHERE Id = $id", ("$id", tacto)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    [InlineData(-1, false)]
    public void Dias_de_prenez_entre_0_y_300(int dias, bool valido)
    {
        var (animal, estado) = EscenarioTacto();
        if (valido) _db.Tacto(animal, estado, diasPrenez: dias);
        else Assert.Throws<SqliteException>(() => _db.Tacto(animal, estado, diasPrenez: dias));
    }

    [Fact]
    public void Tacto_normal_sin_estado_nuevo_se_rechaza_pero_la_anulacion_puede_no_tenerlo()
    {
        var (animal, estado) = EscenarioTacto();
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, null));

        var original = _db.Tacto(animal, estado);
        _db.Tacto(animal, null, tipo: "Anulacion", tactoAnuladoId: original);
    }

    [Fact]
    public void Tipo_Anulacion_exige_tacto_anulado_y_Normal_lo_prohibe()
    {
        var (animal, estado) = EscenarioTacto();
        var original = _db.Tacto(animal, estado);

        Assert.Throws<SqliteException>(() => _db.Tacto(animal, estado, tipo: "Anulacion"));
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, estado, tipo: "Normal", tactoAnuladoId: original));
    }

    [Fact]
    public void Una_anulacion_no_puede_marcarse_como_anulada()
    {
        var (animal, estado) = EscenarioTacto();
        var original = _db.Tacto(animal, estado);
        var anulacion = _db.Tacto(animal, estado, tipo: "Anulacion", tactoAnuladoId: original);

        // el CHECK impide Tipo=Anulacion con Anulado=1 (RF-14)
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Tacto SET Anulado = 1 WHERE Id = $id", ("$id", anulacion)));
    }

    [Fact]
    public void Un_tacto_se_anula_una_sola_vez()
    {
        var (animal, estado) = EscenarioTacto();
        var original = _db.Tacto(animal, estado);
        _db.Tacto(animal, estado, tipo: "Anulacion", tactoAnuladoId: original);
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, estado, tipo: "Anulacion", tactoAnuladoId: original));
    }

    [Fact]
    public void La_anulacion_solo_puede_apuntar_a_un_tacto_del_mismo_animal()
    {
        var rodeo = _db.Rodeo();
        var estado = _db.Estado("SECA", "SECA", "SECA");
        var a1 = _db.Animal(rodeo, "1");
        var a2 = _db.Animal(rodeo, "2");
        var tactoDeA1 = _db.Tacto(a1, estado);

        Assert.Throws<SqliteException>(() => _db.Tacto(a2, estado, tipo: "Anulacion", tactoAnuladoId: tactoDeA1));
        _db.Tacto(a1, estado, tipo: "Anulacion", tactoAnuladoId: tactoDeA1); // mismo animal: válido
    }

    [Fact]
    public void Orden_de_insercion_es_unico()
    {
        var (animal, estado) = EscenarioTacto();
        _db.Tacto(animal, estado, orden: 500);
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, estado, orden: 500));
    }

    [Fact]
    public void Tacto_con_referencias_inexistentes_se_rechaza()
    {
        var (animal, estado) = EscenarioTacto();
        Assert.Throws<SqliteException>(() => _db.Tacto(Guid.NewGuid(), estado));
        Assert.Throws<SqliteException>(() => _db.Tacto(animal, Guid.NewGuid()));
    }

    // ---------- Animal (RF-3, RF-15, RF-25) ----------

    [Fact]
    public void Animal_no_se_puede_borrar()
    {
        var animal = _db.Animal(_db.Rodeo(), "1");
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Animal WHERE Id = $id", ("$id", animal)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Animal"));
    }

    [Fact]
    public void Origen_rodeo_y_datos_originales_no_cambian_nunca()
    {
        var rodeo = _db.Rodeo();
        var importado = _db.Animal(rodeo, "10", origen: "Importado");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET Origen = 'AltaEnCampo' WHERE Id = $id", ("$id", importado)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET DatosOriginalesJson = '{\"x\":1}' WHERE Id = $id", ("$id", importado)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET RodeoId = $r WHERE Id = $id", ("$id", importado), ("$r", _db.Rodeo())));
        Assert.Equal("Importado", _db.Escalar("SELECT Origen FROM Animal WHERE Id = $id", ("$id", importado)));
    }

    [Fact]
    public void Caravana_de_un_importado_no_cambia_pero_la_de_un_alta_en_campo_si()
    {
        var rodeo = _db.Rodeo();
        var importado = _db.Animal(rodeo, "10", origen: "Importado");
        var alta = _db.Animal(rodeo, "11");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET NumeroCaravana = '99' WHERE Id = $id", ("$id", importado)));
        Assert.Equal(1, _db.Ejecutar("UPDATE Animal SET NumeroCaravana = '99' WHERE Id = $id", ("$id", alta)));
    }

    [Fact]
    public void Observaciones_e_id_electronico_de_un_importado_si_se_editan()
    {
        var importado = _db.Animal(_db.Rodeo(), "10", origen: "Importado");
        Assert.Equal(1, _db.Ejecutar("UPDATE Animal SET Observaciones = 'ok', IdElectronico = '123' WHERE Id = $id", ("$id", importado)));
    }

    [Fact]
    public void Anulado_solo_va_de_0_a_1_y_solo_en_altas_en_campo()
    {
        var rodeo = _db.Rodeo();
        var alta = _db.Animal(rodeo, "1");
        var importado = _db.Animal(rodeo, "2", origen: "Importado");

        Assert.Equal(1, _db.Ejecutar("UPDATE Animal SET Anulado = 1, AnuladoEnUtc = $f WHERE Id = $id", ("$id", alta), ("$f", BaseDeDatosDePrueba.Fecha(_db.Reloj.UtcAhora))));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET Anulado = 0 WHERE Id = $id", ("$id", alta)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("UPDATE Animal SET Anulado = 1 WHERE Id = $id", ("$id", importado)));
        Assert.Throws<SqliteException>(() => _db.Animal(rodeo, "3", origen: "Importado", anulado: true));
    }

    [Fact]
    public void Origen_invalido_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Animal(_db.Rodeo(), "1", origen: "Otro"));

    // ---------- Integridad referencial (RF-16) ----------

    [Fact]
    public void Animal_sin_rodeo_y_rodeo_sin_establecimiento_se_rechazan()
    {
        Assert.Throws<SqliteException>(() => _db.Animal(Guid.NewGuid(), "1"));
        Assert.Throws<SqliteException>(() => _db.Rodeo(Guid.NewGuid()));
    }

    [Fact]
    public void Animal_con_estado_inexistente_se_rechaza()
        => Assert.Throws<SqliteException>(() => _db.Animal(_db.Rodeo(), "1", estadoId: Guid.NewGuid()));

    [Fact]
    public void Borrar_algo_con_dependientes_se_rechaza_y_no_arrastra()
    {
        var est = _db.Establecimiento();
        var rodeo = _db.Rodeo(est);
        _db.Animal(rodeo, "1");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Establecimiento WHERE Id = $id", ("$id", est)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rodeo WHERE Id = $id", ("$id", rodeo)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Animal"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM Rodeo"));
    }

    // ---------- Sin borrados (RF-24) ----------

    [Fact]
    public void Establecimiento_rodeo_y_estado_no_se_pueden_borrar_ni_sin_dependientes()
    {
        var est = _db.Establecimiento();
        var rodeo = _db.Rodeo();
        var estado = _db.Estado("SECA", "SECA", "SECA");

        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Establecimiento WHERE Id = $id", ("$id", est)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM Rodeo WHERE Id = $id", ("$id", rodeo)));
        Assert.Throws<SqliteException>(() => _db.Ejecutar("DELETE FROM EstadoReproductivo WHERE Id = $id", ("$id", estado)));
    }

    [Fact]
    public void Dar_de_baja_con_Activo_en_0_si_se_puede()
    {
        var est = _db.Establecimiento();
        Assert.Equal(1, _db.Ejecutar("UPDATE Establecimiento SET Activo = 0 WHERE Id = $id", ("$id", est)));
    }
}
