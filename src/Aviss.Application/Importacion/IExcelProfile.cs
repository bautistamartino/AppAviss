namespace Aviss.Application.Importacion;

// Contrato entre el formato del archivo (perfil, en Aviss.Excel) y las reglas de negocio (aquí, sin NPOI).
// El perfil solo entrega una LECTURA CRUDA: encabezados, filas con celdas tipadas y el total declarado.
// Todo lo demás (ID, repetidos, RPRO, JSON de originales) lo decide Application (RF-25).

/// <summary>Un formato de archivo soportado (hoy solo Dairy Comp). Cambiar de sistema = escribir otro perfil.</summary>
public interface IExcelProfile
{
    /// <summary>Nombre que queda guardado en el rodeo (por ejemplo "Dairy Comp").</summary>
    string Nombre { get; }

    /// <summary>
    /// Lee los bytes del archivo (nunca lanza: todo problema se devuelve como rechazo).
    /// <paramref name="maxFilas"/> es el tope de filas de datos; si el archivo lo supera, rechaza con
    /// <see cref="MotivoRechazoArchivo.ExcedeFilas"/>.
    /// </summary>
    ResultadoLectura Leer(byte[] contenido, int maxFilas);
}

/// <summary>Tipo de una celda tal como lo entrega el archivo.</summary>
public enum TipoCelda { Texto, Numerica, Fecha, Booleana, Error, Formula, Blanco }

/// <summary>
/// Una celda leída. <see cref="Texto"/> es el texto tal como lo entrega la celda (las numéricas en formato invariante
/// sin ceros de más; las fechas, como el texto que la celda muestra). <see cref="Numero"/> solo viene en las numéricas.
/// </summary>
public sealed record CeldaLeida(TipoCelda Tipo, string Texto, double? Numero)
{
    public static CeldaLeida EnBlanco { get; } = new(TipoCelda.Blanco, "", null);

    public static CeldaLeida DeTexto(string texto) => new(TipoCelda.Texto, texto, null);

    /// <summary>Numérica: el texto es la forma invariante del número (123 y no 123.0).</summary>
    public static CeldaLeida DeNumero(double numero)
        => new(TipoCelda.Numerica, numero.ToString("R", System.Globalization.CultureInfo.InvariantCulture), numero);

    public static CeldaLeida De(TipoCelda tipo, string texto) => new(tipo, texto, null);

    /// <summary>Vacía = en blanco o con solo espacios (RF-6).</summary>
    public bool EsVacia => Tipo == TipoCelda.Blanco || string.IsNullOrWhiteSpace(Texto);
}

/// <summary>Una fila de datos. <see cref="NumeroFila"/> es el número de fila de Excel (1 = primera). Las celdas van por encabezado original.</summary>
public sealed record FilaLeida(int NumeroFila, IReadOnlyDictionary<string, CeldaLeida> Celdas);

/// <summary>Lo que el perfil entiende del archivo, sin juicios de negocio.</summary>
/// <param name="Encabezados">Encabezados originales no vacíos, en el orden del archivo (con su capitalización).</param>
/// <param name="NombreColumnaId">Encabezado original de la columna ID.</param>
/// <param name="NombreColumnaEstado">Encabezado original de la columna RPRO.</param>
/// <param name="ColumnasOpcionalesFaltantes">Columnas opcionales que el archivo no trae (RF-4).</param>
/// <param name="Filas">Filas de datos (sin la fila Total y sin filas totalmente vacías).</param>
/// <param name="TotalDeclarado">El N de la fila final "Total:N", si la hay (RF-5).</param>
public sealed record LecturaExcel(
    IReadOnlyList<string> Encabezados,
    string NombreColumnaId,
    string NombreColumnaEstado,
    IReadOnlyList<string> ColumnasOpcionalesFaltantes,
    IReadOnlyList<FilaLeida> Filas,
    int? TotalDeclarado);

/// <summary>O bien una lectura, o bien el motivo por el que se rechaza el archivo entero (con su mensaje).</summary>
public sealed class ResultadoLectura
{
    public LecturaExcel? Lectura { get; }
    public MotivoRechazoArchivo? Rechazo { get; }
    public string? Mensaje { get; }
    public bool EsCorrecto => Lectura is not null;

    private ResultadoLectura(LecturaExcel lectura) { Lectura = lectura; }

    private ResultadoLectura(MotivoRechazoArchivo rechazo, string mensaje)
    {
        Rechazo = rechazo;
        Mensaje = mensaje;
    }

    public static ResultadoLectura Correcto(LecturaExcel lectura) => new(lectura);

    /// <param name="detalle">Solo para <see cref="MotivoRechazoArchivo.FaltaColumna"/> y <see cref="MotivoRechazoArchivo.EncabezadoDuplicado"/>: el nombre de la columna.</param>
    public static ResultadoLectura Rechazado(MotivoRechazoArchivo motivo, string? detalle = null)
        => new(motivo, MensajesImportacion.Para(motivo, detalle));
}
