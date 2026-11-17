using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-4 a RF-10 y RF-35: mangas, contador, cierre manual, "Terminar el tacto", cierre automático y reapertura.</summary>
public sealed class MangaServiceTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<EstadoTrabajoDto>> Iniciar(Guid? rodeo = null, Guid? usuario = null)
        => _e.Campo.IniciarTrabajoAsync(rodeo ?? _e.RodeoId, usuario ?? _e.UsuarioId);

    private async Task MarcarVarias(int cantidad, string prefijo = "A")
    {
        for (var i = 0; i < cantidad; i++)
            await _e.VacaMarcadaAsync($"{prefijo}{i}");
    }

    // ---------- Empezar a trabajar (RF-4) ----------

    [Fact]
    public async Task Sin_mangas_se_muestra_la_manga_1_por_empezar_con_contador_cero_y_no_se_guarda_nada()
    {
        var r = await Iniciar();

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(1, r.Valor.NumeroManga);
        Assert.False(r.Valor.MangaAbierta);
        Assert.Null(r.Valor.MangaId);
        Assert.Equal(0, r.Valor.Contador);
        Assert.Equal(0, _e.Cuenta("Manga")); // la manga existe recién con la primera marca
    }

    [Fact]
    public async Task Si_hay_una_manga_abierta_de_hoy_se_retoma_con_su_contador()
    {
        await MarcarVarias(3);

        var r = await Iniciar();

        Assert.True(r.Valor.MangaAbierta);
        Assert.Equal(1, r.Valor.NumeroManga);
        Assert.Equal(3, r.Valor.Contador);
        Assert.Equal(0, _e.Cuenta("CierreManga"));
    }

    [Fact]
    public async Task Iniciar_trabajo_exige_un_veterinario_activo_y_un_rodeo_vigente()
    {
        var baja = _e.Base.Usuario("De baja", activo: false);
        Assert.Equal(CodigoError.UsuarioDeBaja, (await Iniciar(usuario: baja)).Codigo);
        Assert.Equal(CodigoError.NoEncontrado, (await Iniciar(rodeo: Guid.NewGuid())).Codigo);

        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");
        Assert.Equal(CodigoError.RodeoDeBaja, (await Iniciar()).Codigo);
    }

    [Fact]
    public async Task El_estado_de_trabajo_incluye_el_avance_del_dia()
    {
        _e.AnimalImportado("1");
        _e.AnimalImportado("2");
        _e.AnimalImportado("3");
        _e.AnimalImportado("4");
        await _e.LeerAsync("1");

        var estado = await _e.EstadoAsync();

        Assert.Equal(4, estado.Avance.TotalVacas);
        Assert.Equal(1, estado.Avance.Procesadas);
        Assert.Equal(3, estado.Avance.Pendientes);
        Assert.Equal(25, estado.Avance.Porcentaje);
        Assert.Equal(DateOnly.FromDateTime(_e.Reloj.UtcAhora), estado.Avance.Fecha);
    }

    // ---------- Cierre automático de un día anterior (RF-10) ----------

    [Fact]
    public async Task Una_manga_abierta_de_un_dia_anterior_se_cierra_sola_al_empezar_a_trabajar()
    {
        var ayer = _e.Reloj.UtcAhora.AddDays(-1);
        var animal = _e.AnimalImportado("100");
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId, creadaEn: ayer);
        _e.Base.MarcaPasada(animal, manga, _e.UsuarioId, fechaHora: ayer);

        var r = await Iniciar();

        Assert.True(r.EsCorrecto, r.Mensaje);
        // El cierre es automático, sin cantidad confirmada ni diferencia, con lo que contaba Aviss.
        var cierre = Guid.Parse(_e.Base.Escalar("SELECT Id FROM CierreManga")!.ToString()!);
        Assert.Equal("Automatico", _e.Col("CierreManga", cierre, "Tipo"));
        Assert.Equal(T(manga), _e.Col("CierreManga", cierre, "MangaId"));
        Assert.Equal("1", _e.Col("CierreManga", cierre, "CantidadAviss"));
        Assert.Null(_e.Col("CierreManga", cierre, "CantidadConfirmada"));
        Assert.Null(_e.Col("CierreManga", cierre, "Diferencia"));
        Assert.Equal("0", _e.Col("CierreManga", cierre, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MangaCerrada' AND ValorNuevo = 'Automatico'"));
        // Y se muestra la siguiente como manga por empezar.
        Assert.Equal(2, r.Valor.NumeroManga);
        Assert.False(r.Valor.MangaAbierta);
        Assert.Equal(0, r.Valor.Contador);
    }

    [Fact]
    public async Task No_hay_corte_a_medianoche_con_la_app_abierta()
    {
        // Se marca a las 23:50 y se sigue marcando pasada la medianoche: es la misma manga, nada se cierra.
        _e.Reloj.Fijar(new DateTime(2026, 11, 16, 23, 50, 0, DateTimeKind.Utc));
        await _e.VacaMarcadaAsync("100");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(20)); // 00:10 del día siguiente
        var (_, lectura) = await _e.VacaMarcadaAsync("101");

        var estado = await _e.EstadoAsync();

        Assert.Equal(1, lectura.NumeroManga);
        Assert.Equal(2, estado.Contador);
        Assert.True(estado.MangaAbierta);
        Assert.Equal(0, _e.Cuenta("CierreManga"));
        Assert.Equal(1, _e.Cuenta("Manga"));
        // El avance sí cambió de día: la vaca marcada anoche ya no cuenta como procesada de hoy.
        Assert.Equal(1, estado.Avance.Procesadas);
    }

    [Fact]
    public async Task Una_vaca_marcada_antes_de_medianoche_y_leida_despues_en_la_misma_manga_es_animal_repetido_y_no_falla()
    {
        _e.Reloj.Fijar(new DateTime(2026, 11, 16, 23, 50, 0, DateTimeKind.Utc));
        await _e.VacaMarcadaAsync("100");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(20)); // 00:10 del día siguiente: para el avance la vaca ya no está procesada
        var antes = _e.FotoCampo();

        var r = await _e.LeerAsync("100");

        // La manga sigue abierta y la vaca ya está marcada en ella: no se la cuenta dos veces (RF-5).
        Assert.Equal(TipoResultadoLectura.Repetido, r.Tipo);
        Assert.Equal(1, r.UltimoRegistro!.NumeroManga);
        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(1, (await _e.EstadoAsync()).Contador);
    }

    [Fact]
    public async Task Al_cortarse_la_energia_la_manga_queda_abierta_con_su_contador()
    {
        await MarcarVarias(2);
        // "Reabrir la app" = empezar a trabajar de nuevo el mismo día: la manga sigue abierta, intacta.
        var r = await Iniciar();
        Assert.True(r.Valor.MangaAbierta);
        Assert.Equal(2, r.Valor.Contador);
    }

    // ---------- Finalizar manga (RF-6) ----------

    [Fact]
    public async Task El_resumen_de_la_manga_trae_el_contador_y_las_caravanas_en_orden()
    {
        await _e.VacaMarcadaAsync("30");
        await _e.VacaMarcadaAsync("10");
        await _e.VacaMarcadaAsync("20");

        var r = await _e.Campo.ObtenerResumenMangaAsync(_e.RodeoId);

        Assert.True(r.EsCorrecto);
        Assert.Equal(3, r.Valor.Contador);
        Assert.Equal(1, r.Valor.NumeroManga);
        Assert.Equal(["30", "10", "20"], r.Valor.Caravanas);
    }

    [Fact]
    public async Task Finalizar_con_Coincide_cierra_la_manga_y_deja_la_siguiente_por_empezar()
    {
        await MarcarVarias(3);
        var avanceAntes = (await _e.EstadoAsync()).Avance;

        var r = await _e.FinalizarAsync();

        var c = r.Cierre!;
        Assert.Equal(TipoCierreManga.Manual, c.Tipo);
        Assert.Equal(3, c.CantidadAviss);
        Assert.Equal(3, c.CantidadConfirmada);
        Assert.Equal(0, c.Diferencia);
        Assert.Equal(1, c.NumeroManga);
        Assert.Equal(T(_e.UsuarioId), _e.Col("CierreManga", c.CierreId, "UsuarioId"));
        Assert.Equal(T(_e.DispositivoId), _e.Col("CierreManga", c.CierreId, "DispositivoId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("CierreManga", c.CierreId, "FechaHoraUtc"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MangaCerrada' AND ValorNuevo = 'Manual'"));

        var estado = await _e.EstadoAsync();
        Assert.Equal(2, estado.NumeroManga);
        Assert.False(estado.MangaAbierta);
        Assert.Equal(0, estado.Contador);
        Assert.Equal(avanceAntes, estado.Avance); // cerrar una manga no reinicia el avance del día
    }

    [Fact]
    public async Task Finalizar_con_No_coincide_guarda_la_cantidad_real_y_la_diferencia()
    {
        await MarcarVarias(3);

        var r = await _e.FinalizarAsync(cantidadReal: 5);

        Assert.Equal(3, r.Cierre!.CantidadAviss);
        Assert.Equal(5, r.Cierre.CantidadConfirmada);
        Assert.Equal(2, r.Cierre.Diferencia);
        Assert.Equal("2", _e.Col("CierreManga", r.Cierre.CierreId, "Diferencia"));

        // Y si Aviss contó de más, la diferencia es negativa.
        await MarcarVarias(4, "B");
        var r2 = await _e.FinalizarAsync(cantidadReal: 1);
        Assert.Equal(-3, r2.Cierre!.Diferencia);
    }

    [Fact]
    public async Task Una_cantidad_negativa_se_rechaza_sin_cerrar_nada()
    {
        await MarcarVarias(2);
        var antes = _e.FotoCampo();

        var r = await _e.Campo.FinalizarMangaAsync(new FinalizarMangaCommand(_e.RodeoId, -1, _e.UsuarioId));

        Assert.Equal(CodigoError.CantidadInvalida, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Finalizar_sin_manga_abierta_avisa_que_no_hay_nada_para_finalizar()
    {
        var r = await _e.Campo.FinalizarMangaAsync(new FinalizarMangaCommand(_e.RodeoId, null, _e.UsuarioId));
        Assert.Equal(CodigoError.NoHayMangaAbierta, r.Codigo);

        var resumen = await _e.Campo.ObtenerResumenMangaAsync(_e.RodeoId);
        Assert.Equal(CodigoError.NoHayMangaAbierta, resumen.Codigo);
        Assert.Equal(0, _e.Cuenta("CierreManga"));
    }

    [Fact]
    public async Task Finalizar_con_un_veterinario_de_baja_se_rechaza()
    {
        await MarcarVarias(1);
        var baja = _e.Base.Usuario("De baja", activo: false);
        var r = await _e.Campo.FinalizarMangaAsync(new FinalizarMangaCommand(_e.RodeoId, null, baja));
        Assert.Equal(CodigoError.UsuarioDeBaja, r.Codigo);
        Assert.Equal(0, _e.Cuenta("CierreManga"));
    }

    [Fact]
    public async Task La_primera_marca_despues_de_cerrar_abre_la_manga_siguiente_con_el_numero_que_sigue()
    {
        await MarcarVarias(2);
        await _e.FinalizarAsync();

        var (_, lectura) = await _e.VacaMarcadaAsync("Z1");

        Assert.Equal(2, lectura.NumeroManga);
        Assert.Equal(1, lectura.ContadorManga);
        Assert.Equal(2, _e.Cuenta("Manga"));
        Assert.Equal(2, _e.Cuenta("Historial", "Accion = 'MangaAbierta'"));
    }

    [Fact]
    public async Task Una_falla_a_mitad_de_cerrar_no_deja_ningun_cierre()
    {
        await MarcarVarias(2);
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("MangaCerrada");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _e.Campo.FinalizarMangaAsync(new FinalizarMangaCommand(_e.RodeoId, null, _e.UsuarioId)));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal(0, _e.Cuenta("CierreManga"));
        Assert.True((await _e.EstadoAsync()).MangaAbierta); // la manga abierta quedó intacta
    }

    // ---------- Terminar el tacto (RF-7) ----------

    [Fact]
    public async Task Terminar_el_tacto_con_manga_abierta_la_cierra_marcada_como_termino_el_tacto()
    {
        await MarcarVarias(3);

        var r = await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, 4, _e.UsuarioId));

        Assert.True(r.EsCorrecto, r.Mensaje);
        var c = r.Valor.Cierre!;
        Assert.Equal(TipoCierreManga.TerminoElTacto, c.Tipo);
        Assert.Equal(3, c.CantidadAviss);
        Assert.Equal(4, c.CantidadConfirmada);
        Assert.Equal(1, c.Diferencia);
        Assert.Equal(3, r.Valor.Avance.Procesadas);
        Assert.Equal(3, r.Valor.Avance.TotalVacas);
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'MangaCerrada' AND ValorNuevo = 'TerminoElTacto'"));
    }

    [Fact]
    public async Task Terminar_el_tacto_sin_manga_abierta_no_guarda_nada()
    {
        _e.AnimalImportado("1");
        var antes = _e.FotoCampo();

        var r = await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Null(r.Valor.Cierre);
        Assert.Equal(1, r.Valor.Avance.TotalVacas);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Despues_de_terminar_el_tacto_se_puede_volver_a_trabajar_el_mismo_dia_con_el_avance_intacto()
    {
        await MarcarVarias(2);
        _e.AnimalImportado("otra");
        await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId));

        var r = await Iniciar();

        Assert.Equal(2, r.Valor.NumeroManga);
        Assert.False(r.Valor.MangaAbierta);
        Assert.Equal(2, r.Valor.Avance.Procesadas);   // no se reinicia
        Assert.Equal(3, r.Valor.Avance.TotalVacas);
        Assert.Equal("Abierto", _e.Base.Escalar("SELECT Estado FROM Rodeo")); // el rodeo no se bloquea ni cambia de estado
    }

    // ---------- Deshacer un cierre: reabrir la manga (RF-9) ----------

    [Fact]
    public async Task Deshacer_el_ultimo_cierre_reabre_la_manga_con_su_contador()
    {
        await MarcarVarias(3);
        var cierre = (await _e.FinalizarAsync()).Cierre!;

        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(TipoDeshacer.Cierre, r.Valor.Tipo);
        Assert.Equal(1, r.Valor.NumeroManga);
        Assert.Equal("1", _e.Col("CierreManga", cierre.CierreId, "Anulado"));
        Assert.Equal(1, _e.Cuenta("Historial", "Accion = 'AnulacionCierre'"));

        var estado = await _e.EstadoAsync();
        Assert.True(estado.MangaAbierta);
        Assert.Equal(1, estado.NumeroManga);
        Assert.Equal(3, estado.Contador);
    }

    [Fact]
    public async Task Despues_de_reabrir_se_puede_cerrar_de_nuevo_con_un_cierre_nuevo()
    {
        await MarcarVarias(2);
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        var otro = (await _e.FinalizarAsync(cantidadReal: 2)).Cierre!;

        Assert.NotEqual(cierre.CierreId, otro.CierreId);
        Assert.Equal(2, _e.Cuenta("CierreManga"));
        Assert.Equal(1, _e.Cuenta("CierreManga", "Anulado = 0"));
    }

    [Fact]
    public async Task Una_anulacion_de_cierre_no_se_puede_deshacer()
    {
        await MarcarVarias(1);
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        var otra = await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        Assert.Equal(CodigoError.CierreYaAnulado, otra.Codigo);
    }

    [Fact]
    public async Task No_se_deshace_un_cierre_que_no_es_el_ultimo_del_rodeo()
    {
        await MarcarVarias(1, "A");
        var primero = (await _e.FinalizarAsync()).Cierre!;
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await MarcarVarias(1, "B");
        await _e.FinalizarAsync();

        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, primero.CierreId);

        Assert.Equal(CodigoError.CierreNoEsElUltimo, r.Codigo);
        Assert.Equal("0", _e.Col("CierreManga", primero.CierreId, "Anulado"));
    }

    [Fact]
    public async Task No_se_deshace_un_cierre_automatico()
    {
        var ayer = _e.Reloj.UtcAhora.AddDays(-1);
        var animal = _e.AnimalImportado("100");
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId, creadaEn: ayer);
        _e.Base.MarcaPasada(animal, manga, _e.UsuarioId, fechaHora: ayer);
        await Iniciar(); // lo cierra sola
        var cierre = Guid.Parse(_e.Base.Escalar("SELECT Id FROM CierreManga")!.ToString()!);

        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre);

        Assert.Equal(CodigoError.CierreAutomaticoNoSeDeshace, r.Codigo);
    }

    [Fact]
    public async Task No_se_deshace_un_cierre_de_otro_dia()
    {
        await MarcarVarias(1);
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        _e.Reloj.Avanzar(TimeSpan.FromDays(1));

        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        Assert.Equal(CodigoError.CierreDeOtroDia, r.Codigo);
    }

    [Fact]
    public async Task No_se_deshace_un_cierre_si_ya_se_marco_una_vaca_en_la_manga_siguiente()
    {
        await MarcarVarias(1, "A");
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        await MarcarVarias(1, "B"); // abre la manga 2
        var antes = _e.FotoCampo();

        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        Assert.Equal(CodigoError.HayOtraMangaAbierta, r.Codigo);
        Assert.Equal(antes, _e.FotoCampo());
    }

    [Fact]
    public async Task Deshacer_un_cierre_que_no_existe_se_rechaza()
    {
        var r = await _e.DeshacerAsync(TipoDeshacer.Cierre, Guid.NewGuid());
        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Una_falla_a_mitad_de_reabrir_deja_el_cierre_como_estaba()
    {
        await MarcarVarias(2);
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        var antes = _e.FotoCampo();
        _e.ForzarFallaAlGuardarHistorial("AnulacionCierre");

        await Assert.ThrowsAnyAsync<Exception>(() => _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId));

        Assert.Equal(antes, _e.FotoCampo());
        Assert.Equal("0", _e.Col("CierreManga", cierre.CierreId, "Anulado"));
    }

    // ---------- Desmarcar en una manga cerrada (RF-8) ----------

    [Fact]
    public async Task Desmarcar_una_vaca_de_una_manga_cerrada_conserva_el_cierre_y_muestra_las_dos_cifras()
    {
        await MarcarVarias(3);
        var cierre = (await _e.FinalizarAsync()).Cierre!;
        var ultima = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).First(m => m.Tipo == TipoMovimiento.Marca);

        var r = await _e.DeshacerAsync(TipoDeshacer.Marca, ultima.RegistroId);
        Assert.True(r.EsCorrecto, r.Mensaje);

        // El cierre guardó 3 y sigue diciendo 3; el contador de hoy es 2 y se muestran las dos.
        Assert.Equal("3", _e.Col("CierreManga", cierre.CierreId, "CantidadAviss"));
        var movimiento = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.Tipo == TipoMovimiento.Cierre);
        Assert.Equal(3, movimiento.CantidadAviss);
        Assert.Equal(3, movimiento.CantidadConfirmada);
        Assert.Equal(2, movimiento.ContadorActual);
    }
}
