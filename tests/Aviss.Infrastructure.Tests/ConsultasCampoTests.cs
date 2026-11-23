using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-1, RF-2, RF-13, RF-14, RF-28, RF-30: rodeos para trabajar, avance, ficha, movimientos y listas.</summary>
public sealed class ConsultasCampoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    // ---------- Rodeos para trabajar (RF-2) ----------

    [Fact]
    public async Task Lista_los_rodeos_activos_incluido_el_exportado_y_deja_afuera_los_de_baja()
    {
        // _e.RodeoId (Abierto) ya existe. Se suman uno Exportado, uno de baja y un establecimiento de baja con su rodeo.
        var exportado = _e.Base.Rodeo(_e.EstablecimientoId, hash: new string('b', 64), estado: "Exportado");
        var deBaja = _e.Base.Rodeo(_e.EstablecimientoId, hash: new string('c', 64));
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0 WHERE Id = $i", ("$i", deBaja));
        var estBaja = _e.Base.Establecimiento("Tambo cerrado");
        var rodeoDeEstBaja = _e.Base.Rodeo(estBaja, hash: new string('d', 64));
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0 WHERE Id = $i", ("$i", estBaja));

        var lista = await _e.Campo.ListarRodeosParaTrabajarAsync();

        Assert.Equal(new[] { _e.RodeoId, exportado }.Order(), lista.Select(r => r.RodeoId).Order());
        Assert.DoesNotContain(lista, r => r.RodeoId == deBaja || r.RodeoId == rodeoDeEstBaja);
        Assert.Equal(EstadoRodeo.Exportado, lista.Single(r => r.RodeoId == exportado).Estado);
        Assert.All(lista, r => Assert.Equal("Tambo Uno", r.EstablecimientoNombre));
    }

    [Fact]
    public async Task Cada_rodeo_trae_su_avance()
    {
        _e.AnimalImportado("1");
        _e.AnimalImportado("2");
        await _e.LeerAsync("1");

        var rodeo = (await _e.Campo.ListarRodeosParaTrabajarAsync()).Single();

        Assert.Equal(2, rodeo.Avance.TotalVacas);
        Assert.Equal(1, rodeo.Avance.Procesadas);
        Assert.Equal(1, rodeo.Avance.Pendientes);
        Assert.Equal(50, rodeo.Avance.Porcentaje);
    }

    [Fact]
    public async Task Sin_rodeos_importados_la_lista_esta_vacia()
    {
        using var vacio = new EscenarioConServicios(conDatosBase: false);
        Assert.Empty(await vacio.Campo.ListarRodeosParaTrabajarAsync());
    }

    // ---------- Avance del día (RF-30) ----------

    [Fact]
    public async Task Un_rodeo_vacio_tiene_avance_cero_y_no_falla()
    {
        var avance = (await _e.EstadoAsync()).Avance;
        Assert.Equal(0, avance.TotalVacas);
        Assert.Equal(0, avance.Procesadas);
        Assert.Equal(0, avance.Pendientes);
        Assert.Equal(0, avance.Porcentaje);
    }

    [Fact]
    public async Task Cuentan_como_procesadas_las_que_tienen_marca_o_tacto_de_hoy_y_no_las_de_ayer()
    {
        var conMarca = _e.AnimalImportado("1");
        var conTacto = _e.AnimalImportado("2", _e.EstadoSeca);
        var deAyer = _e.AnimalImportado("3", _e.EstadoSeca);
        var ninguna = _e.AnimalImportado("4");
        var anulada = _e.Base.Animal(_e.RodeoId, "5", "AltaEnCampo", anulado: true);

        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId);
        _e.Base.MarcaPasada(conMarca, manga, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddHours(-1));
        _e.Base.Tacto(conTacto, _e.UsuarioId, _e.EstadoPrenada, fechaHora: _e.Reloj.UtcAhora.AddHours(-1));
        _e.Base.Tacto(deAyer, _e.UsuarioId, _e.EstadoPrenada, fechaHora: _e.Reloj.UtcAhora.AddDays(-1));
        _ = ninguna; _ = anulada;

        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Equal(4, avance.TotalVacas);   // la anulada no cuenta
        Assert.Equal(2, avance.Procesadas);
        Assert.Equal(2, avance.Pendientes);
        Assert.Equal(50, avance.Porcentaje);
    }

    [Fact]
    public async Task Una_vaca_con_marca_y_tacto_de_hoy_se_cuenta_una_sola_vez()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("1", _e.EstadoSeca);
        await _e.TactoAsync(animal, _e.EstadoPrenada);

        Assert.Equal(1, (await _e.EstadoAsync()).Avance.Procesadas);
    }

    [Fact]
    public async Task Una_marca_anulada_o_un_tacto_anulado_no_cuentan()
    {
        var a = _e.AnimalImportado("1");
        var b = _e.AnimalImportado("2", _e.EstadoSeca);
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId);
        _e.Base.MarcaPasada(a, manga, _e.UsuarioId, anulada: true);
        _e.Base.Tacto(b, _e.UsuarioId, _e.EstadoPrenada, anulado: true);

        Assert.Equal(0, (await _e.EstadoAsync()).Avance.Procesadas);
    }

    // ---------- Horario de la primera y de la última vaca del día ----------

    [Fact]
    public async Task Sin_registros_de_hoy_no_hay_horarios()
    {
        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Null(avance.PrimeraVacaUtc);
        Assert.Null(avance.FinUtc);
        Assert.False(avance.Terminado);
    }

    [Fact]
    public async Task El_horario_es_el_de_la_primera_vaca_y_el_de_la_ultima_que_se_detecto()
    {
        var primera = _e.Reloj.UtcAhora;
        await _e.VacaMarcadaAsync("1");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(20));
        await _e.VacaMarcadaAsync("2");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(25));
        var ultima = _e.Reloj.UtcAhora;
        await _e.VacaMarcadaAsync("3");

        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Equal(primera, avance.PrimeraVacaUtc);
        Assert.Equal(ultima, avance.FinUtc);
        Assert.False(avance.Terminado);
    }

    [Fact]
    public async Task Un_tacto_posterior_a_la_ultima_marca_tambien_cuenta_como_ultimo_movimiento()
    {
        var (animal, _) = await _e.VacaMarcadaAsync("1", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        var instante = _e.Reloj.UtcAhora;
        await _e.TactoAsync(animal, _e.EstadoPrenada);

        Assert.Equal(instante, (await _e.EstadoAsync()).Avance.FinUtc);
    }

    [Fact]
    public async Task Lo_de_ayer_y_lo_anulado_no_cuentan_para_los_horarios()
    {
        var ayer = _e.AnimalImportado("1");
        var anulada = _e.AnimalImportado("2");
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId);
        _e.Base.MarcaPasada(ayer, manga, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddDays(-1));
        _e.Base.MarcaPasada(anulada, manga, _e.UsuarioId, fechaHora: _e.Reloj.UtcAhora.AddMinutes(-30), anulada: true);

        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Null(avance.PrimeraVacaUtc);
        Assert.Null(avance.FinUtc);
    }

    [Fact]
    public async Task Al_terminar_el_tacto_el_fin_es_la_hora_en_que_se_apreto_el_boton()
    {
        await _e.VacaMarcadaAsync("1");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        await _e.VacaMarcadaAsync("2");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(7));
        var terminar = _e.Reloj.UtcAhora;

        var cierre = await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId));
        Assert.True(cierre.EsCorrecto, cierre.Mensaje);

        var avance = (await _e.EstadoAsync()).Avance;
        Assert.Equal(terminar, avance.FinUtc);
        Assert.True(avance.Terminado);
    }

    [Fact]
    public async Task Si_despues_de_terminar_se_lee_otra_vaca_el_fin_vuelve_a_ser_la_ultima_vaca()
    {
        await _e.VacaMarcadaAsync("1");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        var nueva = _e.Reloj.UtcAhora;
        await _e.VacaMarcadaAsync("2");

        var avance = (await _e.EstadoAsync()).Avance;

        Assert.Equal(nueva, avance.FinUtc);
        Assert.False(avance.Terminado);
    }

    [Fact]
    public async Task Deshacer_el_terminar_el_tacto_quita_ese_horario_de_fin()
    {
        await _e.VacaMarcadaAsync("1");
        var ultimaVaca = _e.Reloj.UtcAhora;
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        var cierre = (await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId))).Valor.Cierre!;

        await _e.DeshacerAsync(TipoDeshacer.Cierre, cierre.CierreId);

        var avance = (await _e.EstadoAsync()).Avance;
        Assert.Equal(ultimaVaca, avance.FinUtc);
        Assert.False(avance.Terminado);
    }

    [Fact]
    public async Task Un_cambio_de_zona_horaria_cambia_que_se_cuenta_como_hoy_sin_tocar_lo_guardado()
    {
        var animal = _e.AnimalImportado("1");
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId);
        // Marca a las 01:00 UTC; "ahora" son las 12:00 UTC del mismo día.
        _e.Base.MarcaPasada(animal, manga, _e.UsuarioId, fechaHora: new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc));
        _e.Base.CierreManga(manga, _e.UsuarioId, fechaHora: new DateTime(2026, 11, 16, 2, 0, 0, DateTimeKind.Utc)); // manga cerrada: la lectura de hoy abre otra
        var antes = _e.FotoCampo();

        Assert.Equal(1, (await _e.EstadoAsync()).Avance.Procesadas);   // en UTC es el mismo día

        // En UTC+13 ahora es el 17/11 a la 01:00 y la marca es del 16/11 a las 14:00: de ayer.
        _e.Zona.Fijar(FakeZonaHoraria.ConDesfase(13));
        var avance = (await _e.EstadoAsync()).Avance;
        Assert.Equal(0, avance.Procesadas);
        Assert.Equal(new DateOnly(2026, 11, 17), avance.Fecha);
        Assert.Equal(antes, _e.FotoCampo());

        // Y también cambia el aviso: leerla de nuevo la marca (ya no está procesada hoy).
        var lectura = await _e.LeerAsync("1");
        Assert.Equal(TipoResultadoLectura.Marcada, lectura.Tipo);
    }

    // ---------- Ficha (RF-13, RF-14) ----------

    [Fact]
    public async Task La_ficha_trae_los_campos_del_excel_el_historial_y_los_tactos()
    {
        var json = """{"ID":"100","LACT":"3","DEL":"120","FPART":" 5/11/26","VINS":"2","UTORO":"TORO 9","COM":"cojera","FAPAR":"20/12/26"}""";
        var animal = _e.AnimalImportado("100", _e.EstadoSeca, json: json);
        await _e.TactoAsync(animal, _e.EstadoPrenada);

        var r = await _e.Campo.ObtenerFichaAsync(animal);

        Assert.True(r.EsCorrecto, r.Mensaje);
        var f = r.Valor;
        Assert.Equal("100", f.Caravana);
        Assert.Equal("PREÑADA", f.EstadoNombre);
        Assert.NotNull(f.FechaUltimoTactoUtc);
        Assert.Equal("3", f.Campos.Single(c => c.Nombre == "LACT").Valor);
        Assert.Equal("2", f.Inseminaciones);
        Assert.Equal("TORO 9", f.UltimoToro);
        Assert.Equal(["Último parto", "Tacto: PREÑADA", "Fecha probable de parto"], f.Historial.Select(h => h.Titulo));
        Assert.Equal(11, f.Historial[0].DiasTranscurridos);
    }

    [Fact]
    public async Task La_ficha_de_un_animal_con_datos_rotos_se_arma_igual()
    {
        var animal = _e.AnimalImportado("100", json: "{esto no es json");
        var r = await _e.Campo.ObtenerFichaAsync(animal);
        Assert.True(r.EsCorrecto);
        Assert.All(r.Valor.Campos, c => Assert.Equal("", c.Valor));
    }

    [Fact]
    public async Task La_ficha_de_un_animal_que_no_existe_se_rechaza()
    {
        Assert.Equal(CodigoError.NoEncontrado, (await _e.Campo.ObtenerFichaAsync(Guid.NewGuid())).Codigo);
    }

    // ---------- Últimos movimientos (RF-28) ----------

    [Fact]
    public async Task Los_movimientos_son_los_50_mas_recientes_con_la_ultima_marca_primero()
    {
        for (var i = 0; i < 60; i++)
        {
            await _e.VacaMarcadaAsync($"V{i}");
            _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        }

        var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        Assert.Equal(50, lista.Count);
        Assert.All(lista, m => Assert.Equal(TipoMovimiento.Marca, m.Tipo));
        Assert.Equal("V59", lista[0].Caravana);
        Assert.Equal("V10", lista[49].Caravana);
        Assert.Equal(lista.OrderByDescending(m => m.FechaHoraUtc).Select(m => m.RegistroId), lista.Select(m => m.RegistroId));
        Assert.All(lista, m => { Assert.True(m.PuedeDeshacer); Assert.Equal(1, m.NumeroManga); Assert.Equal("Vet Uno", m.Veterinario); });
    }

    [Fact]
    public async Task Los_movimientos_mezclan_marcas_tactos_y_cierres_y_marcan_lo_anulado()
    {
        var (animal, lectura) = await _e.VacaMarcadaAsync("100", _e.EstadoSeca);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var tacto = await _e.TactoAsync(animal, _e.EstadoPrenada);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        await _e.VacaMarcadaAsync("900");
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var cierre = (await _e.FinalizarAsync()).Cierre!;

        var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        // Más reciente primero: cierre, marca de la vaca 900, tacto, marca de la vaca 100 (la anulación del tacto no se lista).
        Assert.Equal([TipoMovimiento.Cierre, TipoMovimiento.Marca, TipoMovimiento.Tacto, TipoMovimiento.Marca],
            lista.Select(m => m.Tipo));
        var mCierre = lista[0];
        Assert.Equal(cierre.CierreId, mCierre.RegistroId);
        Assert.Equal(1, mCierre.NumeroManga);
        Assert.Equal(2, mCierre.CantidadAviss);
        Assert.Equal(2, mCierre.CantidadConfirmada);
        Assert.Equal("Manual", mCierre.Detalle);
        Assert.True(mCierre.PuedeDeshacer);
        Assert.Equal("900", lista[1].Caravana);
        Assert.Equal("PREÑADA", lista[2].Detalle);
        // Deshacible: el cierre (último del rodeo), la marca de la 900 y el tacto (último de su vaca); la marca de la 100 no.
        Assert.True(lista[1].PuedeDeshacer);
        Assert.True(lista[2].PuedeDeshacer);
        Assert.False(lista[3].PuedeDeshacer); // la marca de la vaca 100 quedó detrás del tacto

        // Deshacer el tacto lo deja marcado como anulado y ya no se puede volver a deshacer.
        await _e.DeshacerAsync(TipoDeshacer.Tacto, tacto);
        var despues = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);
        var mTacto = despues.Single(m => m.Tipo == TipoMovimiento.Tacto);
        Assert.True(mTacto.Anulado);
        Assert.False(mTacto.PuedeDeshacer);
        Assert.True(despues.Last(m => m.Tipo == TipoMovimiento.Marca).PuedeDeshacer); // la marca de la 100 volvió a ser la última
        _ = lectura;
    }

    [Fact]
    public async Task Los_movimientos_traen_el_cvet_de_la_vaca_y_la_observacion_del_tacto()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca, json: """{"ID":"100","CVET":"PRE"}""");
        await _e.LeerAsync("100");
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        var r = await _e.Campo.RegistrarTactoAsync(new Aviss.Application.Comandos.RegistrarTactoCommand(animal, _e.EstadoPrenada, null, "Cojera", _e.UsuarioId));
        Assert.True(r.EsCorrecto, r.Mensaje);
        var sinCvet = _e.AnimalImportado("200", _e.EstadoSeca);
        await _e.LeerAsync("200");

        var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        var tacto = lista.Single(m => m.Tipo == TipoMovimiento.Tacto);
        Assert.Equal("PRE", tacto.Cvet);
        Assert.Equal("Cojera", tacto.Observaciones);
        Assert.Equal("PREÑADA", tacto.Detalle);
        Assert.Equal("PRE", lista.Single(m => m.Tipo == TipoMovimiento.Marca && m.Caravana == "100").Cvet);
        Assert.Equal("", lista.Single(m => m.Tipo == TipoMovimiento.Marca && m.Caravana == "200").Cvet);
        _ = sinCvet;
    }

    [Fact]
    public async Task Una_marca_desmarcada_sale_anulada_y_ya_no_se_puede_deshacer()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        await _e.DeshacerAsync(TipoDeshacer.Marca, lectura.MarcaId!.Value);

        var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        var marcaDesmarcada = lista.Single(m => m.RegistroId == lectura.MarcaId);
        Assert.True(marcaDesmarcada.Anulado);
        Assert.False(marcaDesmarcada.PuedeDeshacer);
    }

    [Fact]
    public async Task Un_cierre_deja_de_ser_deshacible_cuando_se_empieza_otra_manga()
    {
        await _e.VacaMarcadaAsync("1");
        await _e.FinalizarAsync();
        await _e.VacaMarcadaAsync("2");

        var cierre = (await _e.Campo.ListarMovimientosAsync(_e.RodeoId)).Single(m => m.Tipo == TipoMovimiento.Cierre);

        Assert.False(cierre.PuedeDeshacer);
    }

    [Fact]
    public async Task Los_movimientos_son_solo_los_del_rodeo_pedido()
    {
        var otro = _e.Base.Rodeo(_e.EstablecimientoId, hash: new string('e', 64));
        _e.AnimalImportado("1", rodeoId: otro);
        await _e.LeerAsync("1", rodeoId: otro);
        await _e.VacaMarcadaAsync("2");

        var mios = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);

        Assert.Single(mios);
        Assert.Equal("2", mios[0].Caravana);
    }

    // ---------- Veterinarios (RF-1) ----------

    [Fact]
    public async Task Los_veterinarios_activos_no_incluyen_a_los_de_baja_ni_a_sin_asignar()
    {
        _e.Base.Usuario("Sin asignar", id: Usuario.SinAsignarId);
        _e.Base.Usuario("De baja", activo: false);
        _e.Base.Usuario("Ana", matricula: "MP-1");

        var lista = await _e.Campo.ListarVeterinariosActivosAsync();

        Assert.Equal(["Ana", "Vet Uno"], lista.Select(v => v.Nombre));
        Assert.Equal("MP-1", lista[0].Matricula);
    }

    [Fact]
    public async Task Crear_un_veterinario_lo_deja_en_la_lista_y_un_nombre_repetido_se_rechaza()
    {
        var r = await _e.Campo.CrearVeterinarioAsync("  Dra. Paz ", "123");
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Contains(await _e.Campo.ListarVeterinariosActivosAsync(), v => v.Id == r.Valor && v.Nombre == "Dra. Paz");

        var repetido = await _e.Campo.CrearVeterinarioAsync("dra. paz", null);
        Assert.Equal(CodigoError.Duplicado, repetido.Codigo);
        _e.Base.Usuario("Sin asignar", id: Usuario.SinAsignarId);
        var sinAsignar = await _e.Campo.CrearVeterinarioAsync("SIN ASIGNAR", null);
        Assert.False(sinAsignar.EsCorrecto);
    }

    [Fact]
    public async Task Crear_un_estado_desde_el_campo_lo_deja_activo_y_uno_repetido_o_sin_letras_se_rechaza()
    {
        var r = await _e.Campo.CrearEstadoAsync("  Revisión de pata ", "REV_PATA");
        Assert.True(r.EsCorrecto, r.Mensaje);
        var nuevo = Assert.Single((await _e.Campo.ListarEventosAsync()).Otros, e => e.Id == r.Valor);
        Assert.Equal("Revisión de pata", nuevo.Nombre);
        Assert.Equal("REVISION_DE_PATA", _e.Base.Escalar("SELECT Codigo FROM EstadoReproductivo WHERE Nombre = 'Revisión de pata'")!.ToString());
        Assert.Equal("REV_PATA", _e.Base.Escalar("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Nombre = 'Revisión de pata'")!.ToString());

        var repetido = await _e.Campo.CrearEstadoAsync("revisión de pata", "OTRO_CODIGO");
        Assert.Equal(CodigoError.Duplicado, repetido.Codigo);

        var sinLetras = await _e.Campo.CrearEstadoAsync("¿?!", "X");
        Assert.Equal(CodigoError.DatosInvalidos, sinLetras.Codigo);
    }

    // ---------- Caravana de muestra (simulador) ----------

    [Fact]
    public async Task La_caravana_de_muestra_existente_es_una_vaca_pendiente_y_la_ya_leida_una_marcada_hoy()
    {
        _e.AnimalImportado("A");
        _e.AnimalImportado("B");
        await _e.LeerAsync("A");

        Assert.Equal("B", await _e.Campo.CaravanaDeMuestraAsync(_e.RodeoId, MuestraCaravana.Existente));
        Assert.Equal("A", await _e.Campo.CaravanaDeMuestraAsync(_e.RodeoId, MuestraCaravana.YaLeida));
    }

    [Fact]
    public async Task Sin_vacas_no_hay_caravana_de_muestra()
    {
        Assert.Null(await _e.Campo.CaravanaDeMuestraAsync(_e.RodeoId, MuestraCaravana.Existente));
        Assert.Null(await _e.Campo.CaravanaDeMuestraAsync(_e.RodeoId, MuestraCaravana.YaLeida));
    }
}
