namespace Aviss.Rfid;

/// <summary>
/// Arma líneas a partir de paquetes de bytes que llegan partidos como sea. Terminadores: CR, LF o CRLF (aunque el CR y el LF
/// lleguen en paquetes distintos). Las líneas vacías se ignoran. Si pasa de <c>maximoBytes</c> sin terminador (bytes basura),
/// corta ahí para no crecer sin límite.
/// </summary>
public sealed class AcumuladorLineas
{
    private readonly int _maximo;
    private readonly List<byte> _buffer = [];
    private bool _ultimoFueCr;   // el siguiente LF, si llega, es parte del mismo terminador

    public AcumuladorLineas(int maximoBytes = 1024) => _maximo = maximoBytes;

    /// <summary>Bytes recibidos que todavía no tienen terminador.</summary>
    public byte[] Pendiente => [.. _buffer];

    public IReadOnlyList<byte[]> Agregar(ReadOnlySpan<byte> datos)
    {
        var lineas = new List<byte[]>();
        foreach (var b in datos)
        {
            if (b == 0x0A && _ultimoFueCr)
            {
                _ultimoFueCr = false;   // segunda mitad de un CRLF: ya se cerró la línea con el CR
                continue;
            }
            _ultimoFueCr = b == 0x0D;

            if (b == 0x0D || b == 0x0A)
            {
                Cerrar(lineas);
                continue;
            }

            _buffer.Add(b);
            if (_buffer.Count >= _maximo) Cerrar(lineas);
        }
        return lineas;
    }

    /// <summary>Entrega lo pendiente como línea (por si el bastón no manda terminador). Null si no hay nada.</summary>
    public byte[]? CerrarPendiente()
    {
        if (_buffer.Count == 0) return null;
        var linea = _buffer.ToArray();
        _buffer.Clear();
        return linea;
    }

    public void Reiniciar()
    {
        _buffer.Clear();
        _ultimoFueCr = false;
    }

    private void Cerrar(List<byte[]> lineas)
    {
        if (_buffer.Count == 0) return;   // línea vacía: se ignora
        lineas.Add([.. _buffer]);
        _buffer.Clear();
    }
}
