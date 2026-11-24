using System.Text;

namespace Aviss.Rfid.Diagnostico;

/// <summary>Un tipo de separador encontrado en una trama y cuántas veces apareció.</summary>
public sealed record SeparadorDetectado(string Nombre, int Cantidad);

/// <summary>
/// Una trama de bytes "traducida" para mirarla: hexadecimal, texto con los caracteres no imprimibles marcados
/// (<c>&lt;CR&gt;</c>, <c>&lt;0x1B&gt;</c>...), longitud y separadores detectados. No interpreta nada (spike de la spec 008).
/// </summary>
public sealed record TramaDecodificada(string Hex, string Texto, int Longitud, IReadOnlyList<SeparadorDetectado> Separadores)
{
    /// <summary>Cantidad de veces que aparece un separador (0 si no está).</summary>
    public int Cantidad(string nombre) => Separadores.FirstOrDefault(s => s.Nombre == nombre)?.Cantidad ?? 0;

    /// <summary>Los separadores como texto corto para mostrar: "CRLF x1, COMA x2". Vacío si no hay.</summary>
    public string SeparadoresTexto => string.Join(", ", Separadores.Select(s => $"{s.Nombre} x{s.Cantidad}"));
}

/// <summary>Decodificador de bytes crudos a algo legible. Sin dependencias de Android: se prueba con tramas inventadas.</summary>
public static class DecodificadorTrama
{
    public static TramaDecodificada Decodificar(ReadOnlySpan<byte> datos)
    {
        var hex = new StringBuilder(datos.Length * 3);
        var texto = new StringBuilder(datos.Length);
        // Se cuentan en un orden fijo para que la salida sea siempre igual.
        var cuenta = new Dictionary<string, int>();
        void Sumar(string nombre) => cuenta[nombre] = cuenta.GetValueOrDefault(nombre) + 1;

        for (var i = 0; i < datos.Length; i++)
        {
            var b = datos[i];
            if (i > 0) hex.Append(' ');
            hex.Append(b.ToString("X2"));

            switch (b)
            {
                case 0x0D when i + 1 < datos.Length && datos[i + 1] == 0x0A:
                    // CR seguido de LF es UN separador (CRLF); en el texto se ven los dos marcadores.
                    Sumar("CRLF");
                    texto.Append("<CR><LF>");
                    hex.Append(' ').Append("0A");
                    i++;
                    break;
                case 0x0D: Sumar("CR"); texto.Append("<CR>"); break;
                case 0x0A: Sumar("LF"); texto.Append("<LF>"); break;
                case 0x00: Sumar("NUL"); texto.Append("<NUL>"); break;
                case 0x09: Sumar("TAB"); texto.Append("<TAB>"); break;
                case 0x02: Sumar("STX"); texto.Append("<STX>"); break;
                case 0x03: Sumar("ETX"); texto.Append("<ETX>"); break;
                case (byte)' ': Sumar("ESPACIO"); texto.Append(' '); break;
                case (byte)',': Sumar("COMA"); texto.Append(','); break;
                case (byte)';': Sumar("PUNTO_Y_COMA"); texto.Append(';'); break;
                default:
                    // ASCII imprimible tal cual; el resto (controles y bytes altos) se marca con su valor.
                    if (b is >= 0x20 and <= 0x7E) texto.Append((char)b);
                    else texto.Append("<0x").Append(b.ToString("X2")).Append('>');
                    break;
            }
        }

        var orden = new[] { "CRLF", "CR", "LF", "TAB", "NUL", "STX", "ETX", "ESPACIO", "COMA", "PUNTO_Y_COMA" };
        var separadores = orden.Where(cuenta.ContainsKey).Select(n => new SeparadorDetectado(n, cuenta[n])).ToList();
        return new TramaDecodificada(hex.ToString(), texto.ToString(), datos.Length, separadores);
    }
}

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

/// <summary>Para la tercera ruta (bastón que "tipea" como teclado): saca las líneas completas del texto de un campo.</summary>
public static class AcumuladorTeclado
{
    /// <returns>Las líneas terminadas (CR, LF o CRLF, sin vacías) y el resto sin terminar, que queda en el campo.</returns>
    public static (IReadOnlyList<string> Lineas, string Resto) Procesar(string texto)
    {
        var lineas = new List<string>();
        var actual = new StringBuilder();
        foreach (var c in texto)
        {
            if (c is '\r' or '\n')
            {
                if (actual.Length > 0) lineas.Add(actual.ToString());
                actual.Clear();
            }
            else actual.Append(c);
        }
        return (lineas, actual.ToString());
    }
}
