using Aviss.Application.Importacion;
using Aviss.Domain.Entidades;

namespace Aviss.Application.Tests.Importacion;

/// <summary>Spec 010: el validador decide el EID de cada animal (RF-1 a RF-9, RF-19). El EID nunca excluye una fila.</summary>
public class ValidadorImportacionEidTests
{
    private static readonly DateTime Ahora = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly EstadoReproductivo[] Catalogo = [EstadoReproductivo.Crear("PRENADA", "Preñada", "PREÑADA", Ahora)];

    private static readonly string[] Encabezados = ["ID", "EID", "RPRO"];

    private static FilaLeida Fila(int n, string id, CeldaLeida? eid)
    {
        var celdas = new Dictionary<string, CeldaLeida> { ["ID"] = CeldaLeida.DeTexto(id), ["RPRO"] = CeldaLeida.DeTexto("PREÑADA") };
        if (eid is not null) celdas["EID"] = eid;
        return new FilaLeida(n, celdas);
    }

    private static FilaLeida Fila(int n, string id, string eid) => Fila(n, id, CeldaLeida.DeTexto(eid));

    private static LecturaExcel Lectura(IReadOnlyList<FilaLeida> filas, string? columnaEid = "EID")
        => new(Encabezados, "ID", "RPRO", [], filas, null, columnaEid);

    private static ResultadoValidacion Validar(LecturaExcel l) => ValidadorImportacion.Validar(l, Catalogo);

    private static List<AdvertenciaImportacion> DeEid(ResultadoValidacion r)
        => r.Advertencias.Where(a => a.Tipo is TipoAdvertencia.EidInvalido or TipoAdvertencia.EidRepetido).ToList();

    // ---------- RF-1, RF-2 ----------

    [Fact]
    public void El_animal_queda_con_el_eid_sin_espacios()
    {
        var r = Validar(Lectura([Fila(2, "100", "999 000 111111111"), Fila(3, "101", CeldaLeida.DeNumero(999000222222222d))]));

        Assert.Equal(["999000111111111", "999000222222222"], r.Validas.Select(v => v.IdElectronico));
        Assert.True(r.HayColumnaEid);
        Assert.Empty(DeEid(r));
    }

    // ---------- RF-3 ----------

    [Fact]
    public void Guion_vacio_o_celda_ausente_entran_sin_eid_y_sin_advertencia()
    {
        var r = Validar(Lectura([Fila(2, "100", "-"), Fila(3, "101", ""), Fila(4, "102", "   "), Fila(5, "103", (CeldaLeida?)null)]));

        Assert.Equal(4, r.Validas.Count);
        Assert.All(r.Validas, v => Assert.Null(v.IdElectronico));
        Assert.Empty(DeEid(r));
        Assert.Equal(0, r.ConEid);
        Assert.Equal(4, r.SinEid);
    }

    // ---------- RF-4 ----------

    [Fact]
    public void Eid_invalido_se_descarta_el_animal_entra_y_hay_una_advertencia_agrupada_sin_el_valor()
    {
        var filas = new List<FilaLeida>
        {
            Fila(2, "100", "98200045846712"),       // 14 dígitos
            Fila(3, "101", "98200045846712A"),      // letra
            Fila(4, "102", "9820004584671234"),     // 16
            Fila(5, "103", "999000333333333"),      // bueno
            Fila(6, "104", "ABC"),
            Fila(7, "105", "xyz"),
            Fila(8, "106", "123"),
        };
        var r = Validar(Lectura(filas));

        Assert.Equal(7, r.Validas.Count);                       // nadie queda afuera (RF-6)
        Assert.Empty(r.Excluidas);
        Assert.Equal("999000333333333", r.Validas.Single(v => v.NumeroFila == 5).IdElectronico);
        Assert.All(r.Validas.Where(v => v.NumeroFila != 5), v => Assert.Null(v.IdElectronico));

        var a = Assert.Single(DeEid(r));
        Assert.Equal(TipoAdvertencia.EidInvalido, a.Tipo);
        Assert.Equal(6, a.Cantidad);
        Assert.Equal([2, 3, 4, 6, 7], a.FilasEjemplo);          // a lo sumo 5 ejemplos, por número de fila
        Assert.Null(a.Valor);
        // RF-19: el mensaje no muestra ningún valor.
        Assert.DoesNotContain("9820004584671", a.Mensaje);
        Assert.DoesNotContain("ABC", a.Mensaje);
    }

    // ---------- RF-5 ----------

    [Fact]
    public void Eid_repetido_deja_a_todos_los_que_lo_comparten_sin_eid_y_avisa()
    {
        var r = Validar(Lectura([
            Fila(2, "100", "999000444444444"),
            Fila(3, "101", "999000444444444"),
            Fila(4, "102", "999000444444444"),
            Fila(5, "103", "999000555555555"),
            Fila(6, "104", "999000666666666"),
            Fila(7, "105", "999000666666666"),
        ]));

        Assert.Equal(6, r.Validas.Count);
        Assert.Equal([5], r.Validas.Where(v => v.IdElectronico is not null).Select(v => v.NumeroFila));
        var a = Assert.Single(DeEid(r));
        Assert.Equal(TipoAdvertencia.EidRepetido, a.Tipo);
        Assert.Equal(5, a.Cantidad);                              // 3 + 2 filas
        Assert.Equal([2, 3, 4, 6, 7], a.FilasEjemplo);
        Assert.DoesNotContain("999000", a.Mensaje);
        Assert.Equal(1, r.ConEid);
        Assert.Equal(5, r.SinEid);
    }

    [Fact]
    public void El_mismo_eid_con_y_sin_espacios_cuenta_como_repetido()
    {
        var r = Validar(Lectura([Fila(2, "100", "999 000 777777777"), Fila(3, "101", CeldaLeida.DeNumero(999000777777777d))]));

        Assert.All(r.Validas, v => Assert.Null(v.IdElectronico));
        Assert.Equal(TipoAdvertencia.EidRepetido, Assert.Single(DeEid(r)).Tipo);
    }

    [Fact]
    public void Eid_distintos_no_avisan()
    {
        var r = Validar(Lectura([Fila(2, "100", "999000111111111"), Fila(3, "101", "999000111111112")]));
        Assert.Empty(DeEid(r));
        Assert.Equal(2, r.ConEid);
    }

    [Fact]
    public void Un_eid_invalido_y_uno_repetido_a_la_vez_dan_las_dos_advertencias_en_orden_estable()
    {
        var r = Validar(Lectura([
            Fila(2, "100", "999000111111111"), Fila(3, "101", "999000111111111"), Fila(4, "102", "corto"),
        ]));

        Assert.Equal([TipoAdvertencia.EidInvalido, TipoAdvertencia.EidRepetido], DeEid(r).Select(a => a.Tipo));
    }

    // ---------- RF-6 ----------

    [Fact]
    public void Una_fila_excluida_por_id_no_cuenta_para_el_eid_repetido()
    {
        // La fila 3 tiene ID vacío (excluida) y el mismo EID que la 2: la 2 conserva su EID.
        var r = Validar(Lectura([Fila(2, "100", "999000888888888"), Fila(3, "", "999000888888888")]));

        Assert.Equal("999000888888888", Assert.Single(r.Validas).IdElectronico);
        Assert.Equal(MotivoFila.IdVacio, Assert.Single(r.Excluidas).Motivo);
        Assert.Empty(DeEid(r));
    }

    [Fact]
    public void Un_eid_invalido_en_una_fila_excluida_por_id_no_genera_advertencia()
    {
        var r = Validar(Lectura([Fila(2, "100", "999000888888888"), Fila(3, "", "basura")]));
        Assert.Empty(DeEid(r));
    }

    [Fact]
    public void Un_id_repetido_excluye_a_las_dos_filas_y_no_deja_eid_repetido_con_una_tercera()
    {
        var r = Validar(Lectura([
            Fila(2, "100", "999000111111111"), Fila(3, "100", "999000222222222"), Fila(4, "101", "999000111111111"),
        ]));

        Assert.Equal(2, r.Excluidas.Count);                       // las dos filas con ID 100
        Assert.Equal("999000111111111", Assert.Single(r.Validas).IdElectronico);
        Assert.Empty(DeEid(r));
    }

    // ---------- RF-7 ----------

    [Fact]
    public void Sin_columna_eid_todo_sigue_como_antes()
    {
        var r = Validar(Lectura([Fila(2, "100", "999000111111111"), Fila(3, "101", "999000111111111")], columnaEid: null));

        Assert.False(r.HayColumnaEid);
        Assert.All(r.Validas, v => Assert.Null(v.IdElectronico));
        Assert.Empty(DeEid(r));
        Assert.Equal(0, r.ConEid);
        Assert.Equal(0, r.SinEid);
    }

    // ---------- RF-8 ----------

    [Fact]
    public void Conteos_con_y_sin_eid_cuentan_invalidos_y_repetidos_como_sin_eid()
    {
        var r = Validar(Lectura([
            Fila(2, "1", "999000000000001"), Fila(3, "2", "999000000000002"),   // con EID
            Fila(4, "3", "-"),                                                  // guion
            Fila(5, "4", "mal"),                                                // inválido
            Fila(6, "5", "999000000000009"), Fila(7, "6", "999000000000009"),   // repetidos
        ]));

        Assert.Equal(2, r.ConEid);
        Assert.Equal(4, r.SinEid);
        Assert.Equal(r.Validas.Count, r.ConEid + r.SinEid);
    }

    // ---------- RF-9 ----------

    [Fact]
    public void El_json_original_conserva_el_valor_crudo_del_eid_aunque_se_descarte()
    {
        var r = Validar(Lectura([Fila(2, "100", "corto")]));
        Assert.Contains("\"EID\":\"corto\"", r.Validas[0].DatosOriginalesJson);
    }
}
