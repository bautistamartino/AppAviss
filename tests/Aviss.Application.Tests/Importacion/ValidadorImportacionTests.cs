using System.Text.Json;
using Aviss.Application.Importacion;
using Aviss.Domain.Entidades;

namespace Aviss.Application.Tests.Importacion;

public class ValidadorImportacionTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly EstadoReproductivo Prenada = EstadoReproductivo.Crear("PRENADA", "Preñada", "PREÑADA", Ahora);
    private static readonly EstadoReproductivo Insemin = EstadoReproductivo.Crear("INSEMIN", "Inseminada", "INSEMIN", Ahora);
    private static readonly EstadoReproductivo Seca = EstadoReproductivo.Crear("SECA", "Seca", "SECA", Ahora);

    private static EstadoReproductivo[] Catalogo() => [Prenada, Insemin, Seca];

    private static EstadoReproductivo Baja(string codigo, string dc)
    {
        var e = EstadoReproductivo.Crear(codigo, codigo, dc, Ahora);
        e.Desactivar(Ahora);
        return e;
    }

    private static readonly string[] Encabezados = ["ID", "CVET", "FPART", "LACT", "DEL", "FCELO", "VINS", "UTORO", "COM", "RPRO", "FAPAR"];

    private static FilaLeida Fila(int n, CeldaLeida id, string? rpro = null, Dictionary<string, CeldaLeida>? extra = null)
    {
        var celdas = new Dictionary<string, CeldaLeida> { ["ID"] = id };
        if (rpro is not null) celdas["RPRO"] = CeldaLeida.DeTexto(rpro);
        if (extra is not null) foreach (var kv in extra) celdas[kv.Key] = kv.Value;
        return new FilaLeida(n, celdas);
    }

    private static FilaLeida Fila(int n, string id, string? rpro = null) => Fila(n, CeldaLeida.DeTexto(id), rpro);

    private static LecturaExcel Lectura(IReadOnlyList<FilaLeida> filas, int? total = null, string[]? faltantes = null, string[]? encabezados = null)
        => new(encabezados ?? Encabezados, "ID", "RPRO", faltantes ?? [], filas, total);

    private static ResultadoValidacion Validar(LecturaExcel l, params EstadoReproductivo[] estados)
        => ValidadorImportacion.Validar(l, estados.Length == 0 ? Catalogo() : estados);

    // ---------- Límites del archivo ----------

    [Fact]
    public void Sin_filas_de_datos_rechaza_el_archivo()
    {
        var r = Validar(Lectura([]));
        Assert.Equal(MotivoRechazoArchivo.SinFilasDeDatos, r.Rechazo);
    }

    [Fact]
    public void Mas_de_20000_filas_rechaza_el_archivo_y_20000_se_aceptan()
    {
        var ok = Enumerable.Range(2, LimitesImportacion.MaxFilas).Select(i => Fila(i, i.ToString())).ToList();
        Assert.Null(Validar(Lectura(ok)).Rechazo);

        var demas = ok.Append(Fila(99999, "x")).ToList();
        Assert.Equal(MotivoRechazoArchivo.ExcedeFilas, Validar(Lectura(demas)).Rechazo);
    }

    // ---------- Filas: ID, repetidos, precedencia ----------

    [Fact]
    public void Filas_validas_pasan_y_conservan_numero_de_fila()
    {
        var r = Validar(Lectura([Fila(2, "100", "PREÑADA"), Fila(3, "101", "SECA")]));
        Assert.Null(r.Rechazo);
        Assert.Equal(2, r.FilasLeidas);
        Assert.Equal([2, 3], r.Validas.Select(v => v.NumeroFila));
        Assert.Equal("100", r.Validas[0].Caravana.Valor);
        Assert.Empty(r.Excluidas);
    }

    [Fact]
    public void ID_con_n_minuscula_y_mayuscula_se_excluyen_juntos_como_repetidos()
    {
        var r = Validar(Lectura([Fila(2, "ñ1"), Fila(3, "Ñ1"), Fila(4, "7")]));
        Assert.Equal([4], r.Validas.Select(v => v.NumeroFila));
        Assert.Equal([(2, MotivoFila.IdRepetido), (3, MotivoFila.IdRepetido)], r.Excluidas.Select(e => (e.NumeroFila, e.Motivo)));
    }

    [Fact]
    public void Todas_las_filas_de_un_ID_repetido_quedan_excluidas()
    {
        var r = Validar(Lectura([Fila(2, "5"), Fila(3, "6"), Fila(4, "5"), Fila(5, "5")]));
        Assert.Equal([3], r.Validas.Select(v => v.NumeroFila));
        Assert.Equal([2, 4, 5], r.Excluidas.Select(e => e.NumeroFila));
        Assert.All(r.Excluidas, e => Assert.Equal(MotivoFila.IdRepetido, e.Motivo));
    }

    [Fact]
    public void Numerica_y_texto_equivalentes_cuentan_como_repetidos()
    {
        var r = Validar(Lectura([Fila(2, CeldaLeida.DeNumero(123)), Fila(3, "123")]));
        Assert.Empty(r.Validas);
        Assert.Equal(2, r.Excluidas.Count);
    }

    [Fact]
    public void Un_ID_largo_repetido_no_cuenta_como_repetido_sino_como_largo()
    {
        var largo = new string('A', 21);
        var r = Validar(Lectura([Fila(2, largo), Fila(3, largo), Fila(4, "1")]));
        Assert.All(r.Excluidas, e => Assert.Equal(MotivoFila.IdLargo, e.Motivo));
        Assert.Equal(2, r.Excluidas.Count);
    }

    [Fact]
    public void Precedencia_vacio_luego_invalido_luego_repetido()
    {
        var r = Validar(Lectura([
            Fila(2, CeldaLeida.EnBlanco),            // vacío
            Fila(3, "a b"),                          // caracteres
            Fila(4, CeldaLeida.DeNumero(1.5)),       // número inválido
            Fila(5, CeldaLeida.De(TipoCelda.Fecha, "01/01")),
            Fila(6, "9"), Fila(7, "9"),              // repetidos
        ]));
        Assert.Equal(
            [(2, MotivoFila.IdVacio), (3, MotivoFila.IdCaracteres), (4, MotivoFila.IdNumeroInvalido), (5, MotivoFila.IdTipoInvalido), (6, MotivoFila.IdRepetido), (7, MotivoFila.IdRepetido)],
            r.Excluidas.Select(e => (e.NumeroFila, e.Motivo)));
    }

    [Fact]
    public void Fila_con_datos_pero_sin_ID_es_IdVacio()
    {
        var f = new FilaLeida(2, new Dictionary<string, CeldaLeida> { ["RPRO"] = CeldaLeida.DeTexto("SECA") });
        var r = Validar(Lectura([f, Fila(3, "1")]));
        Assert.Equal([(2, MotivoFila.IdVacio)], r.Excluidas.Select(e => (e.NumeroFila, e.Motivo)));
    }

    [Fact]
    public void Con_filas_de_datos_pero_ninguna_valida_hay_vista_pero_no_se_puede_confirmar()
    {
        var r = Validar(Lectura([Fila(2, "a b")]));
        Assert.Null(r.Rechazo);
        Assert.Empty(r.Validas);
        Assert.False(r.PuedeConfirmar);
        Assert.Single(r.Excluidas);
    }

    // ---------- RPRO ----------

    [Theory]
    [InlineData("PREÑADA")]
    [InlineData("preñada")]
    [InlineData("  Preñada ")]
    public void RPRO_conocido_asigna_el_estado_sin_distinguir_mayusculas(string rpro)
    {
        var r = Validar(Lectura([Fila(2, "1", rpro)]));
        Assert.Equal(Prenada.Id, r.Validas[0].EstadoId);
        Assert.Empty(r.Advertencias);
    }

    [Fact]
    public void RPRO_sin_acento_no_coincide_porque_no_se_normalizan_acentos()
    {
        var r = Validar(Lectura([Fila(2, "1", "PRENADA")]));
        Assert.Null(r.Validas[0].EstadoId);
        Assert.Single(r.Advertencias);
    }

    [Fact]
    public void RPRO_desconocido_entra_sin_estado_con_advertencia_agrupada()
    {
        var filas = Enumerable.Range(2, 8).Select(i => Fila(i, $"v{i}", "ABORTO")).ToList();
        filas.Add(Fila(10, "otra", "ALGO"));
        var r = Validar(Lectura(filas));

        Assert.Equal(9, r.Validas.Count);
        Assert.All(r.Validas, v => Assert.Null(v.EstadoId));
        var aborto = Assert.Single(r.Advertencias, a => a.Tipo == TipoAdvertencia.EstadoDesconocido && a.Valor == "ABORTO");
        Assert.Equal(8, aborto.Cantidad);
        Assert.Equal([2, 3, 4, 5, 6], aborto.FilasEjemplo);   // a lo sumo 5
        var algo = Assert.Single(r.Advertencias, a => a.Valor == "ALGO");
        Assert.Equal(1, algo.Cantidad);
    }

    [Fact]
    public void RPRO_de_estado_dado_de_baja_cuenta_como_desconocido()
    {
        var r = ValidadorImportacion.Validar(Lectura([Fila(2, "1", "VACIA")]), [Baja("VACIA", "VACIA"), Seca]);
        Assert.Null(r.Validas[0].EstadoId);
        Assert.Single(r.Advertencias);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RPRO_vacio_entra_sin_estado_y_sin_advertencia(string? rpro)
    {
        var r = Validar(Lectura([Fila(2, "1", rpro)]));
        Assert.Null(r.Validas[0].EstadoId);
        Assert.Empty(r.Advertencias);
    }

    [Fact]
    public void RPRO_de_filas_excluidas_no_genera_advertencia()
    {
        var r = Validar(Lectura([Fila(2, "a b", "ABORTO"), Fila(3, "1")]));
        Assert.Empty(r.Advertencias);
    }

    // ---------- Advertencias de estructura ----------

    [Fact]
    public void Columnas_opcionales_faltantes_se_avisan()
    {
        var r = Validar(Lectura([Fila(2, "1")], faltantes: ["CVET", "COM"]));
        Assert.Equal(["CVET", "COM"], r.Advertencias.Where(a => a.Tipo == TipoAdvertencia.ColumnaOpcionalFaltante).Select(a => a.Valor));
    }

    [Fact]
    public void Total_que_no_coincide_avisa_y_que_coincide_no()
    {
        var mal = Validar(Lectura([Fila(2, "1"), Fila(3, "2")], total: 5));
        Assert.Single(mal.Advertencias, a => a.Tipo == TipoAdvertencia.TotalNoCoincide);

        var bien = Validar(Lectura([Fila(2, "1"), Fila(3, "2")], total: 2));
        Assert.DoesNotContain(bien.Advertencias, a => a.Tipo == TipoAdvertencia.TotalNoCoincide);

        var sinTotal = Validar(Lectura([Fila(2, "1")], total: null));
        Assert.DoesNotContain(sinTotal.Advertencias, a => a.Tipo == TipoAdvertencia.TotalNoCoincide);
    }

    // ---------- Conteo por estado ----------

    [Fact]
    public void Conteo_por_estado_incluye_sin_estado_y_solo_las_filas_validas()
    {
        var r = Validar(Lectura([
            Fila(2, "1", "PREÑADA"), Fila(3, "2", "PREÑADA"), Fila(4, "3", "SECA"),
            Fila(5, "4"), Fila(6, "5", "ABORTO"),
            Fila(7, "x y", "SECA"),                // excluida: no cuenta
        ]));
        Assert.Equal(2, r.ConteoPorEstado.Single(c => c.EstadoId == Prenada.Id).Cantidad);
        Assert.Equal(1, r.ConteoPorEstado.Single(c => c.EstadoId == Seca.Id).Cantidad);
        Assert.Equal(2, r.ConteoPorEstado.Single(c => c.EstadoId == null).Cantidad);
        Assert.DoesNotContain(r.ConteoPorEstado, c => c.EstadoId == Insemin.Id);   // sin vacas: no aparece
    }

    // ---------- JSON de columnas originales ----------

    [Fact]
    public void Json_usa_encabezado_original_valores_en_texto_omite_vacias_e_incluye_desconocidas()
    {
        var extra = new Dictionary<string, CeldaLeida>
        {
            ["CVET"] = CeldaLeida.DeTexto("Dr X"),
            ["LACT"] = CeldaLeida.DeNumero(3),         // texto "3" (lo arma el perfil; acá se usa el Texto)
            ["FPART"] = CeldaLeida.DeTexto("  1/ 5/25"),
            ["DEL"] = CeldaLeida.EnBlanco,             // vacía: se omite
            ["VINS"] = CeldaLeida.DeTexto("   "),      // solo espacios: se omite
            ["Extra"] = CeldaLeida.DeTexto("ñandú"),   // columna desconocida
        };
        var enc = new[] { "ID", "CVET", "LACT", "FPART", "DEL", "VINS", "RPRO", "Extra" };
        var r = Validar(Lectura([Fila(2, CeldaLeida.DeTexto("100"), "SECA", extra)], encabezados: enc));

        using var doc = JsonDocument.Parse(r.Validas[0].DatosOriginalesJson);
        var props = doc.RootElement.EnumerateObject().ToList();
        Assert.Equal(["ID", "CVET", "LACT", "FPART", "RPRO", "Extra"], props.Select(p => p.Name));   // orden del archivo
        Assert.All(props, p => Assert.Equal(JsonValueKind.String, p.Value.ValueKind));
        Assert.Equal("100", doc.RootElement.GetProperty("ID").GetString());
        Assert.Equal("3", doc.RootElement.GetProperty("LACT").GetString());
        Assert.Equal("  1/ 5/25", doc.RootElement.GetProperty("FPART").GetString());
        Assert.Equal("ñandú", doc.RootElement.GetProperty("Extra").GetString());
    }

    [Fact]
    public void Json_es_estable_para_la_misma_entrada()
    {
        var a = Validar(Lectura([Fila(2, "1", "SECA")]));
        var b = Validar(Lectura([Fila(2, "1", "SECA")]));
        Assert.Equal(a.Validas[0].DatosOriginalesJson, b.Validas[0].DatosOriginalesJson);
    }

    [Fact]
    public void COM_no_va_a_observaciones_porque_la_fila_importada_no_las_lleva()
    {
        // FilaAnimalImportada no tiene campo de observaciones: COM queda solo dentro del JSON de originales.
        Assert.DoesNotContain(typeof(FilaAnimalImportada).GetProperties(), p => p.Name.Contains("Observ"));
        var extra = new Dictionary<string, CeldaLeida> { ["COM"] = CeldaLeida.DeTexto("nota") };
        var r = Validar(Lectura([Fila(2, CeldaLeida.DeTexto("1"), null, extra)]));
        Assert.Contains("\"COM\":\"nota\"", r.Validas[0].DatosOriginalesJson);
    }

    [Fact]
    public void Un_estado_con_el_mismo_codigo_activo_y_otro_de_baja_usa_el_activo()
    {
        var r = ValidadorImportacion.Validar(Lectura([Fila(2, "1", "SECA")]), [Baja("SECA_VIEJA", "SECA"), Seca]);
        Assert.Equal(Seca.Id, r.Validas[0].EstadoId);
    }
}
