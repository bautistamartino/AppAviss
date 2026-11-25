namespace Aviss.Rfid;

/// <summary>
/// Valores provisorios del lector del bastón (spec 008). Viven en un solo lugar para ajustarlos después de probar en la
/// tablet con el bastón real.
/// </summary>
public sealed record OpcionesLectorBaston
{
    /// <summary>RF-21: el mismo EID dentro de esta ventana se ignora.</summary>
    public TimeSpan VentanaRepetidas { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>RF-8: tiempo máximo de cada intento de conexión (Android espera 11 o 60 s: acá se corta antes).</summary>
    public TimeSpan TimeoutIntento { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>RF-10: esperas entre ciclos fallidos; la última es el tope y se mantiene.</summary>
    public IReadOnlyList<TimeSpan> Esperas { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    /// <summary>RF-14: espera antes de reintentar cuando se perdió una conexión que estaba establecida.</summary>
    public TimeSpan EsperaTrasPerdida { get; init; } = TimeSpan.FromSeconds(1);
}
