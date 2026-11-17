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
    MarcaPasada, AnulacionMarca, MangaAbierta, MangaCerrada, AnulacionCierre
}

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
