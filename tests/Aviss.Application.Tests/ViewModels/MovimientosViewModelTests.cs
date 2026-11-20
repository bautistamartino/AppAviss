using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Application.Tests.ViewModels.Soporte;

namespace Aviss.Application.Tests.ViewModels;

public class MovimientosViewModelTests
{
    private sealed class Banco
    {
        public FakeCampoService Campo { get; } = new();
        public SesionTrabajo Sesion { get; } = new();
        public MovimientosViewModel Vm { get; }

        public Banco()
        {
            Sesion.Empezar(Datos.Vet, Datos.Rodeo());
            Vm = new MovimientosViewModel(Campo, Sesion);
        }
    }

    [Fact]
    public async Task Carga_los_movimientos_del_rodeo_en_trabajo()
    {
        var b = new Banco();
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Tacto), Datos.Movimiento(TipoMovimiento.Marca), Datos.Movimiento(TipoMovimiento.Cierre)];

        await b.Vm.CargarAsync();

        Assert.Equal(3, b.Vm.Movimientos.Count);
        Assert.True(b.Vm.HayMovimientos);
        Assert.Equal(["Tacto", "Marca", "Cierre de manga"], b.Vm.Movimientos.Select(m => m.Tipo));
    }

    [Fact]
    public async Task Sin_movimientos_la_lista_queda_vacia()
    {
        var b = new Banco();

        await b.Vm.CargarAsync();

        Assert.False(b.Vm.HayMovimientos);
    }

    [Theory]
    [InlineData(TipoMovimiento.Tacto, TipoDeshacer.Tacto)]
    [InlineData(TipoMovimiento.Marca, TipoDeshacer.Marca)]
    [InlineData(TipoMovimiento.Cierre, TipoDeshacer.Cierre)]
    public async Task Deshacer_manda_el_tipo_y_el_id_del_registro_y_vuelve_a_leer_la_lista(TipoMovimiento tipo, TipoDeshacer esperado)
    {
        var b = new Banco();
        var movimiento = Datos.Movimiento(tipo);
        b.Campo.Movimientos = [movimiento];
        await b.Vm.CargarAsync();

        await b.Vm.DeshacerCommand.ExecuteAsync(b.Vm.Movimientos[0]);

        var cmd = Assert.Single(b.Campo.Deshaceres);
        Assert.Equal(esperado, cmd.Tipo);
        Assert.Equal(movimiento.RegistroId, cmd.Id);   // en un alta es el id del animal
        Assert.Equal(Datos.Vet.Id, cmd.UsuarioId);
        Assert.Equal(2, b.Campo.ConsultasDeMovimientos);   // recargó
        Assert.True(b.Vm.HayMensajeInfo);
    }

    [Fact]
    public async Task Un_deshacer_rechazado_muestra_el_motivo_y_no_recarga()
    {
        var b = new Banco();
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Tacto)];
        b.Campo.OnDeshacer = _ => Resultado.Error<ResultadoDeshacerDto>(CodigoError.TactoNoEsElUltimo, "Ya no es lo último.");
        await b.Vm.CargarAsync();

        await b.Vm.DeshacerCommand.ExecuteAsync(b.Vm.Movimientos[0]);

        Assert.Equal("Ya no es lo último.", b.Vm.MensajeError);
        Assert.Equal(1, b.Campo.ConsultasDeMovimientos);
    }

    [Fact]
    public async Task Lo_que_no_se_puede_deshacer_no_llama_al_servicio()
    {
        var b = new Banco();
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Tacto, puedeDeshacer: false)];
        await b.Vm.CargarAsync();

        await b.Vm.DeshacerCommand.ExecuteAsync(b.Vm.Movimientos[0]);

        Assert.Empty(b.Campo.Deshaceres);
    }

    [Fact]
    public async Task Los_cierres_muestran_las_dos_cifras_cuando_difieren()
    {
        var b = new Banco();
        b.Campo.Movimientos =
        [
            Datos.Movimiento(TipoMovimiento.Cierre, cantidadAviss: 14, cantidadConfirmada: 14, contadorActual: 13),
            Datos.Movimiento(TipoMovimiento.Cierre, cantidadAviss: 10, cantidadConfirmada: 10, contadorActual: 10)
        ];

        await b.Vm.CargarAsync();

        Assert.Equal("Al cerrar: 14 · Hoy: 13", b.Vm.Movimientos[0].TextoCierre);
        Assert.True(b.Vm.Movimientos[0].HayTextoCierre);
        Assert.Equal("", b.Vm.Movimientos[1].TextoCierre);   // no difieren: no se repite
    }

    [Fact]
    public async Task Los_anulados_se_marcan_como_tales()
    {
        var b = new Banco();
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Marca, puedeDeshacer: false, anulado: true)];

        await b.Vm.CargarAsync();

        Assert.True(b.Vm.Movimientos[0].Anulado);
    }

    [Fact]
    public async Task Una_falla_al_cargar_muestra_un_mensaje_fijo()
    {
        var b = new Banco();
        var vm = new MovimientosViewModel(new Fallo(), b.Sesion);

        await vm.CargarAsync();

        Assert.Equal(MovimientosViewModel.MensajeFalla, vm.MensajeError);
    }

    private sealed class Fallo : FakeCampoService
    {
        public override Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }
}
