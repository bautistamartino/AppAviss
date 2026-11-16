using Aviss.Infrastructure.Arranque;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests.Soporte;

public enum ModoMigrador
{
    /// <summary>Agrega la tabla "v2" y registra la migración falsa: la actualización sale bien.</summary>
    Exito,
    /// <summary>Agrega la tabla "v2" y recién después lanza una excepción (falla a mitad).</summary>
    FallaAMitad,
    /// <summary>Lanza antes de tocar nada.</summary>
    FallaSinCambios
}

/// <summary>
/// Con una sola migración real no se puede probar una base de estructura vieja con EF: este migrador finge que existe una
/// migración más ("Falsa") que la base todavía no tiene, y la aplica (o falla) a mano sobre la base real de prueba.
/// </summary>
public sealed class MigradorFalso : IMigrador
{
    public const string IdFalsa = "20270101000000_Falsa";

    private readonly MigradorEf _real = new();

    public ModoMigrador Modo { get; set; } = ModoMigrador.Exito;
    public int Llamadas { get; private set; }

    /// <summary>Se ejecuta justo antes de "migrar" (para mirar el estado en ese instante o sabotear algo).</summary>
    public Action? AlAplicar { get; set; }

    public IReadOnlyList<string> Conocidas() => _real.Conocidas().Append(IdFalsa).ToList();

    public IReadOnlyList<string> Aplicadas(string rutaBase) => _real.Aplicadas(rutaBase);

    public Task AplicarAsync(string rutaBase, CancellationToken ct = default)
    {
        Llamadas++;
        AlAplicar?.Invoke();
        if (Modo == ModoMigrador.FallaSinCambios)
            throw new InvalidOperationException("falla simulada");

        using (var cx = new SqliteConnection($"Data Source={rutaBase};Pooling=False"))
        {
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText = "CREATE TABLE v2 (x INTEGER)";
            cmd.ExecuteNonQuery();
            if (Modo == ModoMigrador.Exito)
            {
                cmd.CommandText = $"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('{IdFalsa}', '10.0.0')";
                cmd.ExecuteNonQuery();
            }
        }
        if (Modo == ModoMigrador.FallaAMitad)
            throw new InvalidOperationException("falla simulada a mitad");
        return Task.CompletedTask;
    }
}
