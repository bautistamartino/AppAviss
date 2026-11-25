using System.Globalization;

namespace Aviss.Rfid.Diagnostico;

/// <summary>Una fila del visor en vivo: un paquete de bytes, una línea armada o una línea del teclado.</summary>
public sealed record EntradaVisor(
    int Numero, DateTime HoraUtc, string Fuente, string Tipo, TramaDecodificada Trama, IReadOnlyList<CandidatoCaravana> Candidatos)
{
    public int Longitud => Trama.Longitud;

    /// <summary>La marca de hora en el huso de la tablet, para mostrarla.</summary>
    public string HoraLocal => HoraUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string CandidatosTexto => Candidatos.Count == 0 ? "" : string.Join(", ", Candidatos.Select(c => c.Digitos));
}

/// <summary>
/// Lo que pasa en una sesión de diagnóstico, sin Android: recibe bytes (o líneas del teclado), los decodifica, arma las líneas,
/// busca candidatos a caravana, cuenta y lo anota todo en el registro. No decide qué es una caravana (spike de la spec 008).
/// Es seguro llamarlo desde cualquier hilo.
/// </summary>
public sealed class SesionDiagnostico
{
    private readonly IRegistroDiagnostico _registro;
    private readonly Func<DateTime> _ahoraUtc;
    private readonly Dictionary<string, AcumuladorLineas> _acumuladores = [];
    private readonly object _candado = new();

    private int _numeroEntrada;
    private string _ultimaLectura = "ninguna";
    private string _ultimoTexto = "";

    public SesionDiagnostico(IRegistroDiagnostico registro, Func<DateTime> ahoraUtc, OpcionesParserCaravana opciones)
    {
        _registro = registro;
        _ahoraUtc = ahoraUtc;
        Opciones = opciones;
    }

    public OpcionesParserCaravana Opciones { get; set; }

    /// <summary>Paquetes de bytes recibidos (cada vez que el bastón o el sistema entrega algo).</summary>
    public int Paquetes { get; private set; }

    /// <summary>Líneas completas armadas (por terminador) o recibidas por el teclado.</summary>
    public int Lineas { get; private set; }

    /// <summary>Se dispara por cada fila nueva del visor (paquete y, si corresponde, las líneas que completó). Puede venir de
    /// cualquier hilo: quien se suscribe debe pasar al hilo de la interfaz.</summary>
    public event Action<EntradaVisor>? EntradaNueva;

    public EntradaVisor RecibirBytes(string fuente, byte[] datos)
    {
        var nuevas = new List<EntradaVisor>();
        EntradaVisor paquete;
        lock (_candado)
        {
            Paquetes++;
            paquete = Registrar(fuente, "PAQUETE", Paquetes, datos);
            nuevas.Add(paquete);

            if (!_acumuladores.TryGetValue(fuente, out var acumulador))
                _acumuladores[fuente] = acumulador = new AcumuladorLineas();
            foreach (var linea in acumulador.Agregar(datos))
            {
                Lineas++;
                nuevas.Add(Registrar(fuente, "LINEA", Lineas, linea));
                _ultimaLectura = Lineas.ToString(CultureInfo.InvariantCulture);
            }
            if (Lineas == 0) _ultimaLectura = "paquete" + Paquetes.ToString(CultureInfo.InvariantCulture);
        }
        foreach (var e in nuevas) EntradaNueva?.Invoke(e);
        return paquete;
    }

    /// <summary>Cierra como línea lo que quedó sin terminador (por si el bastón no manda CR ni LF).</summary>
    public EntradaVisor? CerrarPendientes(string fuente)
    {
        EntradaVisor? entrada = null;
        lock (_candado)
        {
            if (_acumuladores.TryGetValue(fuente, out var a) && a.CerrarPendiente() is { } linea)
            {
                Lineas++;
                entrada = Registrar(fuente, "LINEA", Lineas, linea);
                _ultimaLectura = Lineas.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (entrada is not null) EntradaNueva?.Invoke(entrada);
        return entrada;
    }

    /// <summary>Tercera ruta: una línea que entró como teclado.</summary>
    public EntradaVisor RecibirLineaTeclado(string linea)
    {
        EntradaVisor entrada;
        lock (_candado)
        {
            Lineas++;
            entrada = Registrar("TECLADO", "TECLADO", Lineas, System.Text.Encoding.UTF8.GetBytes(linea));
            _ultimaLectura = Lineas.ToString(CultureInfo.InvariantCulture);
        }
        EntradaNueva?.Invoke(entrada);
        return entrada;
    }

    /// <summary>Anota a mano la caravana impresa de la lectura actual (la última), para relacionarla con el ID del Excel.</summary>
    public void Anotar(string caravanaImpresa)
    {
        lock (_candado)
            _registro.Escribir($"{Hora()} | ANOTACION | caravana_impresa={caravanaImpresa} | lectura={_ultimaLectura} | ultimo_texto={_ultimoTexto}");
    }

    /// <summary>Una nota libre (estado de la conexión, intentos, servicios BLE...). Va al registro.</summary>
    public void Nota(string texto)
    {
        lock (_candado) _registro.Escribir($"{Hora()} | NOTA | {texto}");
    }

    /// <summary>Limpia el visor: contadores y líneas pendientes en cero. El registro del archivo NO se borra.</summary>
    public void Limpiar()
    {
        lock (_candado)
        {
            Paquetes = 0;
            Lineas = 0;
            _numeroEntrada = 0;
            _ultimaLectura = "ninguna";
            _ultimoTexto = "";
            foreach (var a in _acumuladores.Values) a.Reiniciar();
            _registro.Escribir($"{Hora()} | NOTA | visor limpiado (contadores en cero)");
        }
    }

    private EntradaVisor Registrar(string fuente, string tipo, int numero, byte[] datos)
    {
        var trama = DecodificadorTrama.Decodificar(datos);
        // Los candidatos se buscan en las líneas y en lo que entra por teclado; en un paquete suelto puede haber medio código.
        var candidatos = tipo == "PAQUETE" ? [] : ParserCandidatosCaravana.Buscar(trama.Texto, Opciones);
        var hora = _ahoraUtc();
        var entrada = new EntradaVisor(++_numeroEntrada, hora, fuente, tipo, trama, candidatos);

        if (tipo != "PAQUETE") _ultimoTexto = trama.Texto;

        var linea = $"{Hora(hora)} | {fuente} | {tipo} #{numero} | len={trama.Longitud} | hex={trama.Hex} | txt={trama.Texto}";
        if (trama.Separadores.Count > 0) linea += $" | sep={trama.SeparadoresTexto}";
        if (candidatos.Count > 0) linea += $" | candidato={string.Join(",", candidatos.Select(c => c.Digitos))}";
        _registro.Escribir(linea);
        return entrada;
    }

    private string Hora() => Hora(_ahoraUtc());

    private static string Hora(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
