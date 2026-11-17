namespace Aviss.App.ViewModels;

/// <summary>Una lectura pendiente: el código y si vino de la búsqueda manual (no del bastón).</summary>
public readonly record struct Lectura(string Codigo, bool EsManual);

/// <summary>
/// Cola de lecturas de la pantalla de trabajo (spec 003, RF-19). Hace dos cosas:
/// <list type="bullet">
/// <item>Procesa <b>una lectura a la vez</b>, en el orden en que llegaron: dos transacciones en el mismo archivo de SQLite
/// no deben correr en paralelo, y dos vacas leídas muy seguidas quedan marcadas las dos.</item>
/// <item>Mientras hay un <b>cuadro abierto</b> que pide datos (alta, cantidad real, confirmaciones), no procesa nada y
/// conserva <b>solo la última lectura</b>; la procesa cuando el cuadro se cierra.</item>
/// </list>
/// No usa MAUI ni hilos propios: el procesamiento corre donde lo llamen. Es seguro llamarla desde varios hilos.
/// </summary>
public sealed class ColaDeLecturas
{
    private readonly Func<Lectura, Task> _procesar;
    private readonly object _candado = new();
    private readonly Queue<Lectura> _cola = new();
    private bool _bloqueada;
    private bool _drenando;

    /// <param name="procesar">Lo que se hace con cada lectura. Debe atrapar sus propios errores (ver <see cref="Drenar"/>).</param>
    public ColaDeLecturas(Func<Lectura, Task> procesar) => _procesar = procesar;

    /// <summary>True mientras hay un cuadro abierto: no se procesa nada.</summary>
    public bool Bloqueada { get { lock (_candado) return _bloqueada; } }

    /// <summary>Hay lecturas esperando.</summary>
    public bool HayPendiente { get { lock (_candado) return _cola.Count > 0; } }

    /// <summary>Se abrió un cuadro que pide datos o confirmación: de acá en más solo importa la última lectura.</summary>
    public void Bloquear()
    {
        lock (_candado)
        {
            _bloqueada = true;
            ConservarSoloLaUltima();
        }
    }

    /// <summary>
    /// Se cerró el cuadro. Si llegó una lectura mientras estaba abierto, se procesa ahora. El resultado termina cuando
    /// se vació la cola (o de inmediato si otro procesamiento ya la estaba vaciando).
    /// </summary>
    public Task DesbloquearAsync()
    {
        lock (_candado)
        {
            _bloqueada = false;
            if (_drenando || _cola.Count == 0) return Task.CompletedTask;
            _drenando = true;
        }
        return Drenar();
    }

    /// <summary>
    /// Agrega una lectura. Si no hay nada en curso ni cuadro abierto, la procesa y la tarea devuelta termina cuando se
    /// vació la cola; si no, solo la deja esperando y la tarea termina enseguida.
    /// </summary>
    public Task EncolarAsync(string codigo, bool esManual = false)
    {
        lock (_candado)
        {
            if (_bloqueada) _cola.Clear();   // con un cuadro abierto, la nueva reemplaza a la que estaba
            _cola.Enqueue(new Lectura(codigo, esManual));
            if (_bloqueada || _drenando) return Task.CompletedTask;
            _drenando = true;
        }
        return Drenar();
    }

    private void ConservarSoloLaUltima()
    {
        if (_cola.Count <= 1) return;
        Lectura ultima = default;
        while (_cola.Count > 0) ultima = _cola.Dequeue();
        _cola.Enqueue(ultima);
    }

    // Procesa una por una hasta vaciar la cola o hasta que un cuadro se abra. El estado "drenando" se pisa siempre al
    // salir para que la cola no quede trabada.
    private async Task Drenar()
    {
        while (true)
        {
            Lectura siguiente;
            lock (_candado)
            {
                if (_bloqueada || _cola.Count == 0) { _drenando = false; return; }
                siguiente = _cola.Dequeue();
            }

            try { await _procesar(siguiente); }
            catch (Exception)
            {
                // El procesador (el ViewModel) muestra sus propios errores. Si algo se escapa, se descarta esa lectura
                // y se sigue con la próxima: una lectura fallida no debe dejar la pantalla sin escuchar.
            }
        }
    }
}
