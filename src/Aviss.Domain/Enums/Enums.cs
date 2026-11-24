namespace Aviss.Domain.Enums;

/// <summary>De dónde viene el animal: del Excel importado o dado de alta a mano en el campo.</summary>
public enum OrigenAnimal { Importado, AltaEnCampo }

/// <summary>Estado informativo del rodeo. Un rodeo Exportado se puede seguir tactando (RF-2).</summary>
public enum EstadoRodeo { Abierto, Exportado }

/// <summary>Un tacto es Normal o la Anulación de otro tacto (RF-13).</summary>
public enum TipoTacto { Normal, Anulacion }

/// <summary>
/// Tipo de acción que quedó registrada en el historial (RF-10). Desde la spec 003 suma las acciones de la
/// marca de pasada y de las mangas. La base tiene un CHECK con estos mismos nombres (CK_Historial_Accion).
/// </summary>
public enum AccionHistorial
{
    Alta, Edicion, Tacto, AnulacionTacto, AnulacionAlta,
    MarcaPasada, AnulacionMarca, MangaAbierta, MangaCerrada, AnulacionCierre,
    // Spec 004: espera y rechazo.
    EsperaCreada, EsperaResuelta, EsperaSalteada, RechazoCreado, RechazoAnulado
}

/// <summary>
/// Qué hace un evento del catálogo al guardarse (spec 005, RF-1): <c>Estado</c> cambia el estado reproductivo de la vaca
/// (PREÑADA, VACIA, ...); <c>SoloEvento</c> queda registrado pero NO cambia el estado ni el último tacto (OK, Aborto,
/// Revisar Nuevamente). En la base es texto con un CHECK.
/// </summary>
public enum TipoEvento { Estado, SoloEvento }

/// <summary>
/// Los pasos que un evento del catálogo puede pedir después de elegirlo (spec 005, RF-27). El evento los declara en su
/// columna <c>Pasos</c> (nombres separados por coma); un único mecanismo de pantalla los recorre en orden.
/// </summary>
public enum PasoEvento { DiasPreniez, DiasAborto, ResultadoAborto }

/// <summary>
/// Cómo terminó un aborto (spec 005, RF-28). No se guarda en una columna: se deriva del estado del tacto (VACIA, INSEMIN,
/// SECA, FRESCA) o de que el registro sea un <c>Rechazo</c>.
/// </summary>
public enum ResultadoAborto { VaciaOk, Inseminada, Seca, Fresca, Rechazo }

/// <summary>En qué quedó una espera (spec 004, RF-13): pendiente, o resuelta (con un tacto o rechazo) o salteada.</summary>
public enum EstadoEspera { Pendiente, Resuelta, Salteada }

/// <summary>Cómo se cerró una manga (spec 003, RF-6, RF-7, RF-10).</summary>
public enum TipoCierreManga
{
    /// <summary>"Finalizar manga": el veterinario cerró esta manga y seguirá con otra.</summary>
    Manual,
    /// <summary>"Terminar el tacto": cierra la manga abierta y da por terminado el trabajo.</summary>
    TerminoElTacto,
    /// <summary>Red de seguridad: se cerró sola porque quedó abierta de un día anterior; sin cantidad confirmada.</summary>
    Automatico
}

/// <summary>Por qué <c>ReglasMarcaPasada.Validar</c> rechaza una marca de pasada (RF-21).</summary>
public enum MotivoRechazoMarca
{
    AnimalAnulado,
    RodeoDeBaja,
    EstablecimientoDeBaja,
    FechaFutura,
    FechaAnteriorAlUltimoRegistro
}

/// <summary>Por qué no se puede reabrir una manga, es decir, deshacer su cierre (RF-9).</summary>
public enum MotivoNoReabrir
{
    CierreYaAnulado,
    CierreNoEsElUltimo,
    CierreAutomatico,
    CierreDeOtroDia,
    HayOtraMangaAbierta
}

/// <summary>Por qué <c>ReglasTacto.Validar</c> rechaza un tacto (RF-8b).</summary>
public enum MotivoRechazoTacto
{
    AnimalAnulado,
    RodeoDeBaja,
    EstablecimientoDeBaja,
    EstadoNuevoDeBaja,
    EstadoAnteriorNoCoincide,
    FechaFutura,
    FechaAnteriorAlUltimoTacto,
    DiasPrenezFueraDeRango
}
