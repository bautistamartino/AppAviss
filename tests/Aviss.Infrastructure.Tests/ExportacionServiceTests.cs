using System.Security.Cryptography;
using Aviss.Application.Comun;
using Aviss.Application.Exportacion;
using Aviss.Application.Importacion;
using Aviss.Domain.Enums;
using Aviss.Excel;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 007, RF-1, RF-2, RF-12, RF-14, RF-18 y RF-19 a RF-27: el servicio de exportación de punta a punta con la base real, el
/// exportador real (o uno que se hace fallar) y la entrega falsa.
/// </summary>
public sealed class ExportacionServiceTests : IDisposable
{
    private readonly EscenarioExportacion _e = new();
    public void Dispose() => _e.Dispose();

    // Caravanas de 4 dígitos y nombres reconocibles: así se ve si algún mensaje los filtra.
    private const string Pasada = "7101", SinPasar = "7102", CvetCero = "7103", Anulada = "7104";

    /// <summary>Un rodeo con cuatro vacas: una con tacto, una sin pasar, una con CVET 0 y tacto a propósito, y una con el tacto anulado.</summary>
    private (Guid Rodeo, Guid Pasada) RodeoConTrabajo(byte[]? original = null, string archivo = "tambo-norte.csv")
    {
        var rodeo = _e.Rodeo(original ?? EscenarioExportacion.CsvOriginal(Pasada, SinPasar, CvetCero, Anulada), archivo);
        var pasada = _e.Vaca(rodeo, Pasada);
        _e.Vaca(rodeo, SinPasar);
        var cero = _e.Vaca(rodeo, CvetCero, cvet: "0");
        var anulada = _e.Vaca(rodeo, Anulada);
        _e.Base.Tacto(pasada, _e.EstadoSeca);
        _e.Base.Tacto(cero, _e.EstadoSeca);
        _e.Base.Tacto(anulada, _e.EstadoVacia, anulado: true);
        return (rodeo, pasada);
    }

    private static LecturaExcel Leer(byte[] bytes)
    {
        var r = new PerfilDairyComp().Leer(bytes, LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto, "El archivo entregado no se pudo volver a leer");
        return r.Lectura!;
    }

    private static string Celda(LecturaExcel l, string caravana, string columna)
    {
        // Una celda en blanco puede no figurar entre las celdas de la fila: cuenta como vacía.
        var fila = l.Filas.Single(f => f.Celdas[l.NombreColumnaId].Texto.Trim() == caravana);
        return fila.Celdas.TryGetValue(columna, out var celda) ? celda.Texto.Trim() : "";
    }

    private static void SinDatosDelTambo(Resultado r)
    {
        Assert.False(r.EsCorrecto);
        foreach (var prohibido in new[] { Pasada, SinPasar, CvetCero, Anulada, "Lote Secreto", "Establo Reservado", "tambo-norte" })
            Assert.DoesNotContain(prohibido, r.Mensaje!);
    }

    // ---------------------------------------------------------------- Rodeos y resumen (RF-1, RF-2)

    [Fact]
    public async Task Lista_solo_los_rodeos_activos_agrupados_por_establecimiento_con_su_cantidad_de_vacas()
    {
        var estB = _e.Base.Establecimiento("Beta");
        var estA = _e.Base.Establecimiento("Alfa");
        var a2 = _e.Rodeo(EscenarioExportacion.CsvOriginal("1"), "a.csv", estA, "Rodeo 2");
        var a1 = _e.Rodeo(EscenarioExportacion.CsvOriginal("1"), "a.csv", estA, "Rodeo 1");
        var b = _e.Rodeo(EscenarioExportacion.CsvOriginal("1"), "b.csv", estB, "Unico");
        var baja = _e.Rodeo(EscenarioExportacion.CsvOriginal("1"), "c.csv", estA, "De baja");
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0 WHERE Id = $id", ("$id", baja));
        _e.Vaca(a1, "1"); _e.Vaca(a1, "2");
        _e.Base.Animal(a1, "3", "AltaEnCampo", anulado: true);   // una alta anulada no cuenta
        _e.Vaca(b, "9");

        var lista = await _e.Servicio.ListarRodeosAsync();

        Assert.Equal(["Rodeo 1", "Rodeo 2", "Unico"], lista.Select(r => r.RodeoNombre));
        Assert.Equal(["Alfa", "Alfa", "Beta"], lista.Select(r => r.EstablecimientoNombre));
        Assert.Equal([2, 0, 1], lista.Select(r => r.CantidadVacas));
        Assert.Equal(a1, lista[0].RodeoId);
        Assert.DoesNotContain(lista, r => r.RodeoId == baja);
        Assert.Equal(a2, lista[1].RodeoId);
    }

    [Fact]
    public async Task El_resumen_habla_de_las_vacas_del_archivo_y_cuenta_aparte_las_altas_en_campo_con_evento()
    {
        var (rodeo, _) = RodeoConTrabajo();
        var alta = _e.Base.Animal(rodeo, "9001", "AltaEnCampo");   // sin fila en el original
        _e.Base.Tacto(alta, _e.EstadoSeca);

        var r = await _e.Servicio.ObtenerResumenAsync(rodeo);

        Assert.Equal(4, r.Valor.TotalVacas);                 // las del archivo, sin la alta
        Assert.Equal(2, r.Valor.VacasConEvento);
        Assert.Equal(2, r.Valor.VacasSinEvento);
        Assert.Equal(1, r.Valor.VacasFueraDelArchivoConEvento);
    }

    [Fact]
    public async Task El_resumen_cuenta_vacas_con_y_sin_evento_la_de_CVET_0_con_registro_y_la_jornada()
    {
        var (rodeo, _) = RodeoConTrabajo();

        var r = await _e.Servicio.ObtenerResumenAsync(rodeo);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(4, r.Valor.TotalVacas);
        Assert.Equal(2, r.Valor.VacasConEvento);          // la 7101 y la CVET 0 tactada a propósito: la anulada no cuenta
        Assert.Equal(2, r.Valor.VacasSinEvento);
        Assert.Equal(new DateOnly(2026, 11, 16), r.Valor.Jornada);
        Assert.Null(r.Valor.UltimaExportacion);
    }

    [Fact]
    public async Task El_resumen_muestra_la_ultima_exportacion_despues_de_exportar()
    {
        var (rodeo, _) = RodeoConTrabajo();
        await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Xls);

        var r = await _e.Servicio.ObtenerResumenAsync(rodeo);

        var ultima = r.Valor.UltimaExportacion!;
        Assert.Equal(FormatoExportacion.Xls, ultima.Formato);
        Assert.Equal(_e.Reloj.UtcAhora, ultima.FechaUtc);
        Assert.Equal(2, ultima.CantidadVacasConEvento);
    }

    [Fact]
    public async Task El_resumen_de_un_rodeo_inexistente_o_de_baja_es_no_encontrado()
    {
        var (rodeo, _) = RodeoConTrabajo();
        var inexistente = await _e.Servicio.ObtenerResumenAsync(Guid.NewGuid());
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0 WHERE Id = $id", ("$id", rodeo));
        var baja = await _e.Servicio.ObtenerResumenAsync(rodeo);

        Assert.Equal(CodigoError.NoEncontrado, inexistente.Codigo);
        Assert.Equal(CodigoError.NoEncontrado, baja.Codigo);
    }

    // ---------------------------------------------------------------- Camino feliz (RF-18 a RF-22)

    [Theory]
    [InlineData(FormatoExportacion.Csv, "csv")]
    [InlineData(FormatoExportacion.Xls, "xls")]
    public async Task Un_rodeo_exporta_de_punta_a_punta_y_deja_su_constancia(FormatoExportacion formato, string extension)
    {
        var (rodeo, _) = RodeoConTrabajo();
        var original = (byte[])_e.Base.Escalar("SELECT ArchivoOrigenBlob FROM Rodeo WHERE Id = $id", ("$id", rodeo))!;

        var r = await _e.Servicio.ExportarAsync(rodeo, formato);

        Assert.True(r.EsCorrecto, r.Mensaje);
        var guardado = Assert.Single(_e.Entrega.Guardados);
        Assert.Equal($"tambo-norte_aviss_20261116_1200.{extension}", guardado.Nombre);
        Assert.Equal(guardado.Nombre, r.Valor.Archivo.Nombre);
        Assert.Equal("Descargas", r.Valor.Archivo.Lugar);
        Assert.Equal(formato, r.Valor.Formato);
        Assert.Equal(4, r.Valor.Filas);
        Assert.Equal(2, r.Valor.VacasConEvento);
        Assert.True(r.Valor.ConstanciaGuardada);

        // El archivo entregado tiene las columnas nuevas con lo que corresponde: solo las vacas con tacto (la CVET 0 incluida) llevan valores.
        var lectura = Leer(guardado.Contenido);
        Assert.Equal("16/11/2026", Celda(lectura, Pasada, ColumnasExportacion.Fecha.Nombre));
        Assert.Equal("1200", Celda(lectura, Pasada, ColumnasExportacion.Hora.Nombre));
        Assert.Equal("SECA", Celda(lectura, Pasada, ColumnasExportacion.Evento.Nombre));
        // La vaca de CVET 0 tactada a propósito sale como cualquier otra (decisión 2026-11-24).
        Assert.Equal("16/11/2026", Celda(lectura, CvetCero, ColumnasExportacion.Fecha.Nombre));
        Assert.Equal("SECA", Celda(lectura, CvetCero, ColumnasExportacion.Evento.Nombre));
        foreach (var vacia in new[] { SinPasar, Anulada })
            foreach (var columna in ColumnasExportacion.Todas)
                Assert.Equal("", Celda(lectura, vacia, columna.Nombre));

        // La constancia: una fila, con la huella del archivo entregado, y el rodeo figura exportado.
        Assert.Equal(1, _e.Cuenta("Exportacion"));
        Assert.Equal(guardado.Nombre, _e.Base.Escalar("SELECT ArchivoNombre FROM Exportacion"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(guardado.Contenido)).ToLowerInvariant(), _e.Base.Escalar("SELECT Hash FROM Exportacion"));
        Assert.Equal(formato.ToString(), _e.Base.Escalar("SELECT Formato FROM Exportacion"));
        Assert.Equal(4L, _e.Base.Escalar("SELECT CantidadFilas FROM Exportacion"));
        Assert.Equal(2L, _e.Base.Escalar("SELECT CantidadVacasConEvento FROM Exportacion"));
        Assert.Equal("Exportado", _e.EstadoDelRodeo(rodeo));

        // El original guardado en el rodeo no se tocó (RF-5).
        Assert.Equal(original, (byte[])_e.Base.Escalar("SELECT ArchivoOrigenBlob FROM Rodeo WHERE Id = $id", ("$id", rodeo))!);
    }

    [Fact]
    public async Task Tambien_exporta_un_original_xls()
    {
        var (rodeo, _) = RodeoConTrabajo(EscenarioExportacion.XlsOriginal([Pasada, SinPasar, CvetCero, Anulada]), "tambo-norte.xls");

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal("SECA", Celda(Leer(_e.Entrega.Guardados.Single().Contenido), Pasada, ColumnasExportacion.Evento.Nombre));
    }

    [Fact]
    public async Task El_nombre_lleva_la_hora_local_de_la_tablet()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Zona.Fijar(FakeZonaHoraria.ConDesfase(-3));

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal("tambo-norte_aviss_20261116_0900.csv", r.Valor.Archivo.Nombre);
    }

    [Fact]
    public async Task Exportar_dos_veces_no_pisa_el_archivo_y_deja_dos_constancias_sin_cambiar_los_datos()
    {
        var (rodeo, _) = RodeoConTrabajo();
        var antes = (_e.Cuenta("Tacto"), _e.Cuenta("Animal"), _e.Cuenta("Historial"));

        await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);
        await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(2, _e.Entrega.Guardados.Count);
        Assert.NotEqual(_e.Entrega.Guardados[0].Nombre, _e.Entrega.Guardados[1].Nombre);
        Assert.Equal(2, _e.Cuenta("Exportacion"));
        Assert.Equal(antes, (_e.Cuenta("Tacto"), _e.Cuenta("Animal"), _e.Cuenta("Historial")));
    }

    [Fact]
    public async Task Se_puede_seguir_tactando_despues_de_exportar_y_la_nueva_exportacion_lo_trae()
    {
        var (rodeo, _) = RodeoConTrabajo();
        await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);
        var sinPasar = _e.Base.Escalar("SELECT Id FROM Animal WHERE NumeroCaravana = $c", ("$c", SinPasar))!.ToString()!;
        _e.Base.Tacto(Guid.Parse(sinPasar), _e.EstadoVacia, fechaHora: _e.Reloj.UtcAhora.AddMinutes(10));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(20));

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(3, r.Valor.VacasConEvento);
        Assert.Equal("VACIA", Celda(Leer(_e.Entrega.Guardados[1].Contenido), SinPasar, ColumnasExportacion.Evento.Nombre));
    }

    [Fact]
    public async Task Un_rodeo_nuevo_no_trae_nada_del_anterior()
    {
        var (viejo, _) = RodeoConTrabajo();
        var estab = _e.Base.Escalar("SELECT EstablecimientoId FROM Rodeo WHERE Id = $id", ("$id", viejo))!.ToString()!;
        var nuevo = _e.Rodeo(EscenarioExportacion.CsvOriginal(Pasada, SinPasar), "tambo-norte.csv", Guid.Parse(estab), "Lote Nuevo");
        _e.Vaca(nuevo, Pasada);
        _e.Vaca(nuevo, SinPasar);

        var r = await _e.Servicio.ExportarAsync(nuevo, FormatoExportacion.Csv);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(0, r.Valor.VacasConEvento);
        var lectura = Leer(_e.Entrega.Guardados.Single().Contenido);
        foreach (var caravana in new[] { Pasada, SinPasar })
            Assert.Equal("", Celda(lectura, caravana, ColumnasExportacion.Evento.Nombre));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(viejo));   // exportar uno no marca el otro
    }

    // ---------------------------------------------------------------- Errores: nada se entrega ni se registra

    [Fact]
    public async Task Una_validacion_que_falla_no_guarda_ni_registra_nada()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Exportador.ResultadoValidar = new ResultadoValidacionArchivo(DiferenciaExportacion.ValorNuevoIncorrecto);

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.ExportacionNoValida, r.Codigo);
        Assert.Equal(MensajesExportacion.ExportacionNoValida, r.Mensaje);
        SinDatosDelTambo(r);
        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));
    }

    [Theory]
    [InlineData(FalloGuardado.SinEspacio)]
    [InlineData(FalloGuardado.SinPermiso)]
    [InlineData(FalloGuardado.NoDisponible)]
    [InlineData(FalloGuardado.Otro)]
    public async Task Si_no_se_puede_guardar_el_archivo_no_hay_constancia_y_el_rodeo_sigue_abierto(FalloGuardado fallo)
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Entrega.Fallo = fallo;

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Xls);

        Assert.Equal(CodigoError.ArchivoNoGuardado, r.Codigo);
        Assert.Equal(MensajesExportacion.Para(fallo), r.Mensaje);
        SinDatosDelTambo(r);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));
    }

    [Fact]
    public async Task Una_excepcion_al_guardar_se_vuelve_un_error_claro_sin_constancia()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Entrega.Excepcion = new IOException("disco lleno en /storage/emulated/0/tambo-norte");

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.ArchivoNoGuardado, r.Codigo);
        Assert.Equal(MensajesExportacion.Para(FalloGuardado.Otro), r.Mensaje);   // el texto de la excepción no se muestra
        SinDatosDelTambo(r);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
    }

    [Fact]
    public async Task Si_falla_la_constancia_el_archivo_queda_guardado_y_se_avisa()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Base.Ejecutar("CREATE TRIGGER TR_Prueba_NoConstancia BEFORE INSERT ON Exportacion BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.False(r.Valor.ConstanciaGuardada);
        Assert.Single(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));   // la transacción revierte la constancia y la marca juntas
    }

    [Fact]
    public async Task Un_rodeo_inexistente_o_de_baja_no_genera_nada()
    {
        var (rodeo, _) = RodeoConTrabajo();
        var inexistente = await _e.Servicio.ExportarAsync(Guid.NewGuid(), FormatoExportacion.Csv);
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0 WHERE Id = $id", ("$id", rodeo));
        var baja = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.NoEncontrado, inexistente.Codigo);
        Assert.Equal(CodigoError.NoEncontrado, baja.Codigo);
        SinDatosDelTambo(baja);
        Assert.Equal(0, _e.Exportador.Generaciones);
        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
    }

    [Fact]
    public async Task Un_original_ilegible_no_genera_nada()
    {
        var (rodeo, _) = RodeoConTrabajo(new byte[] { 0, 1, 2, 3, 4 });

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.ArchivoRechazado, r.Codigo);
        Assert.Equal(MensajesExportacion.OriginalIlegible, r.Mensaje);
        SinDatosDelTambo(r);
        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
    }

    [Fact]
    public async Task Un_xls_con_caracteres_que_el_csv_no_admite_orienta_a_exportar_en_xls_sin_datos_del_tambo()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Exportador.ResultadoGenerar = ResultadoGeneracionArchivo.Fallido(FalloGeneracion.CsvNoAdmiteCaracteres);

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.CsvNoAdmiteCaracteres, r.Codigo);
        Assert.Equal(MensajesExportacion.CsvNoAdmiteCaracteres, r.Mensaje);
        Assert.Contains(".xls", r.Mensaje!);
        SinDatosDelTambo(r);
        Assert.Empty(_e.Entrega.Guardados);
    }

    [Fact]
    public async Task Un_original_con_una_columna_nueva_ya_existente_se_rechaza_sin_registrar_nada()
    {
        var (rodeo, _) = RodeoConTrabajo(EscenarioExportacion.CsvConColumna("scdat", Pasada, SinPasar, CvetCero, Anulada));

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.ColumnaDuplicada, r.Codigo);
        Assert.Equal(MensajesExportacion.ColumnaDuplicada("SCDAT"), r.Mensaje);   // nombra la columna que chocó (RF-14), no datos del tambo
        Assert.DoesNotContain("CVET", r.Mensaje!);
        Assert.DoesNotContain("SCTIM", r.Mensaje!);
        SinDatosDelTambo(r);
        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));
    }

    [Fact]
    public async Task Una_excepcion_inesperada_al_generar_es_un_error_claro_sin_datos()
    {
        var (rodeo, _) = RodeoConTrabajo();
        _e.Exportador.ExcepcionAlGenerar = new InvalidOperationException("fila 7101 rota");

        var r = await _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv);

        Assert.Equal(CodigoError.ExportacionNoValida, r.Codigo);
        Assert.Equal(MensajesExportacion.NoSePudoGenerar, r.Mensaje);
        SinDatosDelTambo(r);
        Assert.Empty(_e.Entrega.Guardados);
    }

    // ---------------------------------------------------------------- Cancelación

    [Fact]
    public async Task Cancelar_antes_de_empezar_no_guarda_ni_registra_nada()
    {
        var (rodeo, _) = RodeoConTrabajo();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv, cts.Token));

        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));
    }

    [Fact]
    public async Task Cancelar_justo_al_guardar_no_deja_archivo_ni_constancia()
    {
        var (rodeo, _) = RodeoConTrabajo();
        using var cts = new CancellationTokenSource();
        _e.Entrega.AlGuardar = cts.Cancel;   // el veterinario toca "Cancelar" mientras se escribe el archivo

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _e.Servicio.ExportarAsync(rodeo, FormatoExportacion.Csv, cts.Token));

        Assert.Empty(_e.Entrega.Guardados);
        Assert.Equal(0, _e.Cuenta("Exportacion"));
        Assert.Equal("Abierto", _e.EstadoDelRodeo(rodeo));
    }
}
