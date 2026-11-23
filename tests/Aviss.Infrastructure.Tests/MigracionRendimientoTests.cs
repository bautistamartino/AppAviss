using System.Diagnostics;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 006, RF-11 (volumen): la migración "QuitarVeterinario" con 20.000 vacas y más de 100.000 registros (tactos, marcas de
/// pasada e historial) termina en menos de 15 segundos (el mismo tope que confirmar una importación de 20.000 filas, en la PC
/// de desarrollo y no en la tablet) y no cambia ningún dato. Se excluye con <c>--filter "Categoria!=Rendimiento"</c>.
/// La copia previa que hace el arranque antes de migrar no cuenta para el tope. Una versión con 300 vacas del mismo generador
/// corre siempre en <see cref="MigracionQuitarVeterinarioTests"/>.
/// </summary>
[Trait("Categoria", "Rendimiento")]
[Collection("Rendimiento")] // las pruebas de volumen corren de a una: en paralelo se pisan el CPU y el disco
public sealed class MigracionRendimientoTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new(migrar: false);
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Migrar_20000_vacas_y_mas_de_100000_registros_tarda_menos_de_15_segundos_y_no_cambia_nada()
    {
        EsquemaAnterior.MigrarHasta(_db, "EsperaYRechazo");
        var (vacas, registros) = VolumenAnterior.Poblar(_db, 20_000);
        Assert.Equal(20_000, vacas);
        Assert.True(registros > 100_000, $"Se esperaban más de 100.000 registros y hay {registros}.");

        var antes = EsquemaAnterior.Instantanea(_db);
        _db.CerrarCruda();

        var reloj = Stopwatch.StartNew();
        EsquemaAnterior.MigrarHasta(_db, null);
        reloj.Stop();

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(15), $"La migración tardó {reloj.Elapsed.TotalSeconds:F1} s (tope: 15 s).");

        // Los conteos y todas las filas coinciden.
        var despues = EsquemaAnterior.Instantanea(_db);
        foreach (var (tabla, filas) in antes)
        {
            Assert.Equal(filas.Count, despues[tabla].Count);
            Assert.True(filas.SequenceEqual(despues[tabla]), $"La tabla {tabla} cambió al migrar.");
        }
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }
}
