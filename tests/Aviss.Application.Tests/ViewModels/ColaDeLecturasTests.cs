using Aviss.App.ViewModels;

namespace Aviss.Application.Tests.ViewModels;

// La cola serializa el procesamiento de lecturas (una a la vez) y, con un cuadro abierto, guarda solo la última (RF-19).
public class ColaDeLecturasTests
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    /// <summary>Procesador falso: registra el orden y deja que el test decida cuándo termina cada lectura.</summary>
    private sealed class Procesador
    {
        public List<string> Procesadas { get; } = [];
        public int EnCurso { get; private set; }
        public int MaximoSimultaneas { get; private set; }
        public List<TaskCompletionSource> Pendientes { get; } = [];
        public bool Manual { get; set; }

        public async Task ProcesarAsync(Lectura lectura)
        {
            EnCurso++;
            MaximoSimultaneas = Math.Max(MaximoSimultaneas, EnCurso);
            Procesadas.Add(lectura.Codigo);
            if (Manual)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Pendientes.Add(tcs);
                await tcs.Task;
            }
            EnCurso--;
        }
    }

    [Fact]
    public async Task Procesa_una_lectura_a_la_vez_y_en_orden()
    {
        var p = new Procesador { Manual = true };
        var cola = new ColaDeLecturas(p.ProcesarAsync);

        var drenaje = cola.EncolarAsync("a");
        _ = cola.EncolarAsync("b");
        _ = cola.EncolarAsync("c");

        Assert.Equal(["a"], p.Procesadas);   // b y c esperan: no se procesan en paralelo

        p.Pendientes[0].SetResult();
        await EsperarAsync(() => p.Procesadas.Count == 2);
        p.Pendientes[1].SetResult();
        await EsperarAsync(() => p.Procesadas.Count == 3);
        p.Pendientes[2].SetResult();
        await drenaje.WaitAsync(Espera);

        Assert.Equal(["a", "b", "c"], p.Procesadas);   // dos vacas seguidas muy rápido: las dos se procesan
        Assert.Equal(1, p.MaximoSimultaneas);
    }

    [Fact]
    public async Task Con_un_cuadro_abierto_no_procesa_y_guarda_solo_la_ultima()
    {
        var p = new Procesador();
        var cola = new ColaDeLecturas(p.ProcesarAsync);

        cola.Bloquear();
        await cola.EncolarAsync("a");
        await cola.EncolarAsync("b");
        await cola.EncolarAsync("c");

        Assert.Empty(p.Procesadas);
        Assert.True(cola.HayPendiente);

        await cola.DesbloquearAsync();

        Assert.Equal(["c"], p.Procesadas);   // solo la última, al cerrar el cuadro
        Assert.False(cola.HayPendiente);
    }

    [Fact]
    public async Task Si_el_cuadro_se_abre_mientras_hay_cola_queda_solo_la_ultima()
    {
        var p = new Procesador { Manual = true };
        var cola = new ColaDeLecturas(p.ProcesarAsync);

        var drenaje = cola.EncolarAsync("a");
        _ = cola.EncolarAsync("b");
        _ = cola.EncolarAsync("c");

        cola.Bloquear();            // "a" abrió un cuadro (por ejemplo, no encontrada que pide alta)
        p.Pendientes[0].SetResult();
        await drenaje.WaitAsync(Espera);

        Assert.Equal(["a"], p.Procesadas);   // b y c no se procesan mientras el cuadro está abierto
        p.Manual = false;
        await cola.DesbloquearAsync();
        Assert.Equal(["a", "c"], p.Procesadas);
    }

    [Fact]
    public async Task Desbloquear_sin_pendiente_no_hace_nada()
    {
        var p = new Procesador();
        var cola = new ColaDeLecturas(p.ProcesarAsync);

        cola.Bloquear();
        await cola.DesbloquearAsync();

        Assert.Empty(p.Procesadas);
        Assert.False(cola.Bloqueada);
    }

    [Fact]
    public async Task Despues_de_desbloquear_vuelve_a_procesar_de_inmediato()
    {
        var p = new Procesador();
        var cola = new ColaDeLecturas(p.ProcesarAsync);
        cola.Bloquear();
        await cola.DesbloquearAsync();

        await cola.EncolarAsync("x");

        Assert.Equal(["x"], p.Procesadas);
    }

    [Fact]
    public async Task Un_error_del_procesador_no_traba_la_cola()
    {
        var llamadas = new List<string>();
        var cola = new ColaDeLecturas(l =>
        {
            llamadas.Add(l.Codigo);
            if (l.Codigo == "mala") throw new InvalidOperationException();
            return Task.CompletedTask;
        });

        await cola.EncolarAsync("mala");
        await cola.EncolarAsync("buena");

        Assert.Equal(["mala", "buena"], llamadas);
    }

    [Fact]
    public async Task Recuerda_si_la_lectura_fue_manual()
    {
        var recibidas = new List<Lectura>();
        var cola = new ColaDeLecturas(l => { recibidas.Add(l); return Task.CompletedTask; });

        await cola.EncolarAsync("123", esManual: true);

        Assert.True(recibidas[0].EsManual);
    }

    // Espera (con tope) a que se cumpla una condición: la continuación de una tarea puede correr en otro hilo.
    private static async Task EsperarAsync(Func<bool> condicion)
    {
        var limite = DateTime.UtcNow + Espera;
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(5);
        }
    }
}
