using Aviss.Domain.Reglas;

namespace Aviss.Application.Importacion;

/// <summary>Una fila válida lista para convertirse en animal importado. No lleva observaciones: COM queda solo en el JSON (RF-12).</summary>
/// <param name="NumeroFila">Número de fila de Excel.</param>
/// <param name="EstadoId">Estado del catálogo, o nulo si RPRO estaba vacío o no coincide con ningún estado activo.</param>
/// <param name="DatosOriginalesJson">Columnas originales como objeto JSON (RF-12).</param>
/// <param name="IdElectronico">EID de 15 dígitos sin espacios (spec 010), o nulo si no venía, era inválido o estaba repetido.</param>
public sealed record FilaAnimalImportada(
    int NumeroFila, Caravana Caravana, Guid? EstadoId, string DatosOriginalesJson, string? IdElectronico = null);

/// <summary>Una fila excluida: solo número de fila y motivo (nunca la caravana, RF-13 y RF-24).</summary>
public sealed record FilaExcluida(int NumeroFila, MotivoFila Motivo)
{
    public string Mensaje => MensajesImportacion.Para(Motivo);
}

public enum TipoAdvertencia { ColumnaOpcionalFaltante, TotalNoCoincide, EstadoDesconocido, EidInvalido, EidRepetido }

/// <summary>
/// Un aviso que no impide importar. Los de estado desconocido van agrupados por valor de RPRO: el valor,
/// la cantidad de filas y a lo sumo 5 números de fila de ejemplo (RF-11).
/// </summary>
/// <param name="Valor">Nombre de la columna o valor de RPRO (datos que la spec permite mostrar); nulo en el aviso de total.</param>
public sealed record AdvertenciaImportacion(
    TipoAdvertencia Tipo, string Mensaje, string? Valor, int Cantidad, IReadOnlyList<int> FilasEjemplo);

/// <summary>Cuántas vacas válidas hay por estado. <see cref="EstadoId"/> nulo = "sin estado".</summary>
public sealed record ConteoEstado(Guid? EstadoId, string Nombre, int Cantidad);

/// <summary>Un rodeo ya guardado con la misma huella que el archivo (RF-16): solo nombre y fecha.</summary>
public sealed record RodeoImportadoResumen(string Nombre, DateTime FechaImportacionUtc);

/// <summary>Un establecimiento activo para elegir en la vista previa.</summary>
public sealed record EstablecimientoResumen(Guid Id, string Nombre);

/// <summary>
/// El resultado de leer y validar un archivo. Vive en memoria hasta que se confirma o se descarta (RF-22).
/// Guarda los MISMOS bytes que se leyeron y hashearon: son los que se guardan en el rodeo (RF-1).
/// </summary>
public sealed record VistaPreviaImportacion(
    Guid Id,
    string NombreArchivo,
    string Hash,
    byte[] Contenido,
    string PerfilNombre,
    int FilasLeidas,
    IReadOnlyList<FilaAnimalImportada> Validas,
    IReadOnlyList<FilaExcluida> Excluidas,
    IReadOnlyList<AdvertenciaImportacion> Advertencias,
    IReadOnlyList<ConteoEstado> ConteoPorEstado,
    IReadOnlyList<RodeoImportadoResumen> ArchivoRepetido,
    bool HayColumnaEid = false,     // spec 010: el archivo trae columna EID
    int ConEid = 0,                 // animales que se importan con EID
    int SinEid = 0)                 // animales sin EID (guion, vacío, inválido o repetido)
{
    /// <summary>Con filas de datos pero ninguna válida la vista se muestra igual, pero no se puede confirmar (RF-13).</summary>
    public bool PuedeConfirmar => Validas.Count > 0;

    public bool EsArchivoRepetido => ArchivoRepetido.Count > 0;
}

/// <summary>Resultado de importar (RF-21).</summary>
public sealed record ResultadoImportacion(
    Guid RodeoId,
    string NombreRodeo,
    int FilasLeidas,
    int AnimalesImportados,
    int FilasExcluidas,
    IReadOnlyList<AdvertenciaImportacion> Advertencias,
    bool HayColumnaEid = false,
    int ConEid = 0,
    int SinEid = 0);
