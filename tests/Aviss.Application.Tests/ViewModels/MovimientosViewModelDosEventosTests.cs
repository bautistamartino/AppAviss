using Aviss.App.ViewModels;
using Aviss.Application.Campo;
using Aviss.Application.Tests.ViewModels.Soporte;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.ViewModels;

// Spec 011 (RF-16): Editar con los dos eventos precargados; se puede cambiar, quitar o sumar el Evento 2.
public class MovimientosViewModelDosEventosTests
{
    private static readonly EventoCampoDto Ok = new(Guid.Parse("77777777-0000-0000-0000-000000000001"), "OK", false, true, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Revisar = new(Guid.Parse("77777777-0000-0000-0000-000000000002"), "Revisar Nuevamente", false, true, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Prostag = new(Guid.Parse("77777777-0000-0000-0000-000000000003"), "PROSTAG", true, false, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Vacia = new(Guid.Parse("77777777-0000-0000-0000-000000000004"), "VACIA", true, true, admiteSegundo: true, puedeSerSegundo: false);
    private static readonly EventoCampoDto Aborto = new(Guid.Parse("77777777-0000-0000-0000-000000000005"), "Aborto", true, true,
        [PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], null, "ABORTO");

    private static async Task<(FakeCampoService Campo, MovimientosViewModel Vm)> BancoAsync(MovimientoDto movimiento)
    {
        var campo = new FakeCampoService { Estados = [Ok, Revisar, Prostag, Vacia, Aborto], Movimientos = [movimiento] };
        var sesion = new SesionTrabajo();
        sesion.Empezar(Datos.Rodeo());
        var vm = new MovimientosViewModel(campo, sesion);
        await vm.CargarAsync();
        return (campo, vm);
    }

    private static MovimientoDto DosEventos() => Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1000", detalle: "OK + PROSTAG",
        eventoId: Ok.Id, observaciones: "uno", evento2Id: Prostag.Id, observaciones2: "dos");

    [Fact]
    public async Task Editar_un_registro_con_dos_eventos_los_precarga_con_sus_observaciones()
    {
        var (_, vm) = await BancoAsync(DosEventos());

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.Equal(Ok, vm.EventoEditado);
        Assert.Equal("uno", vm.ObservacionEditada);
        Assert.True(vm.HayFila2Editada);
        Assert.Equal(Prostag, vm.Evento2Editado);
        Assert.Equal("dos", vm.Observacion2Editada);
        Assert.Equal("PROSTAG", vm.TextoEvento2Editado);
        Assert.False(vm.PuedeAgregarEvento2Editado);
    }

    [Fact]
    public async Task Editar_un_registro_de_un_solo_evento_no_muestra_la_fila_2_pero_ofrece_sumarla()
    {
        var (_, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1000", detalle: "OK", eventoId: Ok.Id));

        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.False(vm.HayFila2Editada);
        Assert.Null(vm.Evento2Editado);
        Assert.True(vm.PuedeAgregarEvento2Editado);
    }

    [Fact]
    public async Task Guardar_sin_tocar_nada_reenvia_los_dos_eventos_y_las_dos_observaciones()
    {
        var (campo, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Equal(Ok.Id, cmd.Evento);
        Assert.Equal("uno", cmd.Observaciones);
        Assert.Equal(new SegundoEventoElegido(Prostag.Id, "dos"), cmd.Eleccion!.Segundo);
    }

    [Fact]
    public async Task Sumar_un_evento_2_a_un_registro_viejo_lo_manda_al_guardar()
    {
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1000", detalle: "OK", eventoId: Ok.Id));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.AgregarEvento2EditadoCommand.Execute(null);
        Assert.True(vm.HayFila2Editada);
        Assert.True(vm.Selector.ModoSegundo);   // el selector pasa a elegir el Evento 2, sin el Evento 1
        Assert.DoesNotContain(vm.Selector.Principales, e => e.Id == Ok.Id);
        await vm.Selector.ElegirCommand.ExecuteAsync(Prostag);
        vm.Observacion2Editada = "  nueva  ";
        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Equal(new SegundoEventoElegido(Prostag.Id, "nueva"), cmd.Eleccion!.Segundo);
    }

    [Fact]
    public async Task Cambiar_el_evento_2_manda_el_nuevo()
    {
        var (campo, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.EditarEvento2Command.Execute(null);
        await vm.Selector.ElegirCommand.ExecuteAsync(Revisar);
        await vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Equal(Revisar.Id, cmd.Eleccion!.Segundo!.EventoId);
    }

    [Fact]
    public async Task Quitar_el_evento_2_manda_el_registro_sin_Segundo_y_descarta_su_observacion()
    {
        var (campo, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.QuitarEvento2EditadoCommand.Execute(null);

        Assert.False(vm.HayFila2Editada);
        Assert.Null(vm.Evento2Editado);
        Assert.Equal("", vm.Observacion2Editada);
        Assert.Equal("uno", vm.ObservacionEditada);   // la del Evento 1 no se toca
        await vm.GuardarEdicionCommand.ExecuteAsync(null);
        var cmd = Assert.Single(campo.RegistrosCorregidos);
        Assert.Null(cmd.Eleccion?.Segundo);
        Assert.Equal("uno", cmd.Observaciones);
    }

    [Fact]
    public async Task Cambiar_el_evento_1_a_Aborto_descarta_el_evento_2_con_el_mensaje_fijo()
    {
        var (_, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.EditarEvento1Command.Execute(null);   // vuelve el selector al Evento 1
        await vm.Selector.ElegirCommand.ExecuteAsync(Aborto);
        vm.Selector.TextoDias = "40";
        await vm.Selector.ConfirmarDiasCommand.ExecuteAsync(null);
        await vm.Selector.ElegirResultadoCommand.ExecuteAsync(ResultadoAborto.Seca);

        Assert.Equal(Aborto, vm.EventoEditado);
        Assert.False(vm.HayFila2Editada);
        Assert.Null(vm.Evento2Editado);
        Assert.Equal("Ese evento no se combina con otro: se quitó el evento 2", vm.MensajeEdicion);
    }

    [Fact]
    public async Task Cambiar_el_evento_1_al_mismo_que_el_evento_2_descarta_el_evento_2()
    {
        var (_, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.EditarEvento1Command.Execute(null);
        await vm.Selector.ElegirCommand.ExecuteAsync(Prostag);

        Assert.Equal(Prostag, vm.EventoEditado);
        Assert.False(vm.HayFila2Editada);
        Assert.Equal("Ese evento no se combina con otro: se quitó el evento 2", vm.MensajeEdicion);
    }

    [Fact]
    public async Task Un_evento_2_que_ya_no_esta_activo_obliga_a_quitarlo_o_elegir_otro_para_guardar()
    {
        var inactivo = Guid.NewGuid();
        var (campo, vm) = await BancoAsync(Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1000", detalle: "OK + VIEJO",
            eventoId: Ok.Id, evento2Id: inactivo));
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        Assert.True(vm.HayFila2Editada);
        Assert.Null(vm.Evento2Editado);
        Assert.False(vm.GuardarEdicionCommand.CanExecute(null));

        vm.QuitarEvento2EditadoCommand.Execute(null);
        Assert.True(vm.GuardarEdicionCommand.CanExecute(null));
        await vm.GuardarEdicionCommand.ExecuteAsync(null);
        Assert.Null(Assert.Single(campo.RegistrosCorregidos).Eleccion?.Segundo);
    }

    [Fact]
    public async Task Cancelar_la_edicion_limpia_la_fila_2()
    {
        var (_, vm) = await BancoAsync(DosEventos());
        await vm.EditarCommand.ExecuteAsync(vm.Movimientos[0]);

        vm.CancelarEdicionCommand.Execute(null);

        Assert.False(vm.HayFila2Editada);
        Assert.Null(vm.Evento2Editado);
        Assert.Equal("", vm.Observacion2Editada);
        Assert.False(vm.Selector.ModoSegundo);
    }

    [Fact]
    public async Task Un_registro_con_dos_eventos_se_muestra_unido_en_el_texto_del_movimiento()
    {
        var (_, vm) = await BancoAsync(DosEventos());

        Assert.Contains("OK + PROSTAG", vm.Movimientos[0].TextoTipoDetalle);
    }
}
