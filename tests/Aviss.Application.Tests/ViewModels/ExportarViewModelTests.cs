using Aviss.App.ViewModels;
using Aviss.Application.Comun;
using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>Pantalla "Exportar" (spec 007, T4): lista, resumen, formato al final, generar, cancelar, resultado, errores y compartir.</summary>
public class ExportarViewModelTests
{
    private static readonly Guid Tambo1 = Guid.NewGuid();
    private static readonly Guid Tambo2 = Guid.NewGuid();
    private static readonly RodeoParaExportarDto Norte1 = new(Guid.NewGuid(), "Norte 1", Tambo1, "La Aurora", 226);
    private static readonly RodeoParaExportarDto Norte2 = new(Guid.NewGuid(), "Norte 2", Tambo1, "La Aurora", 282);
    private static readonly RodeoParaExportarDto Sur = new(Guid.NewGuid(), "Sur", Tambo2, "El Trébol", 90);

    private sealed class ZonaFija(TimeSpan desfase) : IZonaHoraria
    {
        public TimeZoneInfo Zona { get; } = TimeZoneInfo.CreateCustomTimeZone("fija", desfase, "fija", "fija");
    }

    private sealed class Entorno
    {
        public FakeExportacionService Servicio { get; } = new() { Rodeos = [Norte1, Norte2, Sur] };
        public FakeEntregaArchivoPantalla Entrega { get; } = new();
        public FakeNavegacion Navegacion { get; } = new();
        public ExportarViewModel Vm { get; }
        public ZonaFija Zona { get; } = new(TimeSpan.FromHours(-3));   // Argentina: la hora de la tablet no es la de UTC
        public Entorno() => Vm = new ExportarViewModel(Servicio, Entrega, Navegacion, Zona);

        public async Task<Entorno> EnResumenAsync(RodeoParaExportarDto? rodeo = null)
        {
            await Vm.CargarAsync();
            await Vm.ElegirRodeoCommand.ExecuteAsync(rodeo ?? Norte1);
            return this;
        }
    }

    // ------------------------------------------------------------------ RF-1: lista

    [Fact]
    public async Task Cargar_lista_los_rodeos_agrupados_por_establecimiento()
    {
        var e = new Entorno();
        await e.Vm.CargarAsync();

        Assert.Equal(PasoExportacion.Lista, e.Vm.Paso);
        Assert.Equal(["La Aurora", "El Trébol"], e.Vm.Grupos.Select(g => g.EstablecimientoNombre));
        Assert.Equal(["Norte 1", "Norte 2"], e.Vm.Grupos[0].Select(r => r.RodeoNombre));
        Assert.Equal(226, e.Vm.Grupos[0][0].CantidadVacas);
        Assert.True(e.Vm.HayRodeos);
    }

    [Fact]
    public async Task Sin_rodeos_lo_dice_y_no_hay_grupos()
    {
        var e = new Entorno();
        e.Servicio.Rodeos = [];
        await e.Vm.CargarAsync();

        Assert.True(e.Vm.NoHayRodeos);
        Assert.Empty(e.Vm.Grupos);
    }

    [Fact]
    public async Task Cargar_con_falla_muestra_un_mensaje_fijo()
    {
        var e = new Entorno();
        e.Servicio.ExcepcionAlListar = new IOException("C:\\datos\\La Aurora 12345");
        await e.Vm.CargarAsync();

        Assert.Equal(ExportarViewModel.MensajeFallaLista, e.Vm.MensajeError);
        Assert.DoesNotContain("Aurora", e.Vm.MensajeError);
    }

    // ------------------------------------------------------------------ RF-2 y RF-3: resumen y formato

    [Fact]
    public async Task Elegir_un_rodeo_muestra_el_resumen_y_no_genera_nada()
    {
        var e = new Entorno();
        var jornada = new DateOnly(2026, 11, 5);
        var ultima = new UltimaExportacionDto(new DateTime(2026, 11, 6, 13, 30, 0, DateTimeKind.Utc), "x.csv", FormatoExportacion.Csv, 150);
        e.Servicio.ResumenAEntregar = Resultado.Ok(new ResumenExportacionDto(Norte1.RodeoId, 226, 150, 76, jornada, ultima));

        await e.EnResumenAsync();

        Assert.Equal(PasoExportacion.Resumen, e.Vm.Paso);
        Assert.Equal([Norte1.RodeoId], e.Servicio.Resumenes);
        Assert.Empty(e.Servicio.Exportaciones);                       // RF-3: nada se genera sin elegir el formato
        Assert.Equal("150 de 226 vacas con evento", e.Vm.TextoConEvento);
        Assert.Equal("Sin evento: 76", e.Vm.TextoSinEvento);
        Assert.Equal("Tacto del día 05/11/2026", e.Vm.TextoJornada);
        Assert.True(e.Vm.HayUltimaExportacion);
        // 13:30 UTC en la zona de la tablet (UTC-3) son las 10:30: sale en la hora de IZonaHoraria, no en la de la PC que corre el test.
        Assert.Equal("Última exportación: 06/11/2026 10:30, 150 vacas", e.Vm.TextoUltimaExportacion);
    }

    [Fact]
    public async Task Las_vacas_con_evento_fuera_del_archivo_se_muestran_aparte_con_una_linea_fija()
    {
        var e = new Entorno();
        e.Servicio.ResumenAEntregar = Resultado.Ok(new ResumenExportacionDto(Norte1.RodeoId, 226, 150, 76, null, null, VacasFueraDelArchivoConEvento: 2));

        await e.EnResumenAsync();

        Assert.Equal("150 de 226 vacas con evento", e.Vm.TextoConEvento);   // solo las del archivo
        Assert.True(e.Vm.HayFueraDelArchivo);
        Assert.Equal("Con evento y fuera del archivo (altas en campo o ID repetido): 2", e.Vm.TextoFueraDelArchivo);
    }

    [Fact]
    public async Task Sin_exportacion_previa_ni_fuera_del_archivo_esas_lineas_no_se_muestran()
    {
        var e = await new Entorno().EnResumenAsync();

        Assert.False(e.Vm.HayFueraDelArchivo);
        Assert.False(e.Vm.HayUltimaExportacion);
        Assert.False(e.Vm.HayJornada);
    }

    [Fact]
    public async Task Sin_formato_elegido_no_se_puede_generar_desde_la_lista()
    {
        var e = new Entorno();
        await e.Vm.CargarAsync();

        Assert.False(e.Vm.GenerarCsvCommand.CanExecute(null));
        Assert.False(e.Vm.GenerarXlsCommand.CanExecute(null));
        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);
        Assert.Empty(e.Servicio.Exportaciones);
    }

    [Fact]
    public async Task Un_boton_por_formato_cada_uno_genera_el_suyo()
    {
        var csv = await new Entorno().EnResumenAsync();
        Assert.True(csv.Vm.GenerarCsvCommand.CanExecute(null));
        Assert.True(csv.Vm.GenerarXlsCommand.CanExecute(null));
        await csv.Vm.GenerarCsvCommand.ExecuteAsync(null);
        Assert.Equal([(Norte1.RodeoId, FormatoExportacion.Csv)], csv.Servicio.Exportaciones);

        var xls = await new Entorno().EnResumenAsync(Norte2);
        await xls.Vm.GenerarXlsCommand.ExecuteAsync(null);
        Assert.Equal([(Norte2.RodeoId, FormatoExportacion.Xls)], xls.Servicio.Exportaciones);
    }

    [Fact]
    public async Task Elegir_un_rodeo_que_ya_no_existe_avisa_y_refresca_la_lista()
    {
        var e = new Entorno();
        await e.Vm.CargarAsync();
        e.Servicio.ResumenAEntregar = Resultado.Error<ResumenExportacionDto>(CodigoError.NoEncontrado, MensajesExportacion.RodeoNoEncontrado);
        e.Servicio.Rodeos = [Sur];

        await e.Vm.ElegirRodeoCommand.ExecuteAsync(Norte1);

        Assert.Equal(PasoExportacion.Lista, e.Vm.Paso);
        Assert.Equal(MensajesExportacion.RodeoNoEncontrado, e.Vm.MensajeError);
        Assert.Single(e.Vm.Grupos);
    }

    [Fact]
    public async Task Resumen_con_falla_muestra_un_mensaje_fijo_y_sigue_en_la_lista()
    {
        var e = new Entorno();
        await e.Vm.CargarAsync();
        e.Servicio.ExcepcionAlResumir = new InvalidOperationException("Norte 1 vaca 4321");

        await e.Vm.ElegirRodeoCommand.ExecuteAsync(Norte1);

        Assert.Equal(PasoExportacion.Lista, e.Vm.Paso);
        Assert.Equal(ExportarViewModel.MensajeFallaResumen, e.Vm.MensajeError);
    }

    // ------------------------------------------------------------------ RF-28: volver

    [Fact]
    public async Task Volver_del_resumen_regresa_a_la_lista_sin_generar_nada()
    {
        var e = await new Entorno().EnResumenAsync();

        await e.Vm.VolverCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Lista, e.Vm.Paso);
        Assert.Null(e.Vm.Resumen);
        Assert.Empty(e.Servicio.Exportaciones);
        Assert.Empty(e.Navegacion.Destinos);
    }

    [Fact]
    public async Task Volver_de_la_lista_va_al_inicio()
    {
        var e = new Entorno();
        await e.Vm.CargarAsync();

        await e.Vm.VolverCommand.ExecuteAsync(null);

        Assert.Equal(["Inicio"], e.Navegacion.Destinos);
        Assert.Empty(e.Servicio.Exportaciones);
    }

    // ------------------------------------------------------------------ generando y cancelar

    [Fact]
    public async Task Mientras_genera_se_muestra_el_paso_generando_y_no_se_puede_generar_otra_vez()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.Bloquear();

        var generando = e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Generando, e.Vm.Paso);
        Assert.True(e.Vm.EsPasoGenerando);
        Assert.False(e.Vm.GenerarCsvCommand.CanExecute(null));
        Assert.False(e.Vm.GenerarXlsCommand.CanExecute(null));
        Assert.True(e.Vm.CancelarGeneracionCommand.CanExecute(null));

        e.Servicio.Liberar();
        await generando;
        Assert.Equal(PasoExportacion.Resultado, e.Vm.Paso);
        Assert.Single(e.Servicio.Exportaciones);
    }

    [Fact]
    public async Task Volver_mientras_genera_no_hace_nada()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.Bloquear();
        var generando = e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        await e.Vm.VolverCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Generando, e.Vm.Paso);
        e.Servicio.Liberar();
        await generando;
    }

    [Fact]
    public async Task Cancelar_mientras_genera_vuelve_al_resumen_sin_error_ni_resultado()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.Bloquear();
        var generando = e.Vm.GenerarXlsCommand.ExecuteAsync(null);

        e.Vm.CancelarGeneracionCommand.Execute(null);
        await generando;                                  // la OperationCanceledException la captura la pantalla

        Assert.Equal(PasoExportacion.Resumen, e.Vm.Paso);
        Assert.Null(e.Vm.Resultado);
        Assert.False(e.Vm.HayMensajeError);
        Assert.False(e.Vm.HayReintento);
        // Se puede volver a generar.
        Assert.True(e.Vm.GenerarCsvCommand.CanExecute(null));
    }

    [Fact]
    public async Task Salir_de_la_pantalla_mientras_genera_cancela()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.Bloquear();
        var generando = e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        e.Vm.AlSalir();
        await generando;

        Assert.Null(e.Vm.Resultado);
        Assert.False(e.Vm.HayMensajeError);
    }

    [Fact]
    public async Task Al_salir_sin_generar_no_pasa_nada()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Vm.AlSalir();
        Assert.Equal(PasoExportacion.Resumen, e.Vm.Paso);
    }

    // ------------------------------------------------------------------ resultado y compartir

    [Fact]
    public async Task El_resultado_muestra_el_nombre_el_lugar_y_las_cantidades()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ResultadoExportar = () => Resultado.Ok(new ExportacionEntregadaDto(
            new ArchivoGuardado("Norte_aviss_20261124_1030 (1).csv", "Descargas", "content://x/1"), FormatoExportacion.Csv, 226, 150, true));

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Resultado, e.Vm.Paso);
        Assert.Equal("Norte_aviss_20261124_1030 (1).csv", e.Vm.NombreArchivo);   // el nombre FINAL, no el sugerido
        Assert.Equal("Guardado en Descargas", e.Vm.TextoLugar);
        Assert.Equal("226 filas, 150 vacas con evento", e.Vm.TextoCantidades);
        Assert.False(e.Vm.HayAvisoConstancia);
    }

    [Fact]
    public async Task Si_se_crearon_SCDAT_o_SCTIM_porque_faltaban_se_avisa_con_un_texto_sin_datos_del_archivo()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ResultadoExportar = () => Resultado.Ok(new ExportacionEntregadaDto(
            new ArchivoGuardado("a.csv", "Descargas", "r"), FormatoExportacion.Csv, 5, 2, true, ["SCDAT", "SCTIM"]));

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.True(e.Vm.HayAvisoColumnasCreadas);
        Assert.Equal(MensajesExportacion.ColumnasCreadas(["SCDAT", "SCTIM"]), e.Vm.AvisoColumnasCreadas);
        Assert.Contains("SCDAT y SCTIM", e.Vm.AvisoColumnasCreadas);
        Assert.False(e.Vm.HayAvisoConstancia);
    }

    [Fact]
    public async Task Sin_columnas_creadas_no_hay_aviso()
    {
        var e = await new Entorno().EnResumenAsync();   // el resultado por defecto no trae columnas creadas

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.False(e.Vm.HayAvisoColumnasCreadas);
        Assert.Equal("", e.Vm.AvisoColumnasCreadas);
    }

    [Fact]
    public async Task Si_no_quedo_la_constancia_se_avisa_pero_el_archivo_figura_guardado()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ResultadoExportar = () => Resultado.Ok(new ExportacionEntregadaDto(
            new ArchivoGuardado("a.csv", "Descargas", "r"), FormatoExportacion.Csv, 5, 2, false));

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Resultado, e.Vm.Paso);
        Assert.True(e.Vm.HayAvisoConstancia);
        Assert.Equal(MensajesExportacion.ConstanciaNoGuardada, e.Vm.AvisoConstanciaNoGuardada);
    }

    [Fact]
    public async Task Compartir_entrega_el_archivo_guardado_al_sistema()
    {
        var e = await new Entorno().EnResumenAsync();
        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        await e.Vm.CompartirCommand.ExecuteAsync(null);

        Assert.Equal([e.Vm.Resultado!.Archivo], e.Entrega.Compartidos);
    }

    [Fact]
    public async Task Si_compartir_falla_el_aviso_es_fijo_y_el_resultado_sigue_a_la_vista()
    {
        var e = await new Entorno().EnResumenAsync();
        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);
        e.Entrega.ExcepcionAlCompartir = new InvalidOperationException("content://Norte/vaca 4321");

        await e.Vm.CompartirCommand.ExecuteAsync(null);

        Assert.Equal(ExportarViewModel.MensajeFallaCompartir, e.Vm.MensajeError);
        Assert.Equal(PasoExportacion.Resultado, e.Vm.Paso);
    }

    [Fact]
    public async Task Listo_vuelve_al_inicio_y_deja_la_pantalla_limpia()
    {
        var e = await new Entorno().EnResumenAsync();
        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        await e.Vm.ListoCommand.ExecuteAsync(null);

        Assert.Equal(["Inicio"], e.Navegacion.Destinos);
        Assert.Equal(PasoExportacion.Lista, e.Vm.Paso);
        Assert.Null(e.Vm.Resultado);
    }

    // ------------------------------------------------------------------ errores y reintentar (RF-24)

    [Fact]
    public async Task Un_rechazo_vuelve_al_resumen_con_el_mensaje_fijo_y_ofrece_reintentar()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ResultadoExportar = () => Resultado.Error<ExportacionEntregadaDto>(
            CodigoError.ArchivoNoGuardado, MensajesExportacion.Para(FalloGuardado.SinEspacio));

        await e.Vm.GenerarXlsCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Resumen, e.Vm.Paso);
        Assert.Equal(MensajesExportacion.Para(FalloGuardado.SinEspacio), e.Vm.MensajeError);
        Assert.Null(e.Vm.Resultado);
        Assert.True(e.Vm.HayReintento);
        Assert.True(e.Vm.ReintentarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reintentar_repite_el_mismo_formato()
    {
        var e = await new Entorno().EnResumenAsync();
        var intentos = 0;
        e.Servicio.ResultadoExportar = () => ++intentos == 1
            ? Resultado.Error<ExportacionEntregadaDto>(CodigoError.ExportacionNoValida, MensajesExportacion.ExportacionNoValida)
            : Resultado.Ok(new ExportacionEntregadaDto(new ArchivoGuardado("a.xls", "Descargas", "r"), FormatoExportacion.Xls, 5, 2, true));

        await e.Vm.GenerarXlsCommand.ExecuteAsync(null);
        await e.Vm.ReintentarCommand.ExecuteAsync(null);

        Assert.Equal([FormatoExportacion.Xls, FormatoExportacion.Xls], e.Servicio.Exportaciones.Select(x => x.Formato));
        Assert.Equal(PasoExportacion.Resultado, e.Vm.Paso);
        Assert.False(e.Vm.HayMensajeError);
    }

    [Fact]
    public async Task Una_excepcion_al_generar_muestra_un_mensaje_fijo_sin_datos()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ExcepcionAlExportar = new IOException("/storage/La Aurora/vaca 4321.xls");

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.Equal(PasoExportacion.Resumen, e.Vm.Paso);
        Assert.Equal(MensajesExportacion.NoSePudoGenerar, e.Vm.MensajeError);
        Assert.True(e.Vm.HayReintento);
        Assert.DoesNotContain("Aurora", e.Vm.MensajeError);
        Assert.DoesNotContain("4321", e.Vm.MensajeError);
    }

    [Fact]
    public async Task Las_columnas_duplicadas_o_un_original_ilegible_no_ofrecen_reintentar()
    {
        var e = await new Entorno().EnResumenAsync();
        e.Servicio.ResultadoExportar = () => Resultado.Error<ExportacionEntregadaDto>(CodigoError.ColumnaDuplicada, MensajesExportacion.ColumnaDuplicada("SCDAT"));

        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);

        Assert.Equal(MensajesExportacion.ColumnaDuplicada("SCDAT"), e.Vm.MensajeError);
        Assert.False(e.Vm.HayReintento);
        Assert.False(e.Vm.ReintentarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Los_textos_de_la_pantalla_no_traen_caravanas()
    {
        // La pantalla solo conoce nombres de rodeo y establecimiento y cantidades: ni el resumen ni el resultado llevan caravanas.
        var e = await new Entorno().EnResumenAsync();
        await e.Vm.GenerarCsvCommand.ExecuteAsync(null);
        var textos = string.Join("|", e.Vm.TextoConEvento, e.Vm.TextoSinEvento, e.Vm.TextoJornada,
            e.Vm.TextoUltimaExportacion, e.Vm.TextoLugar, e.Vm.TextoCantidades, e.Vm.AvisoConstanciaNoGuardada);
        Assert.DoesNotMatch(@"\b\d{4,}\b", textos);
    }

    // ------------------------------------------------------------------ inicio

    [Fact]
    public async Task El_boton_Exportar_del_inicio_va_a_la_pantalla_de_exportar()
    {
        var nav = new FakeNavegacion();
        var inicio = new InicioViewModel(nav);

        await inicio.ExportarCommand.ExecuteAsync(null);

        Assert.Equal(["Exportar"], nav.Destinos);
    }
}
