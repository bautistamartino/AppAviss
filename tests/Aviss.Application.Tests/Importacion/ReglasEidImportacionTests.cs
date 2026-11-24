using Aviss.Application.Importacion;

namespace Aviss.Application.Tests.Importacion;

/// <summary>Spec 010: cómo se interpreta la celda EID del archivo (RF-2, RF-3, RF-4).</summary>
public class ReglasEidImportacionTests
{
    private static ResultadoEid Evaluar(CeldaLeida? c) => ReglasEidImportacion.Evaluar(c);

    // ---------- Sin EID (RF-3): normal, no es un error ----------

    [Fact]
    public void Celda_nula_en_blanco_con_espacios_o_guion_es_sin_eid()
    {
        foreach (var celda in new CeldaLeida?[]
                 {
                     null, CeldaLeida.EnBlanco, CeldaLeida.DeTexto(""), CeldaLeida.DeTexto("   "),
                     CeldaLeida.DeTexto("-"), CeldaLeida.DeTexto("  -  "), CeldaLeida.DeTexto(" - ")
                 })
        {
            var r = Evaluar(celda);
            Assert.Equal(EstadoEid.SinEid, r.Estado);
            Assert.Null(r.Valor);
        }
    }

    // ---------- Válido (RF-2) ----------

    [Theory]
    [InlineData("982000458467123")]
    [InlineData("982 000458467123")]
    [InlineData("982 000 458467123")]
    [InlineData("  982000458467123  ")]
    [InlineData("982 000458467123")]      // espacio de no separar
    [InlineData("982\t000458467123")]
    public void Quince_digitos_en_texto_es_valido_y_se_guarda_sin_espacios(string texto)
    {
        var r = Evaluar(CeldaLeida.DeTexto(texto));
        Assert.Equal(EstadoEid.Valido, r.Estado);
        Assert.Equal("982000458467123", r.Valor);
    }

    [Fact]
    public void Entero_numerico_de_quince_digitos_es_valido()
    {
        var r = Evaluar(CeldaLeida.DeNumero(982000458467123d));
        Assert.Equal(EstadoEid.Valido, r.Estado);
        Assert.Equal("982000458467123", r.Valor);
    }

    [Fact]
    public void Ceros_a_la_izquierda_en_texto_se_conservan()
    {
        var r = Evaluar(CeldaLeida.DeTexto("000458467123456"));
        Assert.Equal(EstadoEid.Valido, r.Estado);
        Assert.Equal("000458467123456", r.Valor);
    }

    // ---------- Inválido (RF-4) ----------

    [Theory]
    [InlineData("98200045846712")]        // 14 dígitos
    [InlineData("9820004584671234")]      // 16 dígitos
    [InlineData("98200045846712A")]       // letra
    [InlineData("ABCDEFGHIJKLMNO")]
    [InlineData("982000458467.23")]
    [InlineData("-982000458467123")]
    [InlineData("9.82000458467123E14")]
    [InlineData("98200045846712٣")]  // dígito arábigo: no es ASCII
    [InlineData("--")]
    public void Texto_que_no_son_quince_digitos_es_invalido(string texto)
    {
        var r = Evaluar(CeldaLeida.DeTexto(texto));
        Assert.Equal(EstadoEid.Invalido, r.Estado);
        Assert.Null(r.Valor);
    }

    [Theory]
    [InlineData(98200045846712d)]         // 14 dígitos (un cero a la izquierda perdido)
    [InlineData(9820004584671234d)]       // 16 dígitos
    [InlineData(982000458467123.5d)]      // decimal
    [InlineData(-982000458467123d)]       // negativo
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0d)]
    public void Numericos_que_no_son_enteros_de_quince_digitos_son_invalidos(double numero)
    {
        var r = Evaluar(CeldaLeida.DeNumero(numero));
        Assert.Equal(EstadoEid.Invalido, r.Estado);
        Assert.Null(r.Valor);
    }

    [Theory]
    [InlineData(TipoCelda.Fecha, "01/01/2026")]
    [InlineData(TipoCelda.Formula, "982000458467123")]
    [InlineData(TipoCelda.Error, "#N/A")]
    [InlineData(TipoCelda.Booleana, "TRUE")]
    public void Fecha_formula_error_o_booleana_es_invalido(TipoCelda tipo, string texto)
    {
        var r = Evaluar(CeldaLeida.De(tipo, texto));
        Assert.Equal(EstadoEid.Invalido, r.Estado);
        Assert.Null(r.Valor);
    }
}
