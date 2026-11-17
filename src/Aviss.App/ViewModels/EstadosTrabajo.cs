using Aviss.Application.Campo;

namespace Aviss.App.ViewModels;

// Los "estados" de la pantalla de trabajo (spec 003, diagrama del plan). Son datos puros: la lógica vive en TrabajoViewModel.

/// <summary>Qué hay en el centro de la pantalla de trabajo.</summary>
public enum EtapaTrabajo
{
    /// <summary>Lista para la próxima lectura (con el resumen de lo último guardado).</summary>
    Esperando,
    /// <summary>Hay una ficha abierta (la de la última vaca leída, buscada o dada de alta).</summary>
    Ficha,
    /// <summary>Se terminó el tacto: se muestra el resumen con "Deshacer" y se puede salir (RF-7).</summary>
    Terminado
}

/// <summary>
/// La capa (cuadro) que está encima de la pantalla de trabajo. Los cuadros que <b>piden datos o confirmación</b>
/// (<see cref="TrabajoViewModel"/> los lista en <c>Bloquea</c>) frenan el procesamiento de lecturas y guardan solo la
/// última; "Aviso" y "NoEncontrada" no: una lectura nueva los cierra y se procesa (RF-19, RF-22b).
/// </summary>
public enum CuadroTrabajo
{
    Ninguno,
    /// <summary>"Animal repetido" o "Ya pasó" (RF-22).</summary>
    Aviso,
    /// <summary>"Caravana no encontrada": dar de alta o buscar a mano (RF-24).</summary>
    NoEncontrada,
    /// <summary>Formulario de alta en campo (RF-25).</summary>
    Alta,
    /// <summary>"¿Deshacer lo último de esta vaca?" (RF-22).</summary>
    ConfirmarCorregir,
    /// <summary>Contador y caravanas con "Coincide" / "No coincide" (RF-6, RF-7).</summary>
    Verificacion,
    /// <summary>Cantidad real de animales, con teclado numérico (RF-6).</summary>
    CantidadReal,
    /// <summary>"¿Terminar el tacto?" (RF-7).</summary>
    SegundaConfirmacion
}

/// <summary>Con qué botón se llegó a la verificación de cierre.</summary>
public enum PropositoCierre { Finalizar, Terminar }

/// <summary>El aviso de 5 segundos con "Deshacer" (RF-27): qué se deshace y qué texto mostrar.</summary>
/// <param name="Id">Id del tacto, de la marca o del cierre o, en un alta, del animal (lo que pide <c>DeshacerAsync</c>).</param>
public sealed record AvisoDeshacerDto(TipoDeshacer Tipo, Guid Id, string Texto);
