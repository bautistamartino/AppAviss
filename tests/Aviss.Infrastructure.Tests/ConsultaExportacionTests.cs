using System.Data.Common;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Persistencia;
using Aviss.Infrastructure.Servicios;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 007, RF-6 a RF-12 y RF-25: lo que la consulta arma de un rodeo con los tactos y rechazos REALES guardados en SQLite
/// (anulados, anulaciones, Editar, eventos renombrados, CVET 0, abortos con sus cinco resultados, rodeos de otro tambo).
/// </summary>
public sealed class ConsultaExportacionTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    private BaseDeDatosDePrueba Db => _e.Base;
    public void Dispose() => _e.Dispose();

    // El catálogo mínimo con las claves estándar que usa el armado (mismas columnas que dejaría el sembrador).
    private readonly Guid _vacia, _prenada, _insemin, _seca, _fresca, _fechaPrevia, _aborto, _ok;

    public ConsultaExportacionTests()
    {
        _vacia = Db.Estado("VACIA", "VACIA", "VACIA", tipo: "Estado", clave: "VACIA");
        _prenada = _e.EstadoPrenada;   // el del escenario, con la clave estándar que usa el armado
        Db.Ejecutar("UPDATE EstadoReproductivo SET ClaveEstandar = 'PRENADA' WHERE Id = $id", ("$id", _prenada));
        _insemin = Db.Estado("INSEMIN", "INSEMIN", "INSEMIN", tipo: "Estado", clave: "INSEMIN");
        _seca = _e.EstadoSeca;
        Db.Ejecutar("UPDATE EstadoReproductivo SET ClaveEstandar = 'SECA' WHERE Id = $id", ("$id", _seca));
        _fresca = Db.Estado("FRESCA", "FRESCA", "FRESCA", tipo: "Estado", clave: "FRESCA");
        _fechaPrevia = Db.Estado("PRENADA_FECHA_PREVIA", "Preñada fecha previa", "PREÑADA FECHA PREVIA", tipo: "Estado",
            clave: "PRENADA_FECHA_PREVIA", pasos: "DiasPreniez", destinoId: _prenada);
        _aborto = Db.Estado("ABORTO", "Aborto", "ABORTO", tipo: "Estado", clave: "ABORTO", pasos: "DiasAborto,ResultadoAborto");
        _ok = Db.Estado("OK", "OK", "OK", tipo: "SoloEvento", clave: "OK");
    }

    private Guid Vaca(string caravana, string? cvet = null, Guid? rodeo = null, bool anulada = false)
    {
        var json = cvet is null ? $"{{\"ID\":\"{caravana}\"}}" : $"{{\"ID\":\"{caravana}\",\"CVET\":\"{cvet}\"}}";
        // Solo un alta en campo puede anularse (CK_Animal_Anulado); un importado no.
        return anulada
            ? Db.Animal(rodeo ?? _e.RodeoId, caravana, "AltaEnCampo", anulado: true)
            : Db.Animal(rodeo ?? _e.RodeoId, caravana, "Importado", datosOriginalesJson: json);
    }

    private static DateTime Hora(int h, int min = 0, int dia = 5) => new(2026, 11, dia, h, min, 0, DateTimeKind.Utc);

    private Task<DatosExportacion> Consultar(Guid? rodeo = null)
        => _e.Servicio<IConsultaExportacion>().ObtenerAsync(rodeo ?? _e.RodeoId);

    // ---------- Lo básico ----------

    [Fact]
    public async Task Un_rodeo_sin_registros_no_tiene_valores_ni_jornada_pero_cuenta_sus_vacas()
    {
        Vaca("1"); Vaca("2");
        var d = await Consultar();
        Assert.Empty(d.ValoresPorCaravana);
        Assert.Null(d.Jornada);
        Assert.Equal(2, d.TotalVacas);
        Assert.Equal(0, d.VacasConEvento);
        Assert.Equal(2, d.VacasSinEvento);
    }

    [Fact]
    public async Task Un_tacto_normal_sale_con_el_nombre_del_estado_y_su_fecha_y_hora_locales()
    {
        _e.Zona.Fijar(FakeZonaHoraria.ConDesfase(-3));
        var a = Vaca("100");
        Db.Tacto(a, _seca, fechaHora: Hora(12, 5));

        var d = await Consultar();

        var v = d.ValoresPorCaravana["100"];
        Assert.Equal("SECA", v.Evento);
        Assert.Equal("05/11/2026", v.Fecha);
        Assert.Equal("0905", v.Hora);
        Assert.Equal("", v.DiasPreniez);
        Assert.Equal(new DateOnly(2026, 11, 5), d.Jornada);
        Assert.Equal(1, d.VacasConEvento);
    }

    [Fact]
    public async Task Preniada_fecha_previa_sale_por_su_nombre_con_los_dias_y_no_con_el_del_estado_destino()
    {
        var a = Vaca("100");
        Db.Tacto(a, _prenada, eventoId: _fechaPrevia, diasPreniezInformados: 130, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Preñada fecha previa", v.Evento);
        Assert.Equal("130", v.DiasPreniez);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
    }

    [Theory]
    [InlineData("VACIA", "Vacía OK")]
    [InlineData("INSEMIN", "Inseminada")]
    [InlineData("SECA", "Seca")]
    [InlineData("FRESCA", "Fresca")]
    public async Task Un_aborto_con_tacto_sale_con_los_dias_y_el_resultado_segun_el_estado_final(string clave, string etiqueta)
    {
        var final = clave switch { "VACIA" => _vacia, "INSEMIN" => _insemin, "SECA" => _seca, _ => _fresca };
        var a = Vaca("100");
        Db.Tacto(a, final, eventoId: _aborto, diasAborto: 45, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Aborto", v.Evento);
        Assert.Equal("45", v.DiasAborto);
        Assert.Equal(etiqueta, v.ResultadoAborto);
        Assert.Equal("", v.DiasPreniez);
    }

    [Fact]
    public async Task Un_aborto_con_resultado_Rechazo_sale_con_los_dias_y_el_resultado_Rechazo()
    {
        var a = Vaca("100");
        Db.Rechazo(a, eventoId: _aborto, diasAborto: 0, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Aborto", v.Evento);
        Assert.Equal("0", v.DiasAborto);
        Assert.Equal("Rechazo", v.ResultadoAborto);
    }

    [Fact]
    public async Task Un_aborto_viejo_sin_dias_sale_como_Aborto_a_secas()
    {
        var a = Vaca("100");
        // Guardado con la primera vuelta de la spec 005: en Rechazo, con el evento y sin días.
        Db.Ejecutar("DROP TRIGGER IF EXISTS TR_Rechazo_SoloEvento");   // la base nueva no admite crear uno así: se simula el dato viejo
        Db.Rechazo(a, eventoId: _aborto, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Aborto", v.Evento);
        Assert.Equal("", v.DiasAborto);
        Assert.Equal("", v.ResultadoAborto);
    }

    [Fact]
    public async Task El_rechazo_fijo_sale_como_Rechazo_y_un_evento_solo_evento_por_su_nombre()
    {
        var a = Vaca("1");
        var b = Vaca("2");
        Db.Rechazo(a, "motivo", fechaHora: Hora(10));
        Db.Rechazo(b, eventoId: _ok, fechaHora: Hora(10, 1));

        var d = await Consultar();

        Assert.Equal("Rechazo", d.ValoresPorCaravana["1"].Evento);
        Assert.Equal("OK", d.ValoresPorCaravana["2"].Evento);
        // Las observaciones no van al archivo.
        Assert.DoesNotContain("motivo", string.Join("|", d.ValoresPorCaravana["1"].EnOrden()));
    }

    // ---------- Vigentes, anulados, Editar, jornada ----------

    [Fact]
    public async Task Un_tacto_anulado_y_su_anulacion_no_exportan_nada()
    {
        var a = Vaca("100");
        var original = Db.Tacto(a, _seca, fechaHora: Hora(10), anulado: true);
        Db.Tacto(a, null, tipo: "Anulacion", tactoAnuladoId: original, fechaHora: Hora(11));

        var d = await Consultar();

        Assert.Empty(d.ValoresPorCaravana);
        Assert.Null(d.Jornada);   // sin ningún registro vigente no hay jornada
    }

    [Fact]
    public async Task Un_registro_corregido_con_Editar_exporta_el_nuevo()
    {
        var a = Vaca("100");
        Db.Tacto(a, _vacia, fechaHora: Hora(10), anulado: true);
        Db.Tacto(a, _seca, fechaHora: Hora(11));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("SECA", v.Evento);
        Assert.Equal("1100", v.Hora);
    }

    [Fact]
    public async Task Un_rechazo_anulado_no_exporta_y_el_tacto_anterior_sigue_siendo_el_vigente()
    {
        var a = Vaca("100");
        Db.Tacto(a, _seca, fechaHora: Hora(10));
        Db.Rechazo(a, "x", fechaHora: Hora(11), anulado: true);

        Assert.Equal("SECA", (await Consultar()).ValoresPorCaravana["100"].Evento);
    }

    [Fact]
    public async Task Gana_el_ultimo_vigente_de_la_vaca_entre_tactos_y_rechazos()
    {
        var a = Vaca("100");
        Db.Tacto(a, _seca, fechaHora: Hora(10));
        Db.Rechazo(a, "x", fechaHora: Hora(11));
        Db.Tacto(a, _vacia, fechaHora: Hora(9), orden: 99);   // más viejo aunque se guardó después

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Rechazo", v.Evento);
        Assert.Equal("1100", v.Hora);
    }

    [Fact]
    public async Task Las_marcas_de_pasada_y_las_esperas_no_exportan_nada()
    {
        var a = Vaca("100");
        var manga = Db.Manga(_e.RodeoId, 1);
        Db.MarcaPasada(a, manga, Hora(10));
        Db.Espera(a, fechaHora: Hora(10));

        var d = await Consultar();

        Assert.Empty(d.ValoresPorCaravana);
        Assert.Equal(1, d.TotalVacas);
    }

    [Fact]
    public async Task Solo_sale_la_jornada_del_registro_mas_reciente_los_dias_anteriores_no()
    {
        var vieja = Vaca("1");
        var nueva = Vaca("2");
        Db.Tacto(vieja, _seca, fechaHora: Hora(10, dia: 3));
        Db.Tacto(nueva, _vacia, fechaHora: Hora(10, dia: 5));

        var d = await Consultar();

        Assert.Equal(new DateOnly(2026, 11, 5), d.Jornada);
        Assert.False(d.ValoresPorCaravana.ContainsKey("1"));
        Assert.Equal("VACIA", d.ValoresPorCaravana["2"].Evento);
        Assert.Equal(1, d.VacasConEvento);
        Assert.Equal(1, d.VacasSinEvento);
    }

    [Fact]
    public async Task Una_vaca_con_un_registro_de_un_dia_anterior_y_otro_de_la_jornada_sale_con_el_de_la_jornada()
    {
        var a = Vaca("1");
        Db.Tacto(a, _seca, fechaHora: Hora(10, dia: 3));
        Db.Tacto(a, _vacia, fechaHora: Hora(10, dia: 5));

        Assert.Equal("VACIA", (await Consultar()).ValoresPorCaravana["1"].Evento);
    }

    // ---------- Catálogo ----------

    [Fact]
    public async Task Un_evento_renombrado_sale_con_el_nombre_actual_del_catalogo()
    {
        var a = Vaca("100");
        Db.Tacto(a, _prenada, eventoId: _fechaPrevia, diasPreniezInformados: 100, fechaHora: Hora(10));
        Db.Ejecutar("UPDATE EstadoReproductivo SET Nombre = 'Preñada (fecha previa)' WHERE Id = $id", ("$id", _fechaPrevia));

        Assert.Equal("Preñada (fecha previa)", (await Consultar()).ValoresPorCaravana["100"].Evento);
    }

    [Fact]
    public async Task Un_evento_creado_a_mano_sale_por_su_nombre_nunca_por_su_codigo_de_Dairy_Comp()
    {
        var mano = Db.Estado("PROSTAG", "Prostaglandina", "PROSTAG-DC");
        var a = Vaca("100");
        Db.Tacto(a, mano, fechaHora: Hora(10));

        var v = (await Consultar()).ValoresPorCaravana["100"];

        Assert.Equal("Prostaglandina", v.Evento);
        Assert.DoesNotContain("PROSTAG-DC", string.Join("|", v.EnOrden()));
    }

    // ---------- CVET, anuladas, otros rodeos ----------

    [Fact]
    public async Task Una_vaca_con_CVET_0_queda_fuera_aunque_tenga_registro_y_se_cuenta_aparte()
    {
        var cero = Vaca("1", cvet: "0");
        var normal = Vaca("2", cvet: "PRE");
        var sinColumna = Vaca("3");
        Db.Tacto(cero, _seca, fechaHora: Hora(10));
        Db.Tacto(normal, _seca, fechaHora: Hora(10, 1));
        Db.Tacto(sinColumna, _seca, fechaHora: Hora(10, 2));

        var d = await Consultar();

        Assert.False(d.ValoresPorCaravana.ContainsKey("1"));
        Assert.True(d.ValoresPorCaravana.ContainsKey("2"));
        Assert.True(d.ValoresPorCaravana.ContainsKey("3"));   // un CVET ausente no bloquea
        Assert.Equal(1, d.VacasCvetCeroConRegistro);
        Assert.Equal(2, d.VacasConEvento);
        Assert.Equal(3, d.TotalVacas);
    }

    [Fact]
    public async Task Una_alta_en_campo_con_evento_no_cuenta_como_vaca_del_archivo_y_se_informa_aparte()
    {
        var delArchivo = Vaca("1");
        Vaca("2");
        var alta = Db.Animal(_e.RodeoId, "900", "AltaEnCampo");   // no tiene fila en el original
        Db.Tacto(delArchivo, _seca, fechaHora: Hora(10));
        Db.Tacto(alta, _vacia, fechaHora: Hora(10, 1));

        var d = await Consultar();

        Assert.Equal(2, d.TotalVacas);                       // solo las del archivo
        Assert.Equal(1, d.VacasConEvento);
        Assert.Equal(1, d.VacasSinEvento);
        Assert.Equal(1, d.VacasFueraDelArchivoConEvento);    // la alta: tiene valores pero no sale en el archivo
        Assert.Equal(2, d.ValoresPorCaravana.Count);
    }

    [Fact]
    public async Task Una_vaca_con_CVET_0_sin_registro_no_se_cuenta_como_con_registro()
    {
        Vaca("1", cvet: "0");
        Assert.Equal(0, (await Consultar()).VacasCvetCeroConRegistro);
    }

    [Fact]
    public async Task Una_vaca_anulada_no_figura_ni_cuenta()
    {
        var a = Vaca("1", anulada: true);
        Db.Tacto(a, _seca, fechaHora: Hora(10));
        Vaca("2");

        var d = await Consultar();

        Assert.Empty(d.ValoresPorCaravana);
        Assert.Equal(1, d.TotalVacas);
    }

    [Fact]
    public async Task Un_rodeo_de_otro_tambo_no_se_mezcla_aunque_repita_las_caravanas()
    {
        var otroRodeo = Db.Rodeo(Db.Establecimiento("Otro tambo"));
        var mia = Vaca("100");
        var ajena = Vaca("100", rodeo: otroRodeo);
        Db.Tacto(mia, _seca, fechaHora: Hora(10));
        Db.Tacto(ajena, _vacia, fechaHora: Hora(15, dia: 6));   // incluso más reciente: no cambia mi jornada

        var d = await Consultar();
        var d2 = await Consultar(otroRodeo);

        Assert.Equal("SECA", d.ValoresPorCaravana["100"].Evento);
        Assert.Equal(new DateOnly(2026, 11, 5), d.Jornada);
        Assert.Equal("VACIA", d2.ValoresPorCaravana["100"].Evento);
        Assert.Equal(new DateOnly(2026, 11, 6), d2.Jornada);
    }

    [Fact]
    public async Task La_clave_del_diccionario_es_la_clave_de_caravana_sin_distinguir_mayusculas_ni_la_enie()
    {
        var a = Vaca("añ-7b");
        Db.Tacto(a, _seca, fechaHora: Hora(10));

        var d = await Consultar();

        Assert.True(d.ValoresPorCaravana.ContainsKey(Aviss.Domain.Reglas.Caravana.Crear("AÑ-7B").Clave));
    }

    // ---------- RF-25: una sola instantánea ----------

    /// <summary>Después de que termina la primera consulta, guarda un tacto por otra conexión (como otra pantalla de la app).</summary>
    private sealed class GuardaUnTactoEnElMedio : DbCommandInterceptor
    {
        private readonly Action _guardar;
        private bool _hecho;
        public GuardaUnTactoEnElMedio(Action guardar) => _guardar = guardar;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_hecho) { _hecho = true; _guardar(); }
            return new ValueTask<DbDataReader>(result);
        }
    }

    [Fact]
    public async Task Un_tacto_guardado_mientras_se_lee_no_mezcla_momentos()
    {
        var a = Vaca("1");
        var b = Vaca("2");
        Db.Tacto(a, _seca, fechaHora: Hora(10));

        var opciones = new DbContextOptionsBuilder<AvissDbContext>();
        ConfiguracionDb.Configurar(opciones, Db.RutaDb);
        opciones.AddInterceptors(new GuardaUnTactoEnElMedio(() => Db.Tacto(b, _vacia, fechaHora: Hora(11))));
        using var ctx = new AvissDbContext(opciones.Options);

        var d = await new ConsultaExportacion(ctx, new FakeZonaHoraria()).ObtenerAsync(_e.RodeoId);

        // El tacto de "2" se guardó DESPUÉS de empezar a leer: no aparece, y el resumen es coherente con los valores.
        Assert.True(d.ValoresPorCaravana.ContainsKey("1"));
        Assert.False(d.ValoresPorCaravana.ContainsKey("2"));
        Assert.Equal(1, d.VacasConEvento);
        // Una consulta nueva ya lo ve.
        Assert.True((await Consultar()).ValoresPorCaravana.ContainsKey("2"));
    }
}
