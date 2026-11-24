using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

/// <summary>
/// Spec 010 (RF-13, RF-14, RF-16, RF-17): el ViewModel trata un EID como cualquier otro código leído. El código llega tal cual al
/// servicio (el servicio normaliza los espacios), la búsqueda manual solo recorta los extremos y la ficha muestra UNA fila EID.
/// </summary>
public class TrabajoViewModelEidTests
{
    private const string Eid = "999000458013169";

    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public FakeLector Lector { get; } = new();
        public TrabajoViewModel Vm { get; }

        public Banco()
        {
            var sesion = new SesionTrabajo();
            sesion.Empezar(Datos.Rodeo());
            Campo.Estados = [new EventoCampoDto(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA", true, true)];
            var prefs = new FakePreferencias { ControlPorMangaPorDefecto = true };
            Vm = new TrabajoViewModel(Campo, Lector, prefs, new FakeAvisos(), new FakePantalla(), new FakeTemporizador(), new FakeNavegacion(), sesion, Lector);
        }

        /// <summary>Una vaca con EID: tanto su número como su EID (con y sin espacios) devuelven la misma lectura marcada.</summary>
        public Guid VacaConEid(string[] codigos, string? eid = Eid, IReadOnlyList<CampoFichaDto>? extras = null)
        {
            var animal = Guid.NewGuid();
            var ficha = Datos.Ficha(animal, "30092", eid, cvet: "PRE") with { Extras = extras };
            foreach (var codigo in codigos)
                Campo.Lecturas[codigo] = Datos.Marcada(animal, "30092") with { Ficha = ficha, CodigoLeido = codigo };
            Campo.Fichas[animal] = ficha;
            Campo.Estado = Datos.EstadoTrabajo(manga: 1, abierta: true);
            return animal;
        }
    }

    [Fact]
    public async Task El_EID_que_entrega_el_lector_llega_tal_cual_al_servicio_y_abre_la_ficha_del_animal()
    {
        var b = new Banco();
        var animal = b.VacaConEid([Eid]);

        await b.Vm.EncolarLecturaAsync(Eid);

        Assert.Equal([Eid], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal(EtapaTrabajo.Ficha, b.Vm.Etapa);
    }

    [Fact]
    public async Task El_EID_con_espacios_llega_con_sus_espacios_internos_y_recortado_en_los_extremos()
    {
        var b = new Banco();
        var animal = b.VacaConEid(["999 000 458013169"]);

        await b.Vm.EncolarLecturaAsync("  999 000 458013169 ");

        // Los espacios de adentro los resuelve el servicio (IdElectronico.TryCrear); el ViewModel no recorta de más.
        Assert.Equal(["999 000 458013169"], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
    }

    [Fact]
    public async Task La_busqueda_manual_por_EID_envia_el_codigo_recortado_y_abre_la_misma_ficha()
    {
        var b = new Banco();
        var animal = b.VacaConEid([Eid]);
        b.Vm.TextoBusqueda = $" {Eid} ";

        await b.Vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal([Eid], b.Campo.LecturasProcesadas.Select(l => l.Codigo));
        Assert.Equal(animal, b.Vm.Ficha!.AnimalId);
        Assert.Equal("", b.Vm.TextoBusqueda);
    }

    [Fact]
    public async Task Un_EID_que_no_existe_muestra_caravana_no_encontrada_como_siempre()
    {
        var b = new Banco();

        await b.Vm.EncolarLecturaAsync("999000458999999");

        Assert.Equal(CuadroTrabajo.NoEncontrada, b.Vm.Cuadro);
        Assert.Equal("999000458999999", b.Vm.CodigoNoEncontrado);
        Assert.Empty(b.Campo.Tactos);
    }

    [Fact]
    public async Task La_ficha_con_EID_muestra_una_sola_fila_EID_con_el_valor_guardado()
    {
        var b = new Banco();
        // Extras ya sin la columna EID (ConstructorFicha la saca): la fila sale solo del valor guardado en el animal.
        b.VacaConEid([Eid], extras: [new CampoFichaDto("LECHE", "12")]);
        await b.Vm.EncolarLecturaAsync(Eid);

        var filas = b.Vm.FilasDatos;

        var fila = Assert.Single(filas, f => f.Nombre == "EID");
        Assert.Equal(Eid, fila.Valor);
        Assert.Contains(filas, f => f.Nombre == "LECHE");
    }

    [Fact]
    public async Task La_ficha_de_un_animal_sin_EID_no_muestra_la_fila_EID()
    {
        var b = new Banco();
        b.VacaConEid(["30092"], eid: null);
        await b.Vm.EncolarLecturaAsync("30092");

        Assert.DoesNotContain(b.Vm.FilasDatos, f => f.Nombre == "EID");
    }
}
