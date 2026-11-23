using System.Diagnostics;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// RF-37: con 20.000 vacas y más de 100.000 registros de campo en la base, buscar una vaca y armar su ficha, guardar
/// una marca o un tacto, y calcular el avance y el contador tardan lo que pide la spec. Se miden en el equipo de
/// desarrollo (no en la tablet). Se pueden excluir con <c>--filter "Categoria!=Rendimiento"</c>.
/// Cada medición se repite tres veces sobre vacas distintas y se toma la mejor: así un pico de carga de la PC (otro
/// proceso, el recolector de basura) no hace fallar el test, pero un problema real de índices sí.
/// </summary>
[Trait("Categoria", "Rendimiento")]
[Collection("Rendimiento")] // las pruebas de volumen corren de a una: en paralelo se pisan el CPU y el disco y los tiempos se falsean
public sealed class RendimientoCampoTests : IDisposable
{
    private const int Animales = 20_000;
    private const int TactosViejos = 70_000;
    private const int MarcasViejas = 30_000;   // 3 mangas cerradas de 10.000 vacas
    private const int MarcasDeHoy = 5_000;     // la manga abierta de hoy
    private const int TactosDeHoy = 2_000;

    private readonly EscenarioConServicios _e = new();
    private readonly List<Guid> _ids = new(Animales);
    private Guid _mangaAbierta;

    public RendimientoCampoTests() => CargarVolumen();
    public void Dispose() => _e.Dispose();

    /// <summary>Carga masiva por SQL crudo en UNA transacción con comandos preparados (tarda segundos).</summary>
    private void CargarVolumen()
    {
        var c = _e.Base.Cruda;
        var ahora = _e.Reloj.UtcAhora;
        var inicio = ahora.AddDays(-400);

        // Las mangas y sus cierres son pocos: se crean con los atajos. Tres cerradas (pasado) y una abierta hoy.
        var mangasViejas = new Guid[3];
        for (var k = 0; k < 3; k++)
        {
            var fechaManga = ahora.AddDays(-100 + k);
            mangasViejas[k] = _e.Base.Manga(_e.RodeoId, k + 1, _e.UsuarioId, creadaEn: fechaManga);
            _e.Base.CierreManga(mangasViejas[k], _e.UsuarioId, aviss: 10_000, fechaHora: fechaManga.AddHours(5));
        }
        _mangaAbierta = _e.Base.Manga(_e.RodeoId, 4, _e.UsuarioId, creadaEn: ahora.AddHours(-6));

        using var tx = c.BeginTransaction();

        using var animal = c.CreateCommand();
        animal.Transaction = tx;
        animal.CommandText = @"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, EstadoReproductivoId, Origen, Anulado, DatosOriginalesJson, CreadoEnUtc, ActualizadoEnUtc)
                               VALUES ($id, $r, $c, $e, 'Importado', 0, $j, $f, $f)";
        var pId = animal.Parameters.Add("$id", SqliteType.Text);
        var pC = animal.Parameters.Add("$c", SqliteType.Text);
        animal.Parameters.AddWithValue("$r", _e.RodeoId);
        animal.Parameters.AddWithValue("$e", _e.EstadoSeca);
        animal.Parameters.AddWithValue("$j", """{"LACT":"2","DEL":"100","FPART":" 5/11/26","VINS":"2","UTORO":"X","FAPAR":"20/12/26"}""");
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

        long orden = 0; // un solo orden para tactos y marcas, como en la app

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
        void InsertarTacto(int animalIdx, DateTime fecha)
        {
            tId.Value = EscenarioConServicios.Texto(Guid.NewGuid());
            tA.Value = EscenarioConServicios.Texto(_ids[animalIdx]);
            tFh.Value = BaseDeDatosDePrueba.Fecha(fecha);
            tO.Value = ++orden;
            tacto.ExecuteNonQuery();
        }

        using var marca = c.CreateCommand();
        marca.Transaction = tx;
        marca.CommandText = @"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion, UsuarioId, DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                              VALUES ($id, $a, $m, $fh, $o, $u, $d, 0, $fh, $fh)";
        var mId = marca.Parameters.Add("$id", SqliteType.Text);
        var mA = marca.Parameters.Add("$a", SqliteType.Text);
        var mM = marca.Parameters.Add("$m", SqliteType.Text);
        var mFh = marca.Parameters.Add("$fh", SqliteType.Text);
        var mO = marca.Parameters.Add("$o", SqliteType.Integer);
        marca.Parameters.AddWithValue("$u", _e.UsuarioId);
        marca.Parameters.AddWithValue("$d", Guid.NewGuid());
        marca.Prepare();
        void InsertarMarca(int animalIdx, Guid manga, DateTime fecha)
        {
            mId.Value = EscenarioConServicios.Texto(Guid.NewGuid());
            mA.Value = EscenarioConServicios.Texto(_ids[animalIdx]);
            mM.Value = EscenarioConServicios.Texto(manga);
            mFh.Value = BaseDeDatosDePrueba.Fecha(fecha);
            mO.Value = ++orden;
            marca.ExecuteNonQuery();
        }

        // Tactos viejos (todo en el pasado respecto del reloj de prueba), repartidos entre todas las vacas.
        for (var i = 0; i < TactosViejos; i++)
            InsertarTacto(i % Animales, inicio.AddMinutes(i));
        // Marcas viejas: 3 mangas cerradas, cada una con las vacas 0..9.999.
        for (var i = 0; i < MarcasViejas; i++)
            InsertarMarca(i % 10_000, mangasViejas[i / 10_000], ahora.AddDays(-100 + i / 10_000).AddSeconds(i % 10_000));
        // Hoy: 5.000 vacas marcadas en la manga abierta y 2.000 con un tacto de hoy.
        for (var i = 0; i < MarcasDeHoy; i++)
            InsertarMarca(i, _mangaAbierta, ahora.AddHours(-5).AddSeconds(i));
        for (var i = 0; i < TactosDeHoy; i++)
            InsertarTacto(MarcasDeHoy + i, ahora.AddHours(-4).AddSeconds(i));

        tx.Commit();
    }

    /// <summary>Corre la medición tres veces (tras un calentamiento) y devuelve el mejor tiempo en milisegundos.</summary>
    private static async Task<long> MejorDeTresAsync(Func<int, Task> operacion)
    {
        await operacion(0); // calentamiento: compila el modelo y abre la conexión
        var mejor = long.MaxValue;
        for (var i = 1; i <= 3; i++)
        {
            var reloj = Stopwatch.StartNew();
            await operacion(i);
            reloj.Stop();
            mejor = Math.Min(mejor, reloj.ElapsedMilliseconds);
        }
        return mejor;
    }

    [Fact]
    public async Task Buscar_una_vaca_por_caravana_marcarla_y_armar_su_ficha_tarda_menos_de_300_ms()
    {
        // Vacas 12.000 en adelante: tienen tactos viejos pero no marca ni tacto de hoy.
        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(_e.RodeoId, (112_000 + i).ToString(), _e.UsuarioId));
            Assert.True(r.EsCorrecto, r.Mensaje);
            Assert.Equal(TipoResultadoLectura.Marcada, r.Valor.Tipo);
            Assert.NotEmpty(r.Valor.Ficha!.Historial);
        });
        Assert.True(ms < 300, $"Buscar, marcar y armar la ficha tardó {ms} ms");
    }

    [Fact]
    public async Task Pedir_la_ficha_de_una_vaca_con_historial_tarda_menos_de_300_ms()
    {
        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.ObtenerFichaAsync(_ids[15_000 + i]);
            Assert.True(r.EsCorrecto, r.Mensaje);
        });
        Assert.True(ms < 300, $"Armar la ficha tardó {ms} ms");
    }

    [Fact]
    public async Task Registrar_un_tacto_tarda_menos_de_300_ms()
    {
        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.RegistrarTactoAsync(
                new RegistrarTactoCommand(_ids[16_000 + i], _e.EstadoPrenada, null, null, _e.UsuarioId));
            Assert.True(r.EsCorrecto, r.Mensaje);
        });
        Assert.True(ms < 300, $"Registrar el tacto tardó {ms} ms");
    }

    [Fact]
    public async Task Calcular_el_avance_y_el_contador_tarda_menos_de_200_ms()
    {
        EstadoTrabajoDto? estado = null;
        var ms = await MejorDeTresAsync(async _ =>
        {
            var r = await _e.Campo.ObtenerEstadoTrabajoAsync(_e.RodeoId);
            Assert.True(r.EsCorrecto, r.Mensaje);
            estado = r.Valor;
        });

        // De paso se comprueba que los números son los que se cargaron.
        Assert.Equal(Animales, estado!.Avance.TotalVacas);
        Assert.Equal(MarcasDeHoy + TactosDeHoy, estado.Avance.Procesadas);
        Assert.Equal(MarcasDeHoy, estado.Contador);
        Assert.Equal(4, estado.NumeroManga);
        Assert.True(estado.MangaAbierta);
        Assert.True(ms < 200, $"Calcular el avance y el contador tardó {ms} ms");
    }

    // Spec 004: poner en espera, listar las esperas y resolver una (con un tacto) cumplen lo mismo que RF-37 (menos de 300 ms).

    [Fact]
    public async Task Poner_en_espera_a_una_vaca_tarda_menos_de_300_ms()
    {
        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(_ids[17_000 + i], _e.UsuarioId));
            Assert.True(r.EsCorrecto, r.Mensaje);
        });
        Assert.True(ms < 300, $"Poner en espera tardó {ms} ms");
    }

    [Fact]
    public async Task Listar_las_esperas_con_el_volumen_completo_tarda_menos_de_300_ms()
    {
        for (var i = 0; i < 4; i++)
            Assert.True((await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(_ids[17_500 + i], _e.UsuarioId))).EsCorrecto);

        var ms = await MejorDeTresAsync(async _ =>
        {
            var lista = await _e.Campo.ListarEsperasAsync(_e.RodeoId);
            Assert.Equal(4, lista.Count);
        });
        Assert.True(ms < 300, $"Listar las esperas tardó {ms} ms");
    }

    [Fact]
    public async Task Resolver_una_espera_guardando_un_tacto_tarda_menos_de_300_ms()
    {
        for (var i = 0; i <= 3; i++)
            Assert.True((await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(_ids[18_000 + i], _e.UsuarioId))).EsCorrecto);

        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.RegistrarTactoAsync(
                new RegistrarTactoCommand(_ids[18_000 + i], _e.EstadoPrenada, null, null, _e.UsuarioId));
            Assert.True(r.EsCorrecto, r.Mensaje);
        });
        Assert.True(ms < 300, $"Resolver la espera con un tacto tardó {ms} ms");
        Assert.Equal(0, _e.Cuenta("Espera", $"Estado = 'Pendiente' AND AnimalId IN ('{EscenarioConServicios.Texto(_ids[18_000])}','{EscenarioConServicios.Texto(_ids[18_001])}')"));
    }

    [Fact]
    public async Task Guardar_un_rechazo_tarda_menos_de_300_ms()
    {
        var ms = await MejorDeTresAsync(async i =>
        {
            var r = await _e.Campo.RegistrarSoloEventoAsync(new RegistrarSoloEventoCommand(_ids[19_000 + i], EventosCampo.Rechazo, "pata", _e.UsuarioId));
            Assert.True(r.EsCorrecto, r.Mensaje);
        });
        Assert.True(ms < 300, $"Guardar el rechazo tardó {ms} ms");
    }

    [Fact]
    public async Task Los_ultimos_movimientos_con_el_volumen_completo_tardan_menos_de_300_ms()
    {
        var ms = await MejorDeTresAsync(async _ =>
        {
            var lista = await _e.Campo.ListarMovimientosAsync(_e.RodeoId);
            Assert.Equal(50, lista.Count);
        });
        Assert.True(ms < 300, $"Listar los movimientos tardó {ms} ms");
    }
}
