using Aviss.Application.Campo;

namespace Aviss.App.ViewModels;

/// <summary>
/// Lo que se eligió para trabajar (veterinario activo y rodeo), para pasarlo de la pantalla de elección a la de trabajo
/// y a la de movimientos (spec 003, RF-1, RF-2, RF-36). Es un singleton: sobrevive mientras la app siga viva, así que
/// rotar la tablet o pasar a segundo plano no lo pierde. No se guarda en ningún lado: al cerrar la app se pierde y se
/// vuelve a elegir (el veterinario queda recordado aparte, en las preferencias).
/// </summary>
public sealed class SesionTrabajo
{
    public VeterinarioDto? Veterinario { get; private set; }
    public RodeoParaTrabajarDto? Rodeo { get; private set; }

    /// <summary>Hay veterinario y rodeo elegidos: se puede trabajar.</summary>
    public bool EstaLista => Veterinario is not null && Rodeo is not null;

    public void Empezar(VeterinarioDto veterinario, RodeoParaTrabajarDto rodeo)
    {
        Veterinario = veterinario;
        Rodeo = rodeo;
    }

    /// <summary>Cambiar de veterinario sin salir del rodeo ni de la manga (RF-1).</summary>
    public void CambiarVeterinario(VeterinarioDto veterinario) => Veterinario = veterinario;

    /// <summary>Al volver a la elección de rodeo. El veterinario se conserva para preseleccionarlo.</summary>
    public void SalirDelRodeo() => Rodeo = null;
}
