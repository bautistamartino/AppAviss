namespace Aviss.Rfid;

/// <summary>
/// Espera creciente entre ciclos de conexión (RF-10): 1, 2, 4, 8, 15, 15... segundos. Al conectar se llama
/// <see cref="Reiniciar"/> y vuelve a empezar desde 1 s.
/// </summary>
public sealed class PoliticaReintentos(OpcionesLectorBaston opciones)
{
    private int _ciclosFallidos;

    /// <summary>La espera después de <paramref name="ciclosFallidos"/> ciclos sin éxito (0 = el primero). Pura.</summary>
    public TimeSpan SiguienteEspera(int ciclosFallidos)
    {
        var esperas = opciones.Esperas;
        return esperas[Math.Clamp(ciclosFallidos, 0, esperas.Count - 1)];
    }

    /// <summary>La espera que toca ahora y avanza al ciclo siguiente.</summary>
    public TimeSpan SiguienteEspera() => SiguienteEspera(_ciclosFallidos++);

    public void Reiniciar() => _ciclosFallidos = 0;
}
