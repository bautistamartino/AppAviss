using System.Diagnostics;
using Aviss.Application.Comandos;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Volumen de la spec: con 20.000 animales y 100.000 tactos, buscar por caravana tarda menos de 100 ms y registrar
/// un tacto menos de 300 ms. Se miden en el equipo de desarrollo (no en la tablet). Se pueden excluir con
/// <c>--filter "Categoria!=Rendimiento"</c>.
/// </summary>
[Trait("Categoria", "Rendimiento")]
public sealed class RendimientoTests : IDisposable
{
    private const int Animales = 20_000;
    private const int Tactos = 100_000;

    private readonly EscenarioConServicios _e = new();
    private readonly List<Guid> _ids = new(Animales);

    public RendimientoTests() => CargarVolumen();
    public void Dispose() => _e.Dispose();

    /// <summary>Carga masiva por SQL crudo en UNA transacción, con comandos preparados (así tarda segundos).</summary>
    private void CargarVolumen()
    {
        var c = _e.Base.Cruda;
        using var tx = c.BeginTransaction();
        var inicio = _e.Reloj.UtcAhora.AddDays(-400);

        using var animal = c.CreateCommand();
        animal.Transaction = tx;
        animal.CommandText = @"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, EstadoReproductivoId, Origen, Anulado, DatosOriginalesJson, CreadoEnUtc, ActualizadoEnUtc)
                               VALUES ($id, $r, $c, $e, 'Importado', 0, '{}', $f, $f)";
        var pId = animal.Parameters.Add("$id", SqliteType.Text);
        var pC = animal.Parameters.Add("$c", SqliteType.Text);
        animal.Parameters.AddWithValue("$r", _e.RodeoId);
        animal.Parameters.AddWithValue("$e", _e.EstadoSeca);
        animal.Parameters.AddWithValue("$f", BaseDeDatosDePrueba.Fecha(inicio));
        animal.Prepare();
        for (var i = 0; i < Animales; i++)
        {
            var id = Guid.NewGuid();
            _ids.Add(id);
            pId.Value = EscenarioConServicios.Texto(id);
            pC.Value = (100000 + i).ToString();
            animal.ExecuteNonQuery();
        }

        using var tacto = c.CreateCommand();
        tacto.Transaction = tx;
        tacto.CommandText = @"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, UsuarioId, DispositivoId, Tipo, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                              VALUES ($id, $a, $fh, $o, $e, $u, $d, 'Normal', 0, $fh, $fh)";
        var tId = tacto.Parameters.Add("$id", SqliteType.Text);
        var tA = tacto.Parameters.Add("$a", SqliteType.Text);
        var tFh = tacto.Parameters.Add("$fh", SqliteType.Text);
        var tO = tacto.Parameters.Add("$o", SqliteType.Integer);
        tacto.Parameters.AddWithValue("$e", _e.EstadoSeca);
        tacto.Parameters.AddWithValue("$u", _e.UsuarioId);
        tacto.Parameters.AddWithValue("$d", Guid.NewGuid());
        tacto.Prepare();
        for (var i = 0; i < Tactos; i++)
        {
            tId.Value = EscenarioConServicios.Texto(Guid.NewGuid());
            tA.Value = EscenarioConServicios.Texto(_ids[i % Animales]);
            tFh.Value = BaseDeDatosDePrueba.Fecha(inicio.AddMinutes(i)); // todo en el pasado respecto del reloj de prueba
            tO.Value = i + 1;
            tacto.ExecuteNonQuery();
        }
        tx.Commit();
    }

    [Fact]
    public async Task Buscar_un_animal_por_caravana_tarda_menos_de_100_ms()
    {
        var caravana = Caravana.Crear("115000");

        // Calentamiento: primera consulta (compila el modelo y abre la conexión).
        await _e.Servicio<IAnimalRepository>().ObtenerPorCaravanaAsync(_e.RodeoId, Caravana.Crear("100000"));

        var repo = _e.Servicio<IAnimalRepository>();
        var reloj = Stopwatch.StartNew();
        var animal = await repo.ObtenerPorCaravanaAsync(_e.RodeoId, caravana);
        reloj.Stop();

        Assert.NotNull(animal);
        Assert.True(reloj.ElapsedMilliseconds < 100, $"La búsqueda tardó {reloj.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Registrar_un_tacto_tarda_menos_de_300_ms()
    {
        // Calentamiento con otro animal.
        await _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(
            new RegistrarTactoCommand(_ids[1], _e.EstadoPrenada, null, null, _e.UsuarioId));

        var servicio = _e.Servicio<ITrabajoCampoService>();
        var reloj = Stopwatch.StartNew();
        var r = await servicio.RegistrarTactoAsync(new RegistrarTactoCommand(_ids[7], _e.EstadoPrenada, 60, null, _e.UsuarioId));
        reloj.Stop();

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.True(reloj.ElapsedMilliseconds < 300, $"Registrar el tacto tardó {reloj.ElapsedMilliseconds} ms");
    }
}
