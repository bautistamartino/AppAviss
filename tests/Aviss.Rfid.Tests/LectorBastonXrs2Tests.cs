using System.Collections.Concurrent;
using Aviss.Application.Puertos;
using Aviss.Rfid.Diagnostico;
using Aviss.Rfid.Tests.Soporte;

namespace Aviss.Rfid.Tests;

/// <summary>
/// Spec 008, T2: el lector del bastón con fakes. Nada espera de verdad: el temporizador y el reloj los mueve el test.
/// Todos los EID son inventados (prefijo 999).
/// </summary>
public sealed class LectorBastonXrs2Tests : IAsyncDisposable
{
    private const string EidA = "999000000000001";
    private const string EidB = "999000000000002";
    private const string EidC = "999000000000003";
    private const string Direccion = "00:11:22:33:44:55";

    private static readonly TimeSpan S1 = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan S2 = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan S4 = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan S8 = TimeSpan.FromSeconds(8);

    private readonly FakeBluetoothBaston _bt = new();
    private readonly FakeEnlaceSerie _enlace = new();
    private readonly FakePreferenciasBaston _prefs = new() { Actual = new BastonRecordado("XRS2 de prueba", Direccion) };
    private readonly FakeTemporizador _temp = new();
    private readonly FakeAvisos _avisos = new();
    private readonly RelojManual _reloj = new(new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc));
    private readonly LectorBastonXrs2 _lector;
    private readonly ConcurrentQueue<string> _recibidas = new();
    private readonly List<(EstadoConexionRfid Estado, MotivoSinBaston Motivo)> _estados = [];
    private readonly CancellationTokenSource _escucha = new();
    private Task? _tareaEscucha;

    public LectorBastonXrs2Tests()
    {
        _lector = new LectorBastonXrs2(_bt, _enlace, _prefs, _temp, _avisos, _reloj.Ahora);
        _lector.EstadoCambiado += () =>
        {
            lock (_estados) _estados.Add((_lector.Estado, _lector.Motivo));
        };
    }

    public async ValueTask DisposeAsync()
    {
        _escucha.Cancel();
        if (_tareaEscucha is not null) await _tareaEscucha;
        await _lector.DesconectarAsync();
    }

    // ---- ayudas ----

    private void Escuchar() =>
        _tareaEscucha ??= Task.Run(async () =>
        {
            await foreach (var tag in _lector.Lecturas(_escucha.Token)) _recibidas.Enqueue(tag.Codigo);
        });

    private Task EsperarEstado(EstadoConexionRfid estado, MotivoSinBaston motivo = MotivoSinBaston.Ninguno) =>
        Esperas.HastaAsync(() => _lector.Estado == estado && _lector.Motivo == motivo);

    private Task EsperarConexiones(int cantidad) => Esperas.HastaAsync(() => _enlace.Conexiones.Count >= cantidad);

    private Task EsperarRecibidas(int cantidad) => Esperas.HastaAsync(() => _recibidas.Count >= cantidad);

    private Task EsperarPendiente(TimeSpan t) => Esperas.HastaAsync(() => _temp.Pendientes(t) > 0);

    private async Task LiberarCuandoHaya(TimeSpan t)
    {
        await EsperarPendiente(t);
        Assert.True(_temp.Liberar(t));
    }

    private async Task<FakeConexionSerie> ConectarYEsperarAsync()
    {
        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Conectado);
        return _enlace.Conexiones[^1];
    }

    private IReadOnlyList<(EstadoConexionRfid Estado, MotivoSinBaston Motivo)> Estados()
    {
        lock (_estados) return [.. _estados];
    }

    /// <summary>La trama grabada del bastón, con EID inventado: "999 000458467499" + CRLF.</summary>
    private static readonly byte[] TramaCompleta =
        [0x39, 0x39, 0x39, 0x20, 0x30, 0x30, 0x30, 0x34, 0x35, 0x38, 0x34, 0x36, 0x37, 0x34, 0x39, 0x39, 0x0D, 0x0A];

    // ---- RF-7: conectar vuelve enseguida; RF-2: no elige solo ----

    [Fact]
    public async Task ConectarAsync_vuelve_enseguida_aunque_el_baston_no_responda()
    {
        _enlace.PonerModo(ModoEnlace.NoResponde);

        await _lector.ConectarAsync().WaitAsync(TimeSpan.FromSeconds(2));

        await EsperarEstado(EstadoConexionRfid.Conectando);
    }

    [Fact]
    public async Task ConectarAsync_es_idempotente_no_lanza_dos_supervisores()
    {
        _enlace.PonerModo(ModoEnlace.NoResponde);

        await _lector.ConectarAsync();
        await _lector.ConectarAsync();
        await EsperarPendiente(S8);
        await Task.Delay(30);

        Assert.Single(_enlace.Llamadas);
    }

    [Fact]
    public async Task Sin_baston_recordado_no_elige_solo_aunque_haya_un_XRS_emparejado()
    {
        _prefs.Actual = null;
        _bt.Dispositivos.Add(new DispositivoBluetooth("XRS2 999", Direccion, TipoDispositivoBluetooth.Clasico, []));

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoElegido);

        Assert.Empty(_enlace.Llamadas);
        Assert.Equal(0, _bt.PedidosDePermiso);
        Assert.Equal(0, _temp.PendientesTotal);   // y no reintenta: no hay nada que reintentar hasta que se elija
    }

    // ---- RF-4, RF-5, RF-6: motivos previos a conectar ----

    [Fact]
    public async Task Baston_desemparejado_queda_NoEmparejado_sin_abrir_sockets()
    {
        _bt.Emparejado = false;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoEmparejado);

        Assert.Empty(_enlace.Llamadas);
    }

    [Fact]
    public async Task Permiso_denegado_queda_SinPermiso_sin_abrir_sockets_y_al_concederse_conecta_en_el_reintento()
    {
        _bt.Permisos = false;
        _bt.ConcederAlPedir = false;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinPermiso);
        Assert.Empty(_enlace.Llamadas);
        Assert.Equal(1, _bt.PedidosDePermiso);

        _bt.Permisos = true;   // el usuario lo concedió desde los ajustes
        await LiberarCuandoHaya(S1);
        await EsperarEstado(EstadoConexionRfid.Conectado);

        Assert.Equal(1, _bt.PedidosDePermiso);   // el diálogo no se vuelve a abrir solo en cada reintento
    }

    [Fact]
    public async Task Pide_el_permiso_solo_cuando_hay_baston_elegido()
    {
        _prefs.Actual = null;
        _bt.Permisos = false;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoElegido);

        Assert.Equal(0, _bt.PedidosDePermiso);
    }

    [Fact]
    public async Task Bluetooth_apagado_queda_BluetoothApagado_y_al_prenderse_conecta_en_el_reintento()
    {
        _bt.Encendido = false;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.BluetoothApagado);
        Assert.Empty(_enlace.Llamadas);

        _bt.Encendido = true;
        await LiberarCuandoHaya(S1);
        await EsperarEstado(EstadoConexionRfid.Conectado);
    }

    [Fact]
    public async Task Sin_Bluetooth_queda_SinBluetooth_y_no_reintenta()
    {
        _bt.Hay = false;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinBluetooth);

        Assert.Empty(_enlace.Llamadas);
        Assert.Equal(0, _temp.PendientesTotal);
    }

    // ---- RF-8, RF-9: método de socket y timeout ----

    [Fact]
    public async Task Prueba_primero_el_socket_inseguro_y_al_conectar_el_estado_es_Conectado()
    {
        await ConectarYEsperarAsync();

        Assert.Equal([MetodoSocket.Inseguro], _enlace.Llamadas);
    }

    [Fact]
    public async Task Si_el_inseguro_falla_prueba_el_seguro_en_el_mismo_ciclo()
    {
        _enlace.ModoInseguro = ModoEnlace.Falla;

        await ConectarYEsperarAsync();

        Assert.Equal([MetodoSocket.Inseguro, MetodoSocket.Seguro], _enlace.Llamadas);
    }

    [Fact]
    public async Task El_metodo_que_anduvo_se_prueba_primero_la_vez_siguiente()
    {
        _enlace.ModoInseguro = ModoEnlace.Falla;
        var primera = await ConectarYEsperarAsync();

        primera.Cortar();                              // se pierde el bastón...
        await LiberarCuandoHaya(S1);                   // ...y tras la espera de 1 s reconecta
        await EsperarConexiones(2);

        // La segunda vuelta arranca directamente por el seguro (el que anduvo), sin pasar por el inseguro.
        Assert.Equal([MetodoSocket.Inseguro, MetodoSocket.Seguro, MetodoSocket.Seguro], _enlace.Llamadas);
    }

    [Fact]
    public async Task Un_intento_que_no_responde_se_abandona_a_los_8_segundos_y_se_cancela_el_enlace()
    {
        _enlace.PonerModo(ModoEnlace.NoResponde);

        await _lector.ConectarAsync();
        await LiberarCuandoHaya(S8);   // vence el timeout del primer método

        await Esperas.HastaAsync(() => _enlace.Cancelados == 1);
        await Esperas.HastaAsync(() => _enlace.Llamadas.Count == 2);   // pasa al otro método sin esperar
        await LiberarCuandoHaya(S8);

        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
        Assert.Equal(2, _enlace.Cancelados);
    }

    [Fact]
    public async Task Si_el_enlace_abre_justo_despues_del_timeout_esa_conexion_se_cierra()
    {
        // Un enlace que contesta tarde no deja un socket abierto colgado.
        var tarde = new TaskCompletionSource<IConexionSerie>();
        var conexionTarde = new FakeConexionSerie();
        var lector = new LectorBastonXrs2(_bt, new EnlaceTardio(tarde.Task), _prefs, _temp, _avisos, _reloj.Ahora);

        await lector.ConectarAsync();
        await LiberarCuandoHaya(S8);
        tarde.SetResult(conexionTarde);

        await Esperas.HastaAsync(() => conexionTarde.Cerrada);
        await lector.DesconectarAsync();
    }

    private sealed class EnlaceTardio(Task<IConexionSerie> tarea) : IEnlaceSerieBaston
    {
        public Task<IConexionSerie> AbrirAsync(string direccion, MetodoSocket metodo, CancellationToken ct) => tarea;
    }

    // ---- RF-10, RF-11, RF-13: reintentos y estados sin parpadeo ----

    [Fact]
    public async Task Sin_conexion_reintenta_solo_con_esperas_crecientes_y_conecta_cuando_el_baston_se_prende()
    {
        _enlace.PonerModo(ModoEnlace.Falla);

        await _lector.ConectarAsync();

        // Pasan tres ciclos fallidos; el bastón sigue apagado.
        await LiberarCuandoHaya(S1);
        await LiberarCuandoHaya(S2);
        await EsperarPendiente(S4);

        _enlace.PonerModo(ModoEnlace.Conecta);   // el veterinario prende el bastón: nadie llama a nada
        await LiberarCuandoHaya(S4);
        await EsperarEstado(EstadoConexionRfid.Conectado);

        // Se miran solo las esperas entre ciclos (los 8 s son el timeout de cada intento).
        Assert.Equal([S1, S2, S4], _temp.Pedidas.Where(t => t != S8).Take(3));
    }

    [Fact]
    public async Task Secuencia_de_estados_sin_parpadeo_mientras_el_baston_sigue_apagado()
    {
        _enlace.PonerModo(ModoEnlace.Falla);

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
        await LiberarCuandoHaya(S1);
        await LiberarCuandoHaya(S2);
        await EsperarPendiente(S4);   // ya fallaron tres ciclos
        _enlace.PonerModo(ModoEnlace.Conecta);
        await LiberarCuandoHaya(S4);
        await EsperarEstado(EstadoConexionRfid.Conectado);

        Assert.Equal(
        [
            (EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno),
            (EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde),   // una sola vez, no parpadea con cada intento
            (EstadoConexionRfid.Conectado, MotivoSinBaston.Ninguno),
        ], Estados());
    }

    [Fact]
    public async Task Al_conectar_la_politica_vuelve_a_1_segundo()
    {
        _enlace.PonerModo(ModoEnlace.Falla);
        await _lector.ConectarAsync();
        await LiberarCuandoHaya(S1);
        await LiberarCuandoHaya(S2);
        await EsperarPendiente(S4);    // la política ya iba por 4 s
        _enlace.PonerModo(ModoEnlace.Conecta);
        await LiberarCuandoHaya(S4);
        await EsperarEstado(EstadoConexionRfid.Conectado);

        _enlace.Conexiones[^1].Cortar();
        await Esperas.HastaAsync(() => _avisos.BastonesPerdidos == 1);
        _enlace.PonerModo(ModoEnlace.Falla);
        await LiberarCuandoHaya(S1);   // espera tras la pérdida; después falla el ciclo
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);

        // La primera espera del nuevo episodio es de 1 s, no de 8: la política se reinició al conectar.
        await EsperarPendiente(S1);
    }

    [Fact]
    public async Task ReconectarAhora_corta_la_espera_y_muestra_Conectando()
    {
        _enlace.PonerModo(ModoEnlace.Falla);
        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
        await LiberarCuandoHaya(S1);
        await EsperarPendiente(S2);   // ahora está esperando 2 s
        var llamadas = _enlace.Llamadas.Count;

        _enlace.PonerModo(ModoEnlace.Conecta);
        await _lector.ReconectarAhoraAsync();   // sin liberar la espera de 2 s

        await EsperarEstado(EstadoConexionRfid.Conectado);
        Assert.True(_enlace.Llamadas.Count > llamadas);
        Assert.Contains((EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno), Estados().Skip(2));
    }

    [Fact]
    public async Task ReconectarAhora_sin_supervisor_activo_arranca_de_cero()
    {
        _prefs.Actual = null;
        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoElegido);

        _prefs.Actual = new BastonRecordado("XRS2", Direccion);   // la persona eligió uno
        await _lector.ReconectarAhoraAsync();

        await EsperarEstado(EstadoConexionRfid.Conectado);
    }

    [Fact]
    public async Task Un_ErrorEnlace_con_motivo_se_muestra_con_ese_motivo_y_sin_datos_del_baston()
    {
        var error = new ErrorEnlace(MotivoSinBaston.NoEmparejado);
        _enlace.ErrorAlAbrir = error;

        await _lector.ConectarAsync();
        await EsperarEstado(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoEmparejado);

        // RF-26: el mensaje no lleva ni la dirección ni el nombre del bastón.
        Assert.DoesNotContain(Direccion, error.Message);
        Assert.DoesNotContain("XRS", error.Message);
    }

    // ---- RF-14, RF-22: pérdida y reconexión ----

    [Fact]
    public async Task Al_perder_el_baston_avisa_una_vez_pasa_a_Conectando_y_reconecta_sin_duplicar_ni_perder()
    {
        Escuchar();
        var uno = await ConectarYEsperarAsync();
        uno.Enviar("999000000000001\r\n");
        uno.Enviar("999 0004");   // una línea a medias cuando se corta: tiene que descartarse
        await EsperarRecibidas(1);

        uno.Cortar();
        await EsperarEstado(EstadoConexionRfid.Conectando);
        Assert.Equal(1, _avisos.BastonesPerdidos);

        await LiberarCuandoHaya(S1);
        await EsperarConexiones(2);
        await EsperarEstado(EstadoConexionRfid.Conectado);
        _enlace.Conexiones[1].Enviar("999000000000002\r\n");
        await EsperarRecibidas(2);

        Assert.Equal([EidA, EidB], _recibidas.ToArray());
        Assert.Equal(1, _avisos.BastonesPerdidos);
        Assert.True(uno.Cerrada);
    }

    // ---- RF-17 ----

    [Fact]
    public async Task Conectado_y_sin_lecturas_pasa_mucho_tiempo_y_no_se_desconecta_solo()
    {
        await ConectarYEsperarAsync();

        _reloj.Avanzar(TimeSpan.FromHours(2));
        await Task.Delay(50);   // un respiro real y corto: la ausencia de cambios no se puede esperar de otra forma

        Assert.Equal(EstadoConexionRfid.Conectado, _lector.Estado);
        Assert.Equal(0, _temp.PendientesTotal);
        Assert.Equal(0, _avisos.BastonesPerdidos);
    }

    // ---- RF-16: desconectar ----

    [Fact]
    public async Task Desconectar_corta_el_intento_en_curso_no_reintenta_y_volver_a_conectar_intenta_de_inmediato()
    {
        _enlace.PonerModo(ModoEnlace.NoResponde);
        await _lector.ConectarAsync();
        await EsperarPendiente(S8);

        await _lector.DesconectarAsync();

        Assert.Equal(EstadoConexionRfid.Desconectado, _lector.Estado);
        Assert.Equal(MotivoSinBaston.Ninguno, _lector.Motivo);
        Assert.Equal(1, _enlace.Cancelados);
        Assert.Equal(0, _temp.PendientesTotal);   // no queda ninguna espera ni reintento
        var llamadas = _enlace.Llamadas.Count;

        await _lector.ConectarAsync();
        await Esperas.HastaAsync(() => _enlace.Llamadas.Count > llamadas);   // sin esperar nada
    }

    [Fact]
    public async Task Desconectar_cierra_la_conexion_y_vacia_lo_pendiente()
    {
        var conexion = await ConectarYEsperarAsync();
        conexion.Enviar("999000000000001\r\n");
        await Task.Delay(50);   // le damos tiempo al lector de pasar la línea al canal (todavía nadie escucha)

        await _lector.DesconectarAsync();
        Assert.True(conexion.Cerrada);

        Escuchar();
        await Task.Delay(50);
        Assert.Empty(_recibidas);
    }

    // ---- RF-18, RF-19, RF-20: lectura ----

    [Fact]
    public async Task Trama_completa_entrega_el_EID_sin_espacios()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar(TramaCompleta);
        await EsperarRecibidas(1);

        Assert.Equal(["999000458467499"], _recibidas.ToArray());
    }

    [Fact]
    public async Task Trama_partida_en_dos_y_en_tres_paquetes_da_la_misma_lectura()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar(TramaCompleta[..7]);
        c.Enviar(TramaCompleta[7..]);
        await EsperarRecibidas(1);
        _reloj.Avanzar(TimeSpan.FromSeconds(10));   // para que el filtro de repetidas no la tape
        c.Enviar(TramaCompleta[..5]);
        c.Enviar(TramaCompleta[5..16]);
        c.Enviar(TramaCompleta[16..]);
        await EsperarRecibidas(2);

        Assert.Equal(["999000458467499", "999000458467499"], _recibidas.ToArray());
    }

    [Fact]
    public async Task CR_y_LF_en_paquetes_distintos_son_una_sola_linea()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar("999000000000001\r");
        c.Enviar("\n999000000000002\r");
        c.Enviar("\n");
        await EsperarRecibidas(2);
        await Task.Delay(30);

        Assert.Equal([EidA, EidB], _recibidas.ToArray());
    }

    [Fact]
    public async Task Varias_lineas_en_un_paquete_entregan_cada_una_en_orden()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar("999000000000001\r\n999000000000002\r\n999000000000003\r\n");
        await EsperarRecibidas(3);

        Assert.Equal([EidA, EidB, EidC], _recibidas.ToArray());
    }

    [Fact]
    public async Task La_basura_no_entrega_nada_y_la_lectura_siguiente_funciona()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar([0x00, 0xFF, 0x10, 0x0D, 0x0A]);                     // bytes sueltos
        c.Enviar("99900000000001\r\n");                               // 14 dígitos
        c.Enviar("9990000000000012\r\n");                             // 16 dígitos
        c.Enviar("ABC999000000001\r\n");                              // letras
        c.Enviar("999000000000002\r\n");
        await EsperarRecibidas(1);
        await Task.Delay(30);

        Assert.Equal([EidB], _recibidas.ToArray());
        Assert.Equal(EstadoConexionRfid.Conectado, _lector.Estado);
    }

    [Fact]
    public async Task Cinco_mil_bytes_sin_terminador_no_hacen_crecer_nada_y_se_sigue_leyendo()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar(Enumerable.Repeat((byte)'x', 5000).ToArray());
        c.Enviar("\r\n999000000000001\r\n");
        await EsperarRecibidas(1);

        Assert.Equal([EidA], _recibidas.ToArray());
    }

    // ---- RF-21: repetidas ----

    [Fact]
    public async Task Repetida_a_menos_de_3_segundos_se_ignora_y_a_3_o_mas_pasa()
    {
        Escuchar();
        var c = await ConectarYEsperarAsync();

        c.Enviar("999000000000001\r\n");
        await EsperarRecibidas(1);
        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        c.Enviar("999000000000001\r\n");                 // a 1 s: se ignora
        c.Enviar("999000000000002\r\n");                 // otro botón: pasa (y marca que la repetida ya se procesó)
        await EsperarRecibidas(2);
        _reloj.Avanzar(TimeSpan.FromSeconds(3));
        c.Enviar("999000000000002\r\n");                 // a 3 s del anterior: pasa
        await EsperarRecibidas(3);

        Assert.Equal([EidA, EidB, EidB], _recibidas.ToArray());
    }

    // ---- RF-25: simulador ----

    [Fact]
    public async Task Inyectar_pasa_por_el_mismo_filtro_y_la_misma_cola()
    {
        Escuchar();

        _lector.Inyectar("1234");
        _lector.Inyectar("1234");   // repetida inmediata: la ignora el filtro
        _lector.Inyectar("5678");
        await EsperarRecibidas(2);
        await Task.Delay(30);

        Assert.Equal(["1234", "5678"], _recibidas.ToArray());
    }
}
