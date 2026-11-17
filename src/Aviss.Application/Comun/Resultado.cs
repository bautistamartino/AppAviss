namespace Aviss.Application.Comun;

/// <summary>
/// Por qué un caso de uso rechazó una operación. Los rechazos de negocio se devuelven como <see cref="Resultado"/>
/// (no como excepciones); las excepciones quedan para fallas reales (disco, base).
/// </summary>
public enum CodigoError
{
    DatosInvalidos,
    NoEncontrado,
    Duplicado,
    DadoDeBaja,

    // Altas y ediciones de animales
    CaravanaInvalida,
    CaravanaDuplicada,
    IdElectronicoInvalido,
    IdElectronicoDuplicado,
    AnimalAnulado,
    AnimalImportadoNoSeAnula,
    AnimalConTactosVigentes,

    // Catálogo
    CodigoDairyCompDuplicado,

    // Registrar tacto (RF-8b)
    RodeoDeBaja,
    EstablecimientoDeBaja,
    EstadoDeBaja,
    UsuarioDeBaja,
    EstadoAnteriorNoCoincide,
    FechaFutura,
    FechaAnteriorAlUltimoTacto,
    DiasPrenezFueraDeRango,

    // Anular tacto (RF-14)
    TactoYaAnulado,
    TactoDeAnulacion,
    TactoNoEsElUltimo,

    // Copias de seguridad (RF-21)
    SinEspacioParaCopia,
    CopiaFallida,

    // Importación de Dairy Comp (spec 002); los mensajes fijos están en MensajesImportacion
    ArchivoRechazado,
    VistaPreviaNoDisponible,
    ImportacionEnCurso,
    ImportacionYaConfirmada,
    ArchivoRepetidoSinConfirmar,
    SinFilasValidas,

    // Flujo de campo (spec 003); los mensajes fijos están en MensajesCampo
    NoHayMangaAbierta,
    CantidadInvalida,
    MarcaYaAnulada,
    MarcaNoEsLaUltima,
    AnimalConMarcasVigentes,
    CierreYaAnulado,
    CierreNoEsElUltimo,
    CierreAutomaticoNoSeDeshace,
    CierreDeOtroDia,
    HayOtraMangaAbierta,
    FechaAnteriorAlUltimoRegistro
}

/// <summary>Resultado de una operación sin valor de retorno. El mensaje está en español y nunca lleva datos de animales ni de tambos.</summary>
public class Resultado
{
    public bool EsCorrecto { get; }
    public CodigoError? Codigo { get; }
    public string? Mensaje { get; }

    protected Resultado() { EsCorrecto = true; }

    protected Resultado(CodigoError codigo, string mensaje)
    {
        EsCorrecto = false;
        Codigo = codigo;
        Mensaje = mensaje;
    }

    public static Resultado Ok() => new();
    public static Resultado Error(CodigoError codigo, string mensaje) => new(codigo, mensaje);
    public static Resultado<T> Ok<T>(T valor) => Resultado<T>.Ok(valor);
    public static Resultado<T> Error<T>(CodigoError codigo, string mensaje) => Resultado<T>.Error(codigo, mensaje);
}

/// <summary>Resultado de una operación que devuelve un valor (por ejemplo el Id creado).</summary>
public sealed class Resultado<T> : Resultado
{
    private readonly T? _valor;

    /// <summary>Solo se puede leer si la operación salió bien.</summary>
    public T Valor => EsCorrecto
        ? _valor!
        : throw new InvalidOperationException("La operación falló: no hay valor.");

    private Resultado(T valor) { _valor = valor; }
    private Resultado(CodigoError codigo, string mensaje) : base(codigo, mensaje) { }

    public static Resultado<T> Ok(T valor) => new(valor);
    public new static Resultado<T> Error(CodigoError codigo, string mensaje) => new(codigo, mensaje);
}
