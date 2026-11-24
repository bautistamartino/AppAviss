using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

// Spec 005 (RF-12, RF-25, RF-27, RF-30): Editar usa el mismo selector con pasos, con los datos del registro precargados.
public class MovimientosViewModelPasosTests
{
    private static readonly EventoCampoDto Correcta = new(Guid.Parse("44444444-0000-0000-0000-000000000001"), "Preñada fecha correcta", true, true);
    private static readonly EventoCampoDto Previa = new(Guid.Parse("44444444-0000-0000-0000-000000000002"), "Preñada fecha previa", true, true,
        [PasoEvento.DiasPreniez], Correcta.Id, "PRENADA_FECHA_PREVIA");
    private static readonly EventoCampoDto Aborto = new(Guid.Parse("44444444-0000-0000-0000-000000000003"), "Aborto", true, true,
        [PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], null, "ABORTO");
    private static readonly EventoCampoDto Ok = new(Guid.Parse("44444444-0000-0000-0000-000000000004"), "OK", false, true);

    private static async Task<(FakeCampoService Campo, MovimientosViewModel Vm)> BancoAsync(MovimientoDto movimiento)
    {
        var campo = new FakeCampoService { Estados = [Correcta, Previa, Aborto, Ok], Movimientos = [movimiento] };
        var sesion = new SesionTrabajo();
        sesion.Empezar(Datos.Rodeo());
        var vm = new MovimientosViewModel(campo, sesion);
        await vm.CargarAsync();
        return (campo, vm);
    }

    [Fact]
    public async Task Editar_un_aborto_resalta_Aborto_y_precarga_dias_y_resultado()
    {
        var (_, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", detalle: "Aborto, 45 días, Seca",
            eventoId: Aborto.Id, diasAborto: 45, resultadoAborto: ResultadoAborto.Seca));

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.Equal(Aborto.Id, vm.Selector.Resaltado);
        Assert.Equal(Aborto, vm.EventoEditado);
        Assert.Equal("Aborto, 45 días, Seca", vm.TextoEventoEditado);
        // Volver a tocar Aborto abre los pasos con los datos del registro.
        await vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        Assert.Equal("45", vm.Selector.TextoDias);
    }

    [Fact]
    public async Task Editar_un_aborto_con_Rechazo_precarga_el_resultado_Rechazo()
    {
        var (_, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1014", detalle: "Aborto, 10 días, Rechazo",
            eventoId: Aborto.Id, diasAborto: 10, resultadoAborto: ResultadoAborto.Rechazo));

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.Equal(new EleccionEvento(Aborto.Id, null, 10, ResultadoAborto.Rechazo), vm.EleccionEditada);
    }

    [Fact]
    public async Task Editar_una_fecha_previa_resalta_ese_boton_y_precarga_los_dias()
    {
        var (_, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", detalle: "Preñada fecha previa, 130 días",
            eventoId: Previa.Id, diasPreniez: 130));

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.Equal(Previa.Id, vm.Selector.Resaltado);
        await vm.Selector.ElegirCommand.ExecuteAsync(Previa);
        Assert.Equal("130", vm.Selector.TextoDias);
    }

    [Fact]
    public async Task Guardar_la_edicion_sin_tocar_nada_manda_la_eleccion_precargada()
    {
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", eventoId: Previa.Id, diasPreniez: 130));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Equal(Previa.Id, cmd.Evento);
        Assert.Equal(new EleccionEvento(Previa.Id, 130), cmd.Eleccion);
    }

    [Fact]
    public async Task Pasar_de_un_estado_a_un_aborto_con_los_pasos_manda_la_eleccion_nueva()
    {
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", eventoId: Correcta.Id));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        await vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        // Con el paso abierto el casillero de la edición no cambia y no se guarda nada.
        Assert.Equal(Correcta, vm.EventoEditado);
        vm.Selector.TextoDias = "20";
        await vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
        await vm.Selector.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Fresca);
        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Equal(Aborto.Id, cmd.Evento);
        Assert.Equal(new EleccionEvento(Aborto.Id, null, 20, ResultadoAborto.Fresca), cmd.Eleccion);
    }

    [Fact]
    public async Task Cancelar_un_paso_en_la_edicion_deja_el_evento_del_registro()
    {
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", eventoId: Correcta.Id));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);
        await vm.Selector.ElegirCommand.ExecuteAsync(Aborto);

        vm.Selector.CancelarCommand.Execute(null);

        Assert.Equal(Correcta, vm.EventoEditado);
        Assert.Empty(campo.RegistrosCorregidos);
    }

    [Fact]
    public async Task Un_evento_sin_pasos_se_corrige_sin_eleccion_como_antes()
    {
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", eventoId: Correcta.Id));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);
        await vm.Selector.ElegirCommand.ExecuteAsync(Ok);

        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(campo.RegistrosCorregidos).Eleccion);
    }

    [Fact]
    public async Task Un_aborto_viejo_sin_dias_ni_resultado_no_queda_elegido_hay_que_completarlo()
    {
        var (_, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1014", detalle: "Aborto", eventoId: Aborto.Id));

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.Null(vm.EventoEditado);
        Assert.Equal(Aborto.Id, vm.Selector.Resaltado);   // igual se resalta: se ve cuál era
        Assert.False(vm.GuardarEdicionCommand.CanExecute(null));
    }
}
