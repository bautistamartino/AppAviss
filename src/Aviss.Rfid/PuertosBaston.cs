using Aviss.Application.Puertos;
using Aviss.Rfid.Diagnostico;

namespace Aviss.Rfid;

// Puertos de plataforma del lector del bastón (spec 008). El lector (LectorBastonXrs2) no sabe nada de Android: habla con
// estas interfaces, que implementa Aviss.App/Platforms/Android y los tests reemplazan por falsos.

/// <summary>Qué tipo de socket Bluetooth abrir: el inseguro conectó siempre en la prueba; el seguro queda de respaldo.</summary>
public enum MetodoSocket { Inseguro, Seguro }

/// <summary>
/// El enlace no se pudo abrir por un motivo que la pantalla sabe explicar. El mensaje no lleva ni la dirección ni el nombre
/// del bastón (RF-26).
/// </summary>
public sealed class ErrorEnlace(MotivoSinBaston motivo) : Exception("No se pudo abrir el enlace con el bastón.")
{
    public MotivoSinBaston Motivo { get; } = motivo;
}

/// <summary>Una conexión serie abierta con el bastón.</summary>
public interface IConexionSerie
{
    /// <summary>Lee lo que haya. 0 o negativo = el bastón cerró la conexión.</summary>
    ValueTask<int> LeerAsync(Memory<byte> destino, CancellationToken ct);

    /// <summary>Cierra la conexión. Es idempotente y no lanza.</summary>
    ValueTask CerrarAsync();
}

/// <summary>Abre conexiones SPP con un dispositivo ya emparejado.</summary>
public interface IEnlaceSerieBaston
{
    /// <summary>
    /// Abre la conexión. Si el token se cancela, el intento se corta (la implementación cierra el socket). Si falla,
    /// lanza <see cref="ErrorEnlace"/> o cualquier excepción: ambas cuentan como "no respondió".
    /// </summary>
    Task<IConexionSerie> AbrirAsync(string direccion, MetodoSocket metodo, CancellationToken ct);
}

/// <summary>Bluetooth y permisos de la tablet. No pide ubicación ni búsqueda de dispositivos: solo emparejados (RF-26).</summary>
public interface IBluetoothBaston
{
    /// <summary>Pide el permiso de conexión si falta (Android 12 o más). Devuelve si quedó concedido.</summary>
    Task<bool> PedirPermisosAsync();
    bool TienePermisos();
    bool HayBluetooth();
    bool EstaEncendido();
    IReadOnlyList<DispositivoBluetooth> ListarEmparejados();
    bool EstaEmparejado(string direccion);
    void AbrirAjustesBluetooth();
    void AbrirAjustesDeLaApp();
}
