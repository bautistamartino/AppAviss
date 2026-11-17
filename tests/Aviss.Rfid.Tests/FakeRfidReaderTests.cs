using Aviss.Application.Puertos;

namespace Aviss.Rfid.Tests;

public class FakeRfidReaderTests
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Entrega_las_lecturas_grabadas_en_orden()
    {
        var lector = new FakeRfidReader(["111", "222", "333"]);
        using var cts = new CancellationTokenSource(Espera);

        var recibidas = new List<string>();
        await foreach (var tag in lector.Lecturas(cts.Token))
        {
            recibidas.Add(tag.Codigo);
            if (recibidas.Count == 3) break;
        }

        Assert.Equal(["111", "222", "333"], recibidas);
    }

    [Fact]
    public async Task Emitir_entrega_el_codigo_con_la_hora_del_reloj()
    {
        var hora = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var lector = new FakeRfidReader(ahoraUtc: () => hora);
        using var cts = new CancellationTokenSource(Espera);

        lector.Emitir("555");
        await using var e = lector.Lecturas(cts.Token).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());

        Assert.Equal("555", e.Current.Codigo);
        Assert.Equal(hora, e.Current.FechaHoraUtc);
    }

    [Fact]
    public async Task Entrega_lo_que_se_emite_despues_de_empezar_a_escuchar()
    {
        var lector = new FakeRfidReader();
        using var cts = new CancellationTokenSource(Espera);
        await using var e = lector.Lecturas(cts.Token).GetAsyncEnumerator();

        var siguiente = e.MoveNextAsync().AsTask();
        Assert.False(siguiente.IsCompleted);   // nada para leer todavía

        lector.Emitir("777");

        Assert.True(await siguiente);
        Assert.Equal("777", e.Current.Codigo);
    }

    [Fact]
    public async Task Al_cancelar_la_secuencia_termina_sin_lanzar()
    {
        var lector = new FakeRfidReader();
        using var cts = new CancellationTokenSource();
        await using var e = lector.Lecturas(cts.Token).GetAsyncEnumerator();

        var siguiente = e.MoveNextAsync().AsTask();
        cts.Cancel();

        Assert.False(await siguiente.WaitAsync(Espera));
    }

    [Fact]
    public async Task Conectar_y_desconectar_cambian_el_estado()
    {
        var lector = new FakeRfidReader();
        Assert.Equal(EstadoConexionRfid.Desconectado, lector.Estado);

        await lector.ConectarAsync();
        Assert.Equal(EstadoConexionRfid.Conectado, lector.Estado);

        await lector.DesconectarAsync();
        Assert.Equal(EstadoConexionRfid.Desconectado, lector.Estado);
    }

    [Fact]
    public async Task LectorNoDisponible_no_entrega_nada_y_termina_al_cancelar()
    {
        var lector = new LectorNoDisponible();
        using var cts = new CancellationTokenSource();
        await using var e = lector.Lecturas(cts.Token).GetAsyncEnumerator();

        var siguiente = e.MoveNextAsync().AsTask();
        await Task.Delay(50);
        Assert.False(siguiente.IsCompleted);

        cts.Cancel();
        Assert.False(await siguiente.WaitAsync(Espera));
    }
}
