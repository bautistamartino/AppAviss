using System.Diagnostics;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// RF-26: con 20.000 vacas y sus registros, generar, validar y guardar el archivo de vuelta tarda menos de 15 s en cada formato
/// (en la PC de desarrollo, no en la tablet). Se excluye con <c>--filter "Categoria!=Rendimiento"</c>.
/// </summary>
[Trait("Categoria", "Rendimiento")]
[Collection("Rendimiento")] // las pruebas de volumen corren de a una: en paralelo se pisan el CPU y el disco y los tiempos se falsean
public sealed class ExportacionRendimientoTests : IDisposable
{
    private const int Vacas = 20_000;
    private const int ConRegistro = 15_000;

    private readonly EscenarioExportacion _e = new();
    private readonly Guid _rodeo;

    public ExportacionRendimientoTests()
    {
        var caravanas = Enumerable.Range(0, Vacas).Select(i => (100000 + i).ToString()).ToList();
        _rodeo = _e.Rodeo(EscenarioExportacion.XlsOriginal(caravanas), "grande.xls");

        // Carga masiva por SQL crudo en UNA transacción con comandos preparados (tarda segundos).
        var c = _e.Base.Cruda;
        var ahora = BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora);
        using var tx = c.BeginTransaction();
        using var animal = c.CreateCommand();
        animal.Transaction = tx;
        animal.CommandText = @"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, Origen, Anulado, DatosOriginalesJson, CreadoEnUtc, ActualizadoEnUtc)
                               VALUES ($id, $r, $c, 'Importado', 0, $j, $f, $f)";
        var pId = animal.Parameters.Add("$id", SqliteType.Text);
        var pC = animal.Parameters.Add("$c", SqliteType.Text);
        var pJ = animal.Parameters.Add("$j", SqliteType.Text);
        animal.Parameters.AddWithValue("$r", _rodeo);
        animal.Parameters.AddWithValue("$f", ahora);
        animal.Prepare();

        using var tacto = c.CreateCommand();
        tacto.Transaction = tx;
        tacto.CommandText = @"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, Tipo, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                              VALUES ($id, $a, $f, $o, $e, 'Normal', 0, $f, $f)";
        var tId = tacto.Parameters.Add("$id", SqliteType.Text);
        var tA = tacto.Parameters.Add("$a", SqliteType.Text);
        var tO = tacto.Parameters.Add("$o", SqliteType.Integer);
        tacto.Parameters.AddWithValue("$e", _e.EstadoSeca);
        tacto.Parameters.AddWithValue("$f", ahora);
        tacto.Prepare();

        for (var i = 0; i < Vacas; i++)
        {
            var id = Guid.NewGuid();
            pId.Value = EscenarioConServicios.Texto(id);
            pC.Value = caravanas[i];
            pJ.Value = $"{{\"ID\":\"{caravanas[i]}\",\"CVET\":\"1\"}}";
            animal.ExecuteNonQuery();
            if (i >= ConRegistro) continue;
            tId.Value = EscenarioConServicios.Texto(Guid.NewGuid());
            tA.Value = EscenarioConServicios.Texto(id);
            tO.Value = i + 1;
            tacto.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Dispose() => _e.Dispose();

    [Theory]
    [InlineData(FormatoExportacion.Csv)]
    [InlineData(FormatoExportacion.Xls)]
    public async Task Veinte_mil_vacas_con_registros_se_exportan_en_menos_de_15_segundos(FormatoExportacion formato)
    {
        var reloj = Stopwatch.StartNew();
        var r = await _e.Servicio.ExportarAsync(_rodeo, formato);
        reloj.Stop();

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(Vacas, r.Valor.Filas);
        Assert.Equal(ConRegistro, r.Valor.VacasConEvento);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(15), $"Exportar {formato} tardó {reloj.Elapsed.TotalSeconds:F1} s (tope: 15 s)");
    }
}
