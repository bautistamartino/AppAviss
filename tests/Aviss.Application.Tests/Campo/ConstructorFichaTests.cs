using Aviss.Application.Campo;

namespace Aviss.Application.Tests.Campo;

/// <summary>RF-13 y RF-14: la ficha se arma sin fallar, con el historial cronológico y los días transcurridos.</summary>
public class ConstructorFichaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static FichaArmada Armar(string? json, params TactoParaFicha[] tactos)
        => ConstructorFicha.Armar(json, tactos, Ahora, TimeZoneInfo.Utc);

    private static TactoParaFicha Tacto(int dia, string? estado = "PREÑADA", bool anulado = false, int mes = 11, int hora = 10)
        => new(new DateTime(2026, mes, dia, hora, 0, 0, DateTimeKind.Utc), estado, anulado);

    [Fact]
    public void Sin_datos_originales_los_campos_quedan_vacios_y_no_falla()
    {
        var f = Armar(null);
        Assert.Equal(["LACT", "DEL", "FPART", "FAPAR", "COM"], f.Campos.Select(c => c.Nombre));
        Assert.All(f.Campos, c => Assert.Equal("", c.Valor));
        Assert.Equal("", f.Inseminaciones);
        Assert.Equal("", f.UltimoToro);
        Assert.Empty(f.Historial);
    }

    [Theory]
    [InlineData("{esto no es json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    [InlineData("null")]
    public void Un_json_roto_o_que_no_es_un_objeto_se_trata_como_ausente(string json)
    {
        var f = Armar(json, Tacto(10));
        Assert.All(f.Campos, c => Assert.Equal("", c.Valor));
        Assert.Single(f.Historial); // el tacto se muestra igual
    }

    [Fact]
    public void Los_campos_se_muestran_como_texto_recortado_y_sin_importar_mayusculas_en_el_encabezado()
    {
        var f = Armar("""{"lact":" 3 ","DEL":"120","COM":"cojera","FPART":" 5/11/26"}""");
        Assert.Equal("3", f.Campos.Single(c => c.Nombre == "LACT").Valor);
        Assert.Equal("120", f.Campos.Single(c => c.Nombre == "DEL").Valor);
        Assert.Equal("cojera", f.Campos.Single(c => c.Nombre == "COM").Valor);
        Assert.Equal("5/11/26", f.Campos.Single(c => c.Nombre == "FPART").Valor);
    }

    [Fact]
    public void Vins_es_una_cantidad_sin_fecha_y_el_toro_va_aparte()
    {
        var f = Armar("""{"VINS":"3","UTORO":"HOLSTEIN 77"}""");
        Assert.Equal("3", f.Inseminaciones);
        Assert.Equal("HOLSTEIN 77", f.UltimoToro);
        Assert.Empty(f.Historial); // no se inventan fechas de inseminación
    }

    [Fact]
    public void Un_valor_numerico_en_el_json_tambien_se_lee_como_texto()
    {
        var f = Armar("""{"VINS":2,"LACT":4}""");
        Assert.Equal("2", f.Inseminaciones);
        Assert.Equal("4", f.Campos.Single(c => c.Nombre == "LACT").Valor);
    }

    [Fact]
    public void El_historial_mezcla_excel_y_tactos_en_orden_cronologico_con_dias_transcurridos()
    {
        var f = Armar("""{"FPART":" 5/11/26","FCELO":"1/10/2026","FAPAR":"20/12/26"}""", Tacto(10));

        Assert.Equal(["Celo", "Último parto", "Tacto: PREÑADA", "Fecha probable de parto"], f.Historial.Select(e => e.Titulo));
        Assert.Equal([46, 11, 6, -34], f.Historial.Select(e => e.DiasTranscurridos!.Value));
        Assert.Equal(OrigenEvento.Excel, f.Historial[0].Origen);
        Assert.Equal(OrigenEvento.Aviss, f.Historial[2].Origen);
        Assert.Equal(new DateTime(2026, 11, 5), f.Historial[1].FechaLocal);
    }

    [Fact]
    public void Una_fecha_ilegible_se_muestra_como_texto_al_final_sin_fecha_ordenable()
    {
        var f = Armar("""{"FPART":"31/2/26","FCELO":"texto cualquiera","FAPAR":"20/12/26"}""", Tacto(10));

        // Primero lo ordenable, después lo ilegible.
        Assert.Equal(["Tacto: PREÑADA", "Fecha probable de parto"], f.Historial.Take(2).Select(e => e.Titulo));
        var ilegibles = f.Historial.Skip(2).ToList();
        Assert.Equal(2, ilegibles.Count);
        Assert.All(ilegibles, e =>
        {
            Assert.Null(e.FechaLocal);
            Assert.Null(e.DiasTranscurridos);
            Assert.False(string.IsNullOrEmpty(e.TextoFecha));
        });
        Assert.Contains(ilegibles, e => e.Titulo == "Último parto" && e.TextoFecha == "31/2/26");
        Assert.Contains(ilegibles, e => e.Titulo == "Celo" && e.TextoFecha == "texto cualquiera");
    }

    [Fact]
    public void Los_tactos_anulados_se_marcan_y_un_tacto_sin_estado_se_llama_solo_tacto()
    {
        var f = Armar(null, Tacto(8, "SECA", anulado: true), Tacto(9, null));
        Assert.True(f.Historial[0].Anulado);
        Assert.Equal("Tacto: SECA", f.Historial[0].Titulo);
        Assert.False(f.Historial[1].Anulado);
        Assert.Equal("Tacto", f.Historial[1].Titulo);
    }

    [Fact]
    public void Los_tactos_se_ordenan_aunque_lleguen_desordenados()
    {
        var f = Armar(null, Tacto(12), Tacto(3), Tacto(8));
        Assert.Equal([3, 8, 12], f.Historial.Select(e => e.FechaLocal!.Value.Day));
    }

    [Fact]
    public void El_dia_de_un_tacto_se_cuenta_en_hora_local()
    {
        // 02:00 UTC del 16/11 es 23:00 del 15/11 en UTC-3; ahora (12:00 UTC) es el 16/11 a las 09:00 locales.
        var zona = TimeZoneInfo.CreateCustomTimeZone("ar", TimeSpan.FromHours(-3), "ar", "ar");
        var tacto = new TactoParaFicha(new DateTime(2026, 11, 16, 2, 0, 0, DateTimeKind.Utc), "SECA", false);
        var f = ConstructorFicha.Armar(null, [tacto], Ahora, zona);
        Assert.Equal(1, f.Historial.Single().DiasTranscurridos);
        Assert.Equal(new DateTime(2026, 11, 15, 23, 0, 0), f.Historial.Single().FechaLocal);
    }

    [Fact]
    public void Las_marcas_de_pasada_no_forman_parte_del_historial()
    {
        // El constructor ni siquiera recibe marcas: solo tactos y el json (RF-14).
        var f = Armar("""{"FPART":"5/11/26"}""");
        Assert.Single(f.Historial);
    }
}
