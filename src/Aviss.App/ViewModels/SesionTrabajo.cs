using Aviss.Application.Campo;
using Aviss.Domain.Entidades;

namespace Aviss.App.ViewModels;

/// <summary>
/// Lo que se eligió para trabajar (el rodeo), para pasarlo de la pantalla de elección a la de trabajo y a la de
/// movimientos (spec 003, RF-2, RF-36; spec 006: ya no hay veterinario). Es un singleton: sobrevive mientras la app siga viva,
/// así que rotar la tablet o pasar a segundo plano no lo pierde. No se guarda en ningún lado: al cerrar la app se pierde y se
/// vuelve a elegir.
/// </summary>
public sealed class SesionTrabajo
{
    public RodeoParaTrabajarDto? Rodeo { get; private set; }

    /// <summary>Hay un rodeo elegido: se puede trabajar.</summary>
    public bool EstaLista => Rodeo is not null;

    // TRANSITORIO: lo borra la T2 de la spec 006. Los comandos todavía piden un UsuarioId y se les pasa el usuario
    // "Sin asignar" (existe en toda base); cuando los comandos pierdan el campo, esto desaparece.
    public Guid UsuarioId => Usuario.SinAsignarId;

    public void Empezar(RodeoParaTrabajarDto rodeo) => Rodeo = rodeo;

    /// <summary>Al volver a la elección de rodeo.</summary>
    public void SalirDelRodeo() => Rodeo = null;
}
