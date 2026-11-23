using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006 (RF-7, RF-8, RF-9, RF-15, RF-18): en una base NUEVA no existe el concepto de veterinario ni de tablet.
/// </summary>
public sealed class EsquemaSinVeterinarioTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    private static readonly string[] ColumnasProhibidas =
        { "UsuarioId", "AbiertaPorUsuarioId", "AnuladoPorUsuarioId", "ResueltaPorUsuarioId", "DispositivoId" };

    [Fact]
    public void No_existe_la_tabla_de_veterinarios()
    {
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usuario'"));
        // Tampoco quedan índices ni triggers del veterinario.
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE '%Usuario%' OR sql LIKE '%Usuario%'"));
    }

    [Fact]
    public void Ninguna_tabla_guarda_quien_ni_que_tablet()
    {
        var tablas = new List<string>();
        using (var cmd = _db.Cruda.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tablas.Add(r.GetString(0));
        }
        Assert.Contains("Tacto", tablas);

        foreach (var tabla in tablas)
        foreach (var columna in ColumnasProhibidas)
            Assert.Equal(0, _db.Contar($"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = $c", ("$c", columna)));
    }

    [Fact]
    public void Una_base_nueva_no_tiene_filas_de_Sin_asignar_ni_de_nadie()
    {
        // No hay tabla donde guardarlas; y el volcado de texto de toda la base no menciona "Sin asignar".
        Assert.DoesNotContain("Sin asignar", EsquemaAnterior.VolcadoDeTexto(_db));
    }
}
