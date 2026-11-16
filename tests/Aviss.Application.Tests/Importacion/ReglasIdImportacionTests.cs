using Aviss.Application.Importacion;

namespace Aviss.Application.Tests.Importacion;

/// <summary>Una prueba por cada fila de la tabla de RF-7 (cómo se obtiene la caravana de la celda ID según su tipo).</summary>
public class ReglasIdImportacionTests
{
    private const char Nbsp = ' ';

    [Theory]
    [InlineData("123", "123")]
    [InlineData("  123  ", "123")]            // espacios de borde se recortan
    [InlineData("0123", "0123")]              // los ceros se conservan
    [InlineData("AB-12", "AB-12")]
    [InlineData("ñ1", "ñ1")]
    public void Texto_valido_se_guarda_tal_cual_recortado(string celda, string esperado)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto(celda));
        Assert.True(r.EsValido);
        Assert.Equal(esperado, r.Caravana!.Valor);
        Assert.Null(r.Motivo);
    }

    [Fact]
    public void Texto_con_NBSP_de_borde_se_recorta()
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto($"{Nbsp}123{Nbsp}"));
        Assert.True(r.EsValido);
        Assert.Equal("123", r.Caravana!.Valor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  \t")]
    public void Texto_vacio_o_solo_espacios_es_IdVacio(string celda)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto(celda));
        Assert.False(r.EsValido);
        Assert.Equal(MotivoFila.IdVacio, r.Motivo);
    }

    [Theory]
    [InlineData("12 3")]          // espacio interno
    [InlineData("12\t3")]         // tabulación interna
    [InlineData("12 3")]     // NBSP interno
    [InlineData("12\u00003")]     // carácter de control
    [InlineData("12\n3")]
    public void Texto_con_espacios_o_control_en_el_medio_es_IdCaracteres(string celda)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto(celda));
        Assert.Equal(MotivoFila.IdCaracteres, r.Motivo);
    }

    [Fact]
    public void Texto_de_20_caracteres_es_valido_y_de_21_es_IdLargo()
    {
        Assert.True(ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto(new string('A', 20))).EsValido);
        Assert.Equal(MotivoFila.IdLargo, ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto(new string('A', 21))).Motivo);
    }

    [Fact]
    public void Ceros_a_la_izquierda_hacen_caravanas_distintas()
    {
        var a = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto("0123")).Caravana!;
        var b = ReglasIdImportacion.Evaluar(CeldaLeida.DeTexto("123")).Caravana!;
        Assert.NotEqual(a.Clave, b.Clave);
    }

    [Theory]
    [InlineData(123.0, "123")]
    [InlineData(0.0, "0")]
    [InlineData(999999999999999.0, "999999999999999")]   // 15 dígitos: el máximo
    public void Numerica_entera_no_negativa_pasa_a_texto_sin_decimales(double n, string esperado)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeNumero(n));
        Assert.True(r.EsValido);
        Assert.Equal(esperado, r.Caravana!.Valor);
    }

    [Theory]
    [InlineData(123.5)]
    [InlineData(-1.0)]
    [InlineData(1e20)]                       // notación científica
    [InlineData(1000000000000000.0)]         // 16 dígitos
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Numerica_no_entera_negativa_o_enorme_es_IdNumeroInvalido(double n)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.DeNumero(n));
        Assert.Equal(MotivoFila.IdNumeroInvalido, r.Motivo);
    }

    [Fact]
    public void Numerica_sin_valor_es_IdNumeroInvalido()
    {
        var r = ReglasIdImportacion.Evaluar(new CeldaLeida(TipoCelda.Numerica, "", null));
        Assert.Equal(MotivoFila.IdNumeroInvalido, r.Motivo);
    }

    [Theory]
    [InlineData(TipoCelda.Fecha)]
    [InlineData(TipoCelda.Booleana)]
    [InlineData(TipoCelda.Error)]
    [InlineData(TipoCelda.Formula)]
    public void Fecha_booleana_error_o_formula_es_IdTipoInvalido(TipoCelda tipo)
    {
        var r = ReglasIdImportacion.Evaluar(CeldaLeida.De(tipo, "algo"));
        Assert.Equal(MotivoFila.IdTipoInvalido, r.Motivo);
    }

    [Fact]
    public void Celda_en_blanco_es_IdVacio()
    {
        Assert.Equal(MotivoFila.IdVacio, ReglasIdImportacion.Evaluar(CeldaLeida.EnBlanco).Motivo);
    }

    [Fact]
    public void Celda_inexistente_es_IdVacio()
    {
        Assert.Equal(MotivoFila.IdVacio, ReglasIdImportacion.Evaluar(null).Motivo);
    }
}
