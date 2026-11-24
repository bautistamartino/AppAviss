using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

// Spec 005 (RF-23, RF-27, RF-28): el mecanismo común de pasos posteriores del selector, sin pantalla.
public class SelectorEventosPasosTests
{
    private static readonly EventoCampoDto Ok = new(Guid.NewGuid(), "OK", false, true);
    private static readonly EventoCampoDto Previa = new(Guid.NewGuid(), "Preñada fecha previa", true, true,
        [PasoEvento.DiasPreniez], null, "PRENADA_FECHA_PREVIA");
    private static readonly EventoCampoDto Aborto = new(Guid.NewGuid(), "Aborto", true, true,
        [PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], null, "ABORTO");
    // Un evento con pasos que vive en "Otro evento": el mecanismo no depende de ser principal (RF-27, punto 7).
    private static readonly EventoCampoDto Raro = new(Guid.NewGuid(), "RARO", true, false, [PasoEvento.DiasPreniez], null, null);

    private static ResultadoAborto[] Todos => [ResultadoAborto.VaciaOk, ResultadoAborto.Inseminada, ResultadoAborto.Seca, ResultadoAborto.Fresca, ResultadoAborto.Rechazo];

    private static SelectorEventosViewModel Selector(IReadOnlyList<ResultadoAborto>? resultados = null)
    {
        var s = new SelectorEventosViewModel(true);
        s.Actualizar(new ListaEventosDto([Ok, Previa, Aborto], [ArmadorListaEventos.EventoRechazo, Raro], resultados ?? Todos));
        return s;
    }

    [Fact]
    public async Task Elegir_un_evento_sin_pasos_avisa_y_deja_la_eleccion_sin_datos()
    {
        var s = Selector();
        EventoCampoDto? avisado = null;
        s.AlElegir = e => { avisado = e; return Task.CompletedTask; };

        await s.ElegirCommand.ExecuteAsync(Ok);

        Assert.Equal(Ok, avisado);
        Assert.Equal(new EleccionEvento(Ok.Id), s.EleccionActual);
        Assert.False(s.HayPaso);
    }

    [Fact]
    public async Task Preniada_fecha_previa_abre_el_paso_de_dias_y_no_avisa_hasta_confirmar()
    {
        var s = Selector();
        var avisos = 0;
        s.AlElegir = _ => { avisos++; return Task.CompletedTask; };

        await s.ElegirCommand.ExecuteAsync(Previa);

        Assert.Equal(PasoEvento.DiasPreniez, s.PasoActual);
        Assert.True(s.PideDias);
        Assert.False(s.PideResultado);
        Assert.False(s.MostrandoLista);
        Assert.Equal("Preñada fecha previa", s.TituloPaso);
        Assert.Equal("Confirmar", s.TextoConfirmar);   // un solo paso
        Assert.False(s.PuedeVolverDePaso);
        Assert.Equal(0, avisos);
        Assert.Null(s.EleccionActual);

        s.TextoDias = "130";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.Equal(1, avisos);
        Assert.False(s.HayPaso);
        Assert.Equal(new EleccionEvento(Previa.Id, DiasPreniez: 130), s.EleccionActual);
        Assert.Equal(Previa.Id, s.Resaltado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("130,5")]
    [InlineData("130.5")]
    [InlineData("401")]
    [InlineData("abc")]
    public async Task Los_dias_de_preniez_invalidos_muestran_el_mensaje_y_el_paso_sigue_abierto(string texto)
    {
        var s = Selector();
        var avisos = 0;
        s.AlElegir = _ => { avisos++; return Task.CompletedTask; };
        await s.ElegirCommand.ExecuteAsync(Previa);

        s.TextoDias = texto;
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.True(s.HayMensajeError);
        Assert.Equal(PasoEvento.DiasPreniez, s.PasoActual);
        Assert.Equal(texto, s.TextoDias);   // lo escrito se conserva
        Assert.Equal(0, avisos);
        Assert.Null(s.EleccionActual);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("400")]
    public async Task Los_limites_de_los_dias_de_preniez_son_validos(string texto)
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Previa);

        s.TextoDias = texto;
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.False(s.HayPaso);
        Assert.Equal(int.Parse(texto), s.EleccionActual!.DiasPreniez);
    }

    [Fact]
    public async Task Un_mensaje_de_error_se_borra_al_corregir_y_confirmar()
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Previa);
        s.TextoDias = "0";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);
        Assert.True(s.HayMensajeError);

        s.TextoDias = "90";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.False(s.HayMensajeError);
        Assert.Equal(90, s.EleccionActual!.DiasPreniez);
    }

    [Fact]
    public async Task Aborto_acepta_0_como_dias_y_pasa_al_resultado_con_Siguiente()
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Aborto);
        Assert.Equal(PasoEvento.DiasAborto, s.PasoActual);
        Assert.Equal("Siguiente", s.TextoConfirmar);

        s.TextoDias = "0";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.Equal(PasoEvento.ResultadoAborto, s.PasoActual);
        Assert.True(s.PideResultado);
        Assert.True(s.PuedeVolverDePaso);
        Assert.Null(s.EleccionActual);   // todavía no está completo
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("12,5")]
    [InlineData("401")]
    public async Task Los_dias_de_aborto_invalidos_no_pasan_al_resultado(string texto)
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Aborto);

        s.TextoDias = texto;
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.Equal(PasoEvento.DiasAborto, s.PasoActual);
        Assert.True(s.HayMensajeError);
    }

    [Fact]
    public async Task Aborto_completo_arma_la_eleccion_con_dias_y_resultado_y_avisa_una_sola_vez()
    {
        var s = Selector();
        var avisos = new List<EventoCampoDto>();
        s.AlElegir = e => { avisos.Add(e); return Task.CompletedTask; };
        await s.ElegirCommand.ExecuteAsync(Aborto);
        s.TextoDias = "45";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        await s.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.VaciaOk);

        Assert.Equal([Aborto], avisos);
        Assert.Equal(new EleccionEvento(Aborto.Id, null, 45, ResultadoAborto.VaciaOk), s.EleccionActual);
        Assert.False(s.HayPaso);
        Assert.Equal("Aborto, 45 días, Vacía OK", SelectorEventosViewModel.Resumen(Aborto, s.EleccionActual));
    }

    [Fact]
    public void El_resumen_de_fecha_previa_y_de_un_evento_sin_pasos()
    {
        Assert.Equal("Preñada fecha previa, 130 días", SelectorEventosViewModel.Resumen(Previa, new EleccionEvento(Previa.Id, 130)));
        Assert.Equal("OK", SelectorEventosViewModel.Resumen(Ok, new EleccionEvento(Ok.Id)));
        Assert.Equal("OK", SelectorEventosViewModel.Resumen(Ok, null));
    }

    [Fact]
    public async Task Los_cinco_resultados_se_ofrecen_y_los_desactivados_no()
    {
        var todos = Selector();
        var sinSeca = Selector([ResultadoAborto.VaciaOk, ResultadoAborto.Inseminada, ResultadoAborto.Fresca, ResultadoAborto.Rechazo]);

        Assert.Equal(Todos, todos.ResultadosDisponibles);
        Assert.DoesNotContain(ResultadoAborto.Seca, sinSeca.ResultadosDisponibles);
        Assert.Contains(ResultadoAborto.Rechazo, sinSeca.ResultadosDisponibles);

        // Elegir uno que no se ofrece no hace nada.
        await sinSeca.ElegirCommand.ExecuteAsync(Aborto);
        sinSeca.TextoDias = "10";
        await sinSeca.ConfirmarDiasCommand.ExecuteAsync(null);
        await sinSeca.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Seca);
        Assert.Equal(PasoEvento.ResultadoAborto, sinSeca.PasoActual);
        Assert.Null(sinSeca.EleccionActual);
    }

    [Fact]
    public async Task Cada_resultado_se_puede_elegir()
    {
        foreach (var r in Todos)
        {
            var s = Selector();
            await s.ElegirCommand.ExecuteAsync(Aborto);
            s.TextoDias = "5";
            await s.ConfirmarDiasCommand.ExecuteAsync(null);
            await s.ElegirResultadoCommand.ExecuteAsync(r);
            Assert.Equal(r, s.EleccionActual!.ResultadoAborto);
        }
    }

    [Fact]
    public async Task Volver_de_un_paso_conserva_lo_escrito_y_lo_elegido()
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Aborto);
        s.TextoDias = "45";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        s.VolverDePasoCommand.Execute(null);

        Assert.Equal(PasoEvento.DiasAborto, s.PasoActual);
        Assert.Equal("45", s.TextoDias);
        Assert.False(s.PuedeVolverDePaso);
        Assert.Null(s.EleccionActual);
    }

    [Fact]
    public async Task Cancelar_cierra_los_pasos_sin_elegir_nada_y_deja_el_casillero_como_estaba()
    {
        var s = Selector();
        s.Abrir(Ok.Id, new EleccionEvento(Ok.Id));
        var avisos = 0;
        s.AlElegir = _ => { avisos++; return Task.CompletedTask; };
        await s.ElegirCommand.ExecuteAsync(Aborto);
        s.TextoDias = "45";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        s.CancelarCommand.Execute(null);

        Assert.False(s.HayPaso);
        Assert.True(s.MostrandoLista);
        Assert.Equal(0, avisos);
        Assert.Equal(new EleccionEvento(Ok.Id), s.EleccionActual);
        Assert.Equal(Ok.Id, s.Resaltado);
        Assert.Equal("", s.TextoDias);
    }

    [Fact]
    public async Task Elegir_otro_evento_con_un_paso_abierto_no_hace_nada()
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Previa);

        await s.ElegirCommand.ExecuteAsync(Ok);

        Assert.Equal(PasoEvento.DiasPreniez, s.PasoActual);
    }

    [Fact]
    public async Task Volver_a_elegir_el_mismo_evento_abre_los_pasos_con_los_valores_actuales()
    {
        var s = Selector();
        s.Abrir(Aborto.Id, new EleccionEvento(Aborto.Id, null, 45, ResultadoAborto.Seca));

        await s.ElegirCommand.ExecuteAsync(Aborto);
        Assert.Equal("45", s.TextoDias);

        await s.ConfirmarDiasCommand.ExecuteAsync(null);
        Assert.Equal(ResultadoAborto.Seca, s.ResultadoElegido);
    }

    [Fact]
    public async Task Los_valores_de_otro_evento_no_precargan_los_pasos()
    {
        var s = Selector();
        s.Abrir(Previa.Id, new EleccionEvento(Previa.Id, 130));

        await s.ElegirCommand.ExecuteAsync(Aborto);

        Assert.Equal("", s.TextoDias);
    }

    [Fact]
    public async Task Un_evento_de_Otro_evento_tambien_puede_tener_pasos()
    {
        var s = Selector();
        s.VerOtrosCommand.Execute(null);
        EventoCampoDto? avisado = null;
        s.AlElegir = e => { avisado = e; return Task.CompletedTask; };

        await s.ElegirCommand.ExecuteAsync(Raro);
        Assert.Equal(PasoEvento.DiasPreniez, s.PasoActual);
        s.TextoDias = "200";
        await s.ConfirmarDiasCommand.ExecuteAsync(null);

        Assert.Equal(Raro, avisado);
        Assert.Equal(200, s.EleccionActual!.DiasPreniez);
        Assert.True(s.OtroEventoResaltado);
    }

    [Fact]
    public async Task Con_un_paso_abierto_no_se_puede_crear_un_evento_nuevo()
    {
        var s = Selector();
        s.VerOtrosCommand.Execute(null);
        Assert.True(s.PuedeCrearNuevo);

        await s.ElegirCommand.ExecuteAsync(Raro);

        Assert.False(s.PuedeCrearNuevo);
    }

    [Fact]
    public async Task Abrir_descarta_un_paso_que_habia_quedado_abierto()
    {
        var s = Selector();
        await s.ElegirCommand.ExecuteAsync(Previa);

        s.Abrir(null);

        Assert.False(s.HayPaso);
    }

    [Fact]
    public void EstaCompleta_exige_todos_los_datos_que_piden_los_pasos()
    {
        Assert.True(SelectorEventosViewModel.EstaCompleta(Ok, new EleccionEvento(Ok.Id)));
        Assert.False(SelectorEventosViewModel.EstaCompleta(Previa, new EleccionEvento(Previa.Id)));
        Assert.True(SelectorEventosViewModel.EstaCompleta(Previa, new EleccionEvento(Previa.Id, 130)));
        Assert.False(SelectorEventosViewModel.EstaCompleta(Aborto, new EleccionEvento(Aborto.Id, null, 45)));
        Assert.True(SelectorEventosViewModel.EstaCompleta(Aborto, new EleccionEvento(Aborto.Id, null, 0, ResultadoAborto.Rechazo)));
        Assert.False(SelectorEventosViewModel.EstaCompleta(Aborto, null));
    }
}
