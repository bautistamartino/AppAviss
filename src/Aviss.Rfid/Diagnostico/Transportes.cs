namespace Aviss.Rfid.Diagnostico;

public enum TipoDispositivoBluetooth { Desconocido, Clasico, Le, Dual }

/// <summary>Un dispositivo Bluetooth emparejado. Solo nombre, dirección y lo que anuncia: nada de datos del tambo.</summary>
public sealed record DispositivoBluetooth(string Nombre, string Direccion, TipoDispositivoBluetooth Tipo, IReadOnlyList<string> Uuids)
{
    public string Titulo => string.IsNullOrWhiteSpace(Nombre) ? "(sin nombre)" : Nombre;
    public string TipoTexto => Tipo switch
    {
        TipoDispositivoBluetooth.Clasico => "Clásico",
        TipoDispositivoBluetooth.Le => "BLE",
        TipoDispositivoBluetooth.Dual => "Dual (clásico + BLE)",
        _ => "Tipo desconocido",
    };
    public string UuidsTexto => Uuids.Count == 0 ? "sin UUIDs anunciados" : string.Join(", ", Uuids);
}

public enum EstadoTransporte { Desconectado, Conectando, Conectado, Error }

/// <summary>Cambio de estado de un transporte; en error, <c>Detalle</c> dice el motivo.</summary>
public sealed record CambioEstadoTransporte(EstadoTransporte Estado, string? Detalle = null);

public enum EstadoPermisosBluetooth { Concedidos, Denegados, BluetoothNoDisponible }

/// <summary>
/// Una forma de hablar con el bastón (SPP clásico o BLE/GATT), solo para diagnóstico. La implementan las clases de
/// <c>Platforms/Android</c>; el spike no es el lector final (<see cref="Aviss.Application.Puertos.IRfidReader"/> llega en la spec 008).
/// </summary>
public interface ITransporteDiagnostico
{
    string Nombre { get; }
    EstadoTransporte Estado { get; }

    /// <summary>Cambió el estado (conectando, conectado, error con motivo, desconectado). Puede venir de cualquier hilo.</summary>
    event Action<CambioEstadoTransporte>? EstadoCambiado;

    /// <summary>Llegaron bytes. El segundo dato es el origen (en BLE, la característica; en SPP, el nombre del transporte).</summary>
    event Action<string, byte[]>? DatosRecibidos;

    /// <summary>Información para el registro (intentos, servicios y características descubiertos...).</summary>
    event Action<string>? Informacion;

    Task ConectarAsync(DispositivoBluetooth dispositivo, CancellationToken ct = default);
    Task DesconectarAsync();
}

/// <summary>Lo propio de Android que necesita la pantalla de diagnóstico: permisos, dispositivos emparejados y transportes.</summary>
public interface IBluetoothDiagnostico
{
    /// <summary>Pide en ejecución los permisos de Bluetooth que correspondan a la versión de Android.</summary>
    Task<EstadoPermisosBluetooth> PedirPermisosAsync();

    /// <summary>Los dispositivos ya emparejados (no busca nuevos). Hace falta tener los permisos.</summary>
    IReadOnlyList<DispositivoBluetooth> ListarEmparejados();

    /// <summary>Abre los ajustes de Bluetooth de Android para emparejar el bastón.</summary>
    void AbrirAjustesBluetooth();

    ITransporteDiagnostico CrearTransporteSpp();
    ITransporteDiagnostico CrearTransporteBle();
}

/// <summary>Entrega del archivo de registro: compartirlo o dejarlo en Descargas.</summary>
public interface IEntregaRegistro
{
    /// <summary>Copia el registro a la carpeta Descargas. Devuelve el nombre final, o null si no se pudo.</summary>
    Task<string?> GuardarEnDescargasAsync(string ruta);

    /// <summary>Abre las opciones de compartir del sistema con el archivo.</summary>
    Task CompartirAsync(string ruta);
}
