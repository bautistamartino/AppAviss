namespace Aviss.Domain.Enums;

/// <summary>De dónde viene el animal: del Excel importado o dado de alta a mano en el campo.</summary>
public enum OrigenAnimal { Importado, AltaEnCampo }

/// <summary>Estado informativo del rodeo. Un rodeo Exportado se puede seguir tactando (RF-2).</summary>
public enum EstadoRodeo { Abierto, Exportado }

/// <summary>Un tacto es Normal o la Anulación de otro tacto (RF-13).</summary>
public enum TipoTacto { Normal, Anulacion }

/// <summary>Tipo de acción que quedó registrada en el historial (RF-10).</summary>
public enum AccionHistorial { Alta, Edicion, Tacto, AnulacionTacto, AnulacionAlta }

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
