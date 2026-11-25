using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aviss.Application.Puertos;

namespace Aviss.Rfid;

/// <summary>
/// El lector real del bastón Tru-Test XRS2 (spec 008). No sabe nada de Android: habla con <see cref="IBluetoothBaston"/> e
/// <see cref="IEnlaceSerieBaston"/>. Un supervisor en segundo plano (un solo bucle) conecta, lee, arma líneas, filtra
/// repetidas y reintenta solo cuando se pierde el bastón; <see cref="ConectarAsync"/> vuelve enseguida.
/// </summary>
/// <remarks>
/// Estados que ve la pantalla: <c>Conectando</c> = "Buscando el bastón"; <c>Desconectado</c> + motivo = "Sin bastón".
/// Mientras el bastón sigue sin responder, el estado visible <b>no cambia</b> entre intentos (no parpadea): queda en
/// Desconectado/NoResponde hasta que conecta. Ningún mensaje lleva EID ni el nombre o la dirección del bastón (RF-26).
/// </remarks>
public sealed class LectorBastonXrs2 : IRfidReader
{
    private readonly IBluetoothBaston _bluetooth;
    private readonly IEnlaceSerieBaston _enlace;
    private readonly IPreferenciasBaston _preferencias;
    private readonly ITemporizador _temporizador;
    private readonly IAvisosCampo _avisos;
    private readonly Func<DateTime> _ahoraUtc;
    private readonly OpcionesLectorBaston _opciones;

    // Canal sin límite: las lecturas esperan a quien escuche (como el buffer del bastón). Varios lectores posibles: el
    // vaciado de DesconectarAsync también lee.
    private readonly Channel<TagRead> _canal = Channel.CreateUnbounded<TagRead>();

    // Todo lo que sigue se toca desde varios hilos (supervisor, pantalla, simulador): se protege con _candado.
    private readonly object _candado = new();
    private readonly PoliticaReintentos _politica;
    private readonly FiltroRepetidas _filtro;
    private EstadoConexionRfid _estado = EstadoConexionRfid.Desconectado;
    private MotivoSinBaston _motivo = MotivoSinBaston.Ninguno;
    private CancellationTokenSource? _cts;
    private Task? _tarea;
    private CancellationTokenSource? _corteEspera;   // cancelar esto corta la espera entre ciclos
    private bool _reconectarPendiente;               // "reconectar ya" tocado mientras no había espera en curso
    private bool _reconectoManual;                   // el próximo ciclo se muestra como "Buscando" aunque estuviera "Sin bastón"
    private MetodoSocket _metodoQueAndo = MetodoSocket.Inseguro;   // en memoria: se prueba primero (decisión del plan)
    private bool _pidioPermiso;                      // el diálogo del permiso se abre una vez por arranque, no en cada reintento
    private MotivoSinBaston _motivoDelCiclo;         // motivo que informó el enlace en el ciclo en curso

    public LectorBastonXrs2(
        IBluetoothBaston bluetooth,
        IEnlaceSerieBaston enlace,
        IPreferenciasBaston preferencias,
        ITemporizador temporizador,
        IAvisosCampo avisos,
        Func<DateTime>? ahoraUtc = null,
        OpcionesLectorBaston? opciones = null)
    {
        _bluetooth = bluetooth;
        _enlace = enlace;
        _preferencias = preferencias;
        _temporizador = temporizador;
        _avisos = avisos;
        _ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        _opciones = opciones ?? new OpcionesLectorBaston();
        _politica = new PoliticaReintentos(_opciones);
        _filtro = new FiltroRepetidas(_opciones.VentanaRepetidas, _ahoraUtc);
    }

    public EstadoConexionRfid Estado { get { lock (_candado) return _estado; } }
    public MotivoSinBaston Motivo { get { lock (_candado) return _motivo; } }
    public event Action? EstadoCambiado;

    // ---------------------------------------------------------------------------------------------------------------
    // API pública
    // ---------------------------------------------------------------------------------------------------------------

    public Task ConectarAsync(CancellationToken ct = default)
    {
        lock (_candado)
        {
            if (_tarea is { IsCompleted: false }) return Task.CompletedTask;   // idempotente
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _pidioPermiso = false;
            var token = _cts.Token;
            _tarea = Task.Run(() => SupervisarAsync(token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        Task? tarea;
        CancellationTokenSource? cts;
        lock (_candado)
        {
            cts = _cts;
            tarea = _tarea;
            _cts = null;
            _tarea = null;
            _reconectarPendiente = false;
            _reconectoManual = false;
        }

        cts?.Cancel();   // corta el intento en curso o la lectura (el enlace cierra el socket) y la espera
        if (tarea is not null)
        {
            try { await tarea.ConfigureAwait(false); }
            catch { /* el supervisor no deja pasar nada; por si acaso, desconectar nunca falla */ }
        }
        cts?.Dispose();

        while (_canal.Reader.TryRead(out _)) { }   // lo que quedó sin leer no sirve: se empieza de cero
        lock (_candado) _politica.Reiniciar();
        Fijar(EstadoConexionRfid.Desconectado, MotivoSinBaston.Ninguno);
    }

    public Task ReconectarAhoraAsync(CancellationToken ct = default)
    {
        bool hayQueArrancar;
        lock (_candado)
        {
            hayQueArrancar = _tarea is not { IsCompleted: false };
            if (!hayQueArrancar)
            {
                _politica.Reiniciar();
                _reconectoManual = true;
                if (_corteEspera is not null) _corteEspera.Cancel();
                else if (_estado != EstadoConexionRfid.Conectado) _reconectarPendiente = true;
            }
        }
        return hayQueArrancar ? ConectarAsync(ct) : Task.CompletedTask;
    }

    public async IAsyncEnumerable<TagRead> Lecturas([EnumeratorCancellation] CancellationToken ct)
    {
        var lector = _canal.Reader;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await lector.WaitToReadAsync(ct).ConfigureAwait(false)) yield break;
            }
            catch (OperationCanceledException)
            {
                yield break;   // cortar la escucha no es un error
            }

            while (lector.TryRead(out var lectura))
                yield return lectura;
        }
    }

    /// <summary>
    /// El simulador de DEBUG "lee" un código por el mismo camino que el bastón (mismo filtro de repetidas y misma cola).
    /// No pasa por el intérprete porque el simulador también escribe números de caravana cortos.
    /// </summary>
    public void Inyectar(string codigo) => Entregar(codigo);

    // ---------------------------------------------------------------------------------------------------------------
    // Supervisor
    // ---------------------------------------------------------------------------------------------------------------

    private async Task SupervisarAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await UnaVueltaAsync(ct).ConfigureAwait(false)) return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // Algo inesperado (no debería pasar): se trata como un ciclo fallido y se sigue intentando.
                    Fijar(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoResponde);
                    await EsperarCicloAsync(SiguienteEspera(), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { /* se pidió cortar */ }
    }

    /// <returns>Falso si el supervisor debe terminar (no hay nada que reintentar solo).</returns>
    private async Task<bool> UnaVueltaAsync(CancellationToken ct)
    {
        // 1. Lo que no depende de si el bastón responde. Sin bastón elegido no se pide ni un permiso (RF-2, RF-5).
        var baston = _preferencias.Obtener();
        if (baston is null)
        {
            Fijar(EstadoConexionRfid.Desconectado, MotivoSinBaston.NoElegido);
            return false;
        }
        if (!_bluetooth.HayBluetooth())
        {
            Fijar(EstadoConexionRfid.Desconectado, MotivoSinBaston.SinBluetooth);
            return false;
        }

        if (!_bluetooth.TienePermisos() && !_pidioPermiso)
        {
            _pidioPermiso = true;
            Fijar(EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno);
            await _bluetooth.PedirPermisosAsync().ConfigureAwait(false);
        }
        if (!_bluetooth.TienePermisos())
            return await EsperarSinConectarAsync(MotivoSinBaston.SinPermiso, ct).ConfigureAwait(false);   // se re-chequea cada vuelta
        if (!_bluetooth.EstaEncendido())
            return await EsperarSinConectarAsync(MotivoSinBaston.BluetoothApagado, ct).ConfigureAwait(false);
        if (!_bluetooth.EstaEmparejado(baston.Direccion))
            return await EsperarSinConectarAsync(MotivoSinBaston.NoEmparejado, ct).ConfigureAwait(false);

        // 2. Un ciclo: el método que anduvo y después el otro. "Buscando" solo si no se estaba mostrando "Sin bastón".
        bool manual;
        lock (_candado)
        {
            manual = _reconectoManual;
            _reconectoManual = false;
        }
        var yaSinBaston = Estado == EstadoConexionRfid.Desconectado && Motivo == MotivoSinBaston.NoResponde;
        if (!yaSinBaston || manual) Fijar(EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno);

        var conexion = await CicloAsync(baston.Direccion, ct).ConfigureAwait(false);
        if (conexion is null)
        {
            // 4. Ciclo sin éxito: queda "Sin bastón" (una sola vez por episodio) y se espera más cada vez.
            var motivo = _motivoDelCiclo != MotivoSinBaston.Ninguno ? _motivoDelCiclo : MotivoSinBaston.NoResponde;
            Fijar(EstadoConexionRfid.Desconectado, motivo);
            await EsperarCicloAsync(SiguienteEspera(), ct).ConfigureAwait(false);
            return true;
        }

        // 3. Conectado: se lee hasta que se pierda.
        lock (_candado) _politica.Reiniciar();
        Fijar(EstadoConexionRfid.Conectado, MotivoSinBaston.Ninguno);
        await LeerHastaPerderAsync(conexion, ct).ConfigureAwait(false);

        // Se perdió estando conectado (la cancelación habría salido por excepción): aviso y vuelta a buscar.
        _avisos.BastonPerdido();
        Fijar(EstadoConexionRfid.Conectando, MotivoSinBaston.Ninguno);
        await EsperarCicloAsync(_opciones.EsperaTrasPerdida, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> EsperarSinConectarAsync(MotivoSinBaston motivo, CancellationToken ct)
    {
        Fijar(EstadoConexionRfid.Desconectado, motivo);
        await EsperarCicloAsync(SiguienteEspera(), ct).ConfigureAwait(false);
        return true;
    }

    private TimeSpan SiguienteEspera()
    {
        lock (_candado) return _politica.SiguienteEspera();
    }

    private async Task<IConexionSerie?> CicloAsync(string direccion, CancellationToken ct)
    {
        _motivoDelCiclo = MotivoSinBaston.Ninguno;
        var primero = _metodoQueAndo;
        var segundo = primero == MetodoSocket.Inseguro ? MetodoSocket.Seguro : MetodoSocket.Inseguro;

        foreach (var metodo in new[] { primero, segundo })
        {
            var conexion = await IntentarAsync(direccion, metodo, ct).ConfigureAwait(false);
            if (conexion is null) continue;
            _metodoQueAndo = metodo;
            return conexion;
        }
        return null;
    }

    /// <summary>Un intento con tiempo máximo (RF-8). Al vencer se cancela el token, que hace que el enlace cierre el socket.</summary>
    private async Task<IConexionSerie?> IntentarAsync(string direccion, MetodoSocket metodo, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var abrir = AbrirAsync(direccion, metodo, cts.Token);
        var reloj = _temporizador.EsperarAsync(_opciones.TimeoutIntento, cts.Token);

        var primero = await Task.WhenAny(abrir, reloj).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            cts.Cancel();
            _ = CerrarSiAbrioAsync(abrir);
            throw new OperationCanceledException(ct);
        }

        if (primero == abrir)
        {
            cts.Cancel();   // ya no hace falta el reloj
            try { return await abrir.ConfigureAwait(false); }
            catch (ErrorEnlace e) { _motivoDelCiclo = e.Motivo; return null; }
            catch (Exception) { return null; }
        }

        // Venció el tiempo: se corta el intento. Si el enlace igual llegó a abrir algo, se cierra para no dejarlo colgado.
        cts.Cancel();
        _ = CerrarSiAbrioAsync(abrir);
        return null;
    }

    // Envuelve la llamada para que una excepción síncrona del enlace también quede dentro de la tarea.
    private async Task<IConexionSerie> AbrirAsync(string direccion, MetodoSocket metodo, CancellationToken ct) =>
        await _enlace.AbrirAsync(direccion, metodo, ct).ConfigureAwait(false);

    private static async Task CerrarSiAbrioAsync(Task<IConexionSerie> abrir)
    {
        try
        {
            var conexion = await abrir.ConfigureAwait(false);
            await conexion.CerrarAsync().ConfigureAwait(false);
        }
        catch { /* falló o se canceló: no hay nada que cerrar */ }
    }

    /// <summary>Lee hasta que el bastón corta o falla la lectura. Si se cancela el token, lanza (no es una pérdida).</summary>
    private async Task LeerHastaPerderAsync(IConexionSerie conexion, CancellationToken ct)
    {
        // Un acumulador nuevo por conexión: una línea a medias de la conexión anterior se descarta (RF-22).
        var acumulador = new AcumuladorLineas();
        var buffer = new byte[512];

        // Aunque el enlace no respete el token, cancelar cierra el socket y la lectura se destraba.
        using var cierre = ct.Register(static c => _ = Cerrar((IConexionSerie)c!), conexion);
        try
        {
            while (true)
            {
                int n;
                try { n = await conexion.LeerAsync(buffer, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { break; }

                if (n <= 0) break;
                foreach (var linea in acumulador.Agregar(buffer.AsSpan(0, n)))
                {
                    var eid = InterpreteLineaBaston.Interpretar(linea);
                    if (eid is not null) Entregar(eid);   // la basura se ignora en silencio (RF-20)
                }
            }
            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            await Cerrar(conexion).ConfigureAwait(false);
        }
    }

    private static async Task Cerrar(IConexionSerie conexion)
    {
        try { await conexion.CerrarAsync().ConfigureAwait(false); }
        catch { /* cerrar es idempotente y no debe fallar */ }
    }

    private void Entregar(string codigo)
    {
        bool pasa;
        lock (_candado) pasa = _filtro.Aceptar(codigo);
        if (pasa) _canal.Writer.TryWrite(new TagRead(codigo, _ahoraUtc()));
    }

    /// <summary>Espera entre ciclos; "reconectar ya" la corta. Una cancelación del supervisor sí se propaga.</summary>
    private async Task EsperarCicloAsync(TimeSpan tiempo, CancellationToken ct)
    {
        CancellationTokenSource corte;
        lock (_candado)
        {
            if (_reconectarPendiente)
            {
                _reconectarPendiente = false;
                return;
            }
            corte = _corteEspera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        try
        {
            await _temporizador.EsperarAsync(tiempo, corte.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // La cortó "reconectar ya": se sigue con el ciclo siguiente.
        }
        finally
        {
            lock (_candado)
            {
                _corteEspera = null;
                corte.Dispose();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Cambia el estado y avisa solo si algo cambió (así la pantalla no parpadea).</summary>
    private void Fijar(EstadoConexionRfid estado, MotivoSinBaston motivo)
    {
        lock (_candado)
        {
            if (_estado == estado && _motivo == motivo) return;
            _estado = estado;
            _motivo = motivo;
        }
        EstadoCambiado?.Invoke();
    }
}
