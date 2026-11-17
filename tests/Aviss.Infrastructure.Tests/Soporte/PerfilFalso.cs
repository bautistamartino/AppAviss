using Aviss.Application.Importacion;

namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Un perfil de Excel de mentira (RF-25): no lee ningún archivo, devuelve la lectura que el test arma a mano.
/// Sirve para probar el servicio sin crear .xls y para demostrar que otro formato se enchufa sin tocar nada más.
/// </summary>
public sealed class PerfilFalso : IExcelProfile
{
    private readonly Func<byte[], ResultadoLectura> _leer;

    public PerfilFalso(string nombre, Func<byte[], ResultadoLectura> leer) => (Nombre, _leer) = (nombre, leer);

    /// <summary>Un perfil que siempre devuelve la misma lectura.</summary>
    public PerfilFalso(string nombre, LecturaExcel lectura) : this(nombre, _ => ResultadoLectura.Correcto(lectura)) { }

    public string Nombre { get; }

    /// <summary>Cuántas veces se llamó a Leer (para comprobar que un archivo rechazado por tamaño ni se lee).</summary>
    public int Llamadas { get; private set; }

    public ResultadoLectura Leer(byte[] contenido, int maxFilas)
    {
        Llamadas++;
        return _leer(contenido);
    }

    // ---- Armado de lecturas ----

    public const string ColumnaId = "ID";
    public const string ColumnaEstado = "RPRO";

    /// <summary>Una fila con ID de texto y RPRO. Número de fila de Excel = posición + 2 (la 1 son los encabezados).</summary>
    public static FilaLeida Fila(int numeroFila, string? id, string? rpro = null, string? com = null)
    {
        var celdas = new Dictionary<string, CeldaLeida>
        {
            [ColumnaId] = id is null ? CeldaLeida.EnBlanco : CeldaLeida.DeTexto(id),
            [ColumnaEstado] = rpro is null ? CeldaLeida.EnBlanco : CeldaLeida.DeTexto(rpro),
            ["COM"] = com is null ? CeldaLeida.EnBlanco : CeldaLeida.DeTexto(com)
        };
        return new FilaLeida(numeroFila, celdas);
    }

    public static LecturaExcel Lectura(IEnumerable<FilaLeida> filas, int? totalDeclarado = null)
        => new([ColumnaId, ColumnaEstado, "COM"], ColumnaId, ColumnaEstado, [], filas.ToList(), totalDeclarado);

    /// <summary>N vacas con ID "1".."N" y estado PREÑADA.</summary>
    public static LecturaExcel Vacas(int cantidad)
        => Lectura(Enumerable.Range(1, cantidad).Select(i => Fila(i + 1, i.ToString(), "PREÑADA")));
}
