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

    // ---- CVET y columnas extra (DCC, DDUC, LECHE...)

    [Theory]
    [InlineData("""{"CVET":"PRE"}""", "PRE")]
    [InlineData("""{"cvet":" 0 "}""", "0")]
    [InlineData("""{"CVET":0}""", "0")]
    [InlineData("""{"LACT":"2"}""", "")]
    [InlineData("{roto", "")]
    [InlineData(null, "")]
    public void Lee_el_cvet_sin_distinguir_mayusculas_y_sin_fallar(string? json, string esperado)
        => Assert.Equal(esperado, ConstructorFicha.LeerCvet(json));

    [Fact]
    public void La_ficha_trae_el_cvet()
        => Assert.Equal("FRES", Armar("""{"CVET":"FRES"}""").Cvet);

    [Fact]
    public void Las_columnas_que_la_ficha_no_conoce_van_como_extras_en_el_orden_del_archivo_y_solo_con_valor()
    {
        var f = Armar("""{"ID":"1014","CVET":"PRE","RPRO":"INSEMIN","LACT":"3","DDUC":"34","DEL":"95","DCC":"0","VACIA":"","LECHE":"24","UTORO":"FUNKI"}""");

        // RPRO y FCELO también se muestran (spec 009, RF-15): son datos del Excel con valor.
        Assert.Equal([("RPRO", "INSEMIN"), ("DDUC", "34"), ("DCC", "0"), ("LECHE", "24")], f.Extras!.Select(c => (c.Nombre, c.Valor)));
    }

    // ---- Rechazos (spec 004, RF-11) ----

    private static RechazoParaFicha Rechazo(int dia, string? motivo, bool anulado = false, long orden = 1, int hora = 10)
        => new(Guid.NewGuid(), new DateTime(2026, 11, dia, hora, 0, 0, DateTimeKind.Utc), orden, motivo, anulado);

    private static FichaArmada ArmarConRechazos(params RechazoParaFicha[] rechazos)
        => ConstructorFicha.Armar(null, [], Ahora, TimeZoneInfo.Utc, rechazos);

    [Fact]
    public void Sin_rechazos_no_hay_rechazo_vigente_ni_lineas_de_rechazo()
    {
        var f = Armar(null, Tacto(10));
        Assert.Null(f.RechazoVigente);
        Assert.DoesNotContain(f.Historial, e => e.Titulo.StartsWith("RECHAZO"));
    }

    [Fact]
    public void Los_rechazos_entran_al_historial_con_su_motivo_y_los_anulados_van_marcados()
    {
        var f = ArmarConRechazos(Rechazo(10, "pata", anulado: true), Rechazo(12, null, orden: 2));

        Assert.Equal(["RECHAZO: pata", "RECHAZO"], f.Historial.Select(e => e.Titulo));
        Assert.Equal([true, false], f.Historial.Select(e => e.Anulado));
        Assert.All(f.Historial, e => Assert.Equal(OrigenEvento.Aviss, e.Origen));
        Assert.Equal([6, 4], f.Historial.Select(e => e.DiasTranscurridos)); // 16 - 10 y 16 - 12
    }

    [Fact]
    public void El_rechazo_vigente_es_el_no_anulado_mas_reciente_y_con_empate_el_guardado_despues()
    {
        var viejo = Rechazo(10, "viejo");
        var empateA = Rechazo(12, "a", orden: 5);
        var empateB = Rechazo(12, "b", orden: 6);
        var anuladoNuevo = Rechazo(14, "anulado", anulado: true, orden: 7);

        var f = ArmarConRechazos(viejo, empateA, empateB, anuladoNuevo);

        Assert.Equal(empateB.Id, f.RechazoVigente!.RechazoId);
        Assert.Equal("b", f.RechazoVigente.Observaciones);
    }

    [Fact]
    public void Si_todos_los_rechazos_estan_anulados_no_hay_vigente_pero_siguen_en_el_historial()
    {
        var f = ArmarConRechazos(Rechazo(10, "x", anulado: true));
        Assert.Null(f.RechazoVigente);
        Assert.Single(f.Historial);
    }

    [Fact]
    public void Los_rechazos_se_mezclan_en_orden_cronologico_con_los_tactos()
    {
        var f = ConstructorFicha.Armar(null, [Tacto(10), Tacto(14)], Ahora, TimeZoneInfo.Utc, [Rechazo(12, "pata")]);
        Assert.Equal(["Tacto: PREÑADA", "RECHAZO: pata", "Tacto: PREÑADA"], f.Historial.Select(e => e.Titulo));
    }

    [Fact]
    public void Sin_columnas_extra_la_lista_queda_vacia()
        => Assert.Empty(Armar("""{"ID":"1","CVET":"0","LACT":"1"}""").Extras!);

    // ---- Spec 009, RF-15: la ficha de una vaca con CVET 0 trae TODOS sus datos (caso de referencia: la vaca 1019 del rodeo BAUTI1)

    /// <summary>Los datos originales de la vaca 1019 (CVET 0) tal como los guardó la importación.</summary>
    public const string Json1019 = """{"ID":"1019","CVET":"0","LACT":"2","DEL":"211","FPART":"23/04/26","FCELO":"10/07/26","UTORO":"WINDSTAR  ","DDUC":"133","DCC":"133","RPRO":"PREÑADA ","FAPAR":"18/04/27","LECHE":"0"}""";

    [Fact]
    public void La_ficha_de_la_vaca_1019_con_CVET_0_trae_todos_los_datos_del_excel_que_tienen_valor()
    {
        var f = Armar(Json1019);

        // Todo lo que el Excel trae con valor tiene que poder mostrarse en la pestaña Datos (columnas fijas + toro + extras).
        var mostrados = f.Campos.Concat(f.Extras!).Where(c => c.Valor.Length > 0).ToDictionary(c => c.Nombre, c => c.Valor);
        mostrados["UTORO"] = f.UltimoToro;

        Assert.Equal("2", mostrados["LACT"]);
        Assert.Equal("211", mostrados["DEL"]);
        Assert.Equal("23/04/26", mostrados["FPART"]);
        Assert.Equal("10/07/26", mostrados["FCELO"]);
        Assert.Equal("WINDSTAR", mostrados["UTORO"]);
        Assert.Equal("133", mostrados["DDUC"]);
        Assert.Equal("133", mostrados["DCC"]);
        Assert.Equal("PREÑADA", mostrados["RPRO"]);
        Assert.Equal("18/04/27", mostrados["FAPAR"]);
        Assert.Equal("0", mostrados["LECHE"]);
        Assert.Equal("0", f.Cvet);
    }

    // ---- EID (spec 010, RF-17) ----

    [Fact]
    public void El_EID_del_Excel_no_sale_entre_los_extras_porque_la_ficha_lo_muestra_aparte()
    {
        var f = Armar("""{"ID":"30092","EID":"999 000 458013169","CVET":"PRE","LACT":"6","LECHE":"12"}""");

        Assert.DoesNotContain(f.Extras!, c => c.Nombre.Equals("EID", StringComparison.OrdinalIgnoreCase));
        Assert.Equal([("LECHE", "12")], f.Extras!.Select(c => (c.Nombre, c.Valor)));
    }

    [Fact]
    public void El_EID_en_minusculas_tampoco_sale_entre_los_extras()
        => Assert.Empty(Armar("""{"ID":"1","eid":"999000458013169"}""").Extras!);
}
