namespace Aviss.Application.Importacion;

/// <summary>Topes de la importación (RF-2, RF-11, RF-15).</summary>
public static class LimitesImportacion
{
    /// <summary>25 MB = 26.214.400 bytes. El servicio lee hasta MaxBytes + 1 para saber si se pasó.</summary>
    public const int MaxBytes = 26_214_400;

    /// <summary>20.000 filas de datos se aceptan; 20.001 se rechazan.</summary>
    public const int MaxFilas = 20_000;

    /// <summary>Números de fila de ejemplo por advertencia agrupada.</summary>
    public const int MaxEjemplosAdvertencia = 5;

    /// <summary>Largo máximo del nombre de un establecimiento o de un rodeo.</summary>
    public const int MaxLargoNombre = 100;
}
