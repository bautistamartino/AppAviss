using Aviss.Application.Comun;

namespace Aviss.Application.Importacion;

/// <summary>Por qué una fila de datos queda excluida (RF-8, RF-9). Una fila tiene a lo sumo un motivo.</summary>
public enum MotivoFila
{
    IdVacio,
    IdCaracteres,
    IdLargo,
    IdNumeroInvalido,
    IdTipoInvalido,
    IdRepetido
}

/// <summary>Por qué se rechaza el archivo entero (RF-2, RF-3).</summary>
public enum MotivoRechazoArchivo
{
    Vacio,
    Excede25Mb,
    FormatoNoSoportado,
    Protegido,
    Corrupto,
    SinHojas,
    FaltaColumna,
    EncabezadoDuplicado,
    SinFilasDeDatos,
    ExcedeFilas
}

/// <summary>
/// Mensajes fijos en español de la importación (RF-24): nunca llevan caravanas ni nombres de tambos.
/// El único dato variable permitido es el nombre de una COLUMNA (ID, RPRO...) en dos rechazos de encabezados.
/// </summary>
public static class MensajesImportacion
{
    /// <summary>Texto exacto pedido por la spec (RF-15).</summary>
    public const string EstablecimientoRepetido = "Ya existe un establecimiento con ese nombre; elegilo de la lista.";

    /// <summary>Spec 010 RF-4: texto fijo, sin el valor del EID (RF-19).</summary>
    public static string EidInvalido(int cantidad)
        => $"El EID de {cantidad} fila(s) no tiene 15 dígitos; esos animales se importan sin EID.";

    /// <summary>Spec 010 RF-5: texto fijo, sin el valor del EID (RF-19).</summary>
    public static string EidRepetido(int cantidad)
        => $"{cantidad} fila(s) comparten el mismo EID; esos animales se importan sin EID.";

    public static string Para(MotivoFila motivo) => motivo switch
    {
        MotivoFila.IdVacio => "La fila no tiene número de caravana (ID).",
        MotivoFila.IdCaracteres => "El ID tiene espacios o caracteres no permitidos en el medio.",
        MotivoFila.IdLargo => $"El ID supera los {Domain.Reglas.Caravana.LargoMaximo} caracteres.",
        MotivoFila.IdNumeroInvalido => "El ID es un número con decimales, negativo o demasiado grande.",
        MotivoFila.IdTipoInvalido => "El ID no es un texto ni un número (es una fecha, una fórmula o un error).",
        MotivoFila.IdRepetido => "El ID aparece en más de una fila del archivo; no se sabe cuál es la correcta.",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo))
    };

    /// <param name="detalle">Nombre de la columna, solo para <see cref="MotivoRechazoArchivo.FaltaColumna"/> y <see cref="MotivoRechazoArchivo.EncabezadoDuplicado"/>.</param>
    public static string Para(MotivoRechazoArchivo motivo, string? detalle = null) => motivo switch
    {
        MotivoRechazoArchivo.Vacio => "El archivo está vacío.",
        MotivoRechazoArchivo.Excede25Mb => "El archivo pesa más de 25 MB y no se puede importar.",
        MotivoRechazoArchivo.FormatoNoSoportado => "El archivo no es un listado de Dairy Comp (.xls, .csv o .txt con una columna ID). Los archivos .xlsx todavía no se pueden importar.",
        MotivoRechazoArchivo.Protegido => "El archivo está protegido con contraseña y no se puede leer.",
        MotivoRechazoArchivo.Corrupto => "El archivo está dañado y no se puede leer.",
        MotivoRechazoArchivo.SinHojas => "El archivo no tiene ninguna hoja.",
        MotivoRechazoArchivo.FaltaColumna => string.IsNullOrWhiteSpace(detalle)
            ? "Falta una columna obligatoria en el archivo."
            : $"Falta la columna obligatoria {detalle} en el archivo.",
        MotivoRechazoArchivo.EncabezadoDuplicado => string.IsNullOrWhiteSpace(detalle)
            ? "Hay una columna repetida en los encabezados del archivo."
            : $"La columna {detalle} está repetida en los encabezados del archivo.",
        MotivoRechazoArchivo.SinFilasDeDatos => "El archivo no tiene filas de datos, solo encabezados.",
        MotivoRechazoArchivo.ExcedeFilas => $"El archivo tiene más de {LimitesImportacion.MaxFilas:N0} filas de datos y no se puede importar.",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo))
    };

    /// <summary>Mensaje de los códigos de error propios de la importación. Para otros códigos lanza.</summary>
    public static string Para(CodigoError codigo) => codigo switch
    {
        CodigoError.ArchivoRechazado => "No se pudo leer el archivo elegido.",
        CodigoError.VistaPreviaNoDisponible => "La vista previa ya no está disponible; elegí el archivo de nuevo.",
        CodigoError.ImportacionEnCurso => "Ya hay una importación en curso; esperá a que termine.",
        CodigoError.ImportacionYaConfirmada => "Esta importación ya se confirmó; no se vuelve a crear el rodeo.",
        CodigoError.ArchivoRepetidoSinConfirmar => "Este archivo ya se importó antes; confirmá que querés importarlo igual como un rodeo nuevo.",
        CodigoError.SinFilasValidas => "No hay ninguna fila válida para importar.",
        _ => throw new ArgumentOutOfRangeException(nameof(codigo), "Ese código no es de la importación.")
    };
}
