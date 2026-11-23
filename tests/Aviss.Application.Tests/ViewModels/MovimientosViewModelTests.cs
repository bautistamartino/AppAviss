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
            Sesion.Empezar(Datos.Rodeo());
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
        Assert.Equal(Datos.UsuarioId, cmd.UsuarioId);
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

    // ------------------------------------------------------------------ qué se muestra de cada movimiento (sin manga ni veterinario)

    [Fact]
    public void Un_tacto_muestra_hora_caravana_cvet_evento_y_observacion_sin_manga_ni_veterinario()
    {
        var item = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Tacto, caravana: "1014", detalle: "PREÑADA", cvet: "PRE", observaciones: "Cojera"));

        Assert.Equal("1014", item.Titulo);
        Assert.Equal("Tacto", item.Tipo);
        Assert.Equal("CVET PRE", item.TextoCvet);
        Assert.True(item.HayCvet);
        Assert.Equal("PREÑADA", item.Detalle);
        Assert.Equal("Cojera", item.Observacion);
        Assert.Equal("Tacto · PREÑADA", item.TextoTipoDetalle);
        Assert.True(item.HayObservacion);
        Assert.True(item.HayDetalle);
        Assert.Equal(item.Dto.FechaHoraUtc.ToLocalTime().ToString("HH:mm"), item.Hora);
        Assert.DoesNotContain("Manga", item.Detalle);
    }

    [Fact]
    public void Las_filas_y_el_DTO_del_movimiento_no_traen_el_nombre_de_nadie()
    {
        // Spec 006, RF-5: ni el DTO ni la fila tienen un campo con el nombre de quien lo hizo.
        var miembros = typeof(MovimientoDto).GetProperties().Select(p => p.Name).Concat(typeof(MovimientoItem).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(miembros, n => n.Contains("Veterinario", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Una_marca_muestra_hora_caravana_y_cvet_sin_evento()
    {
        var item = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Marca, caravana: "77", cvet: "0"));

        Assert.Equal("Marca", item.Tipo);
        Assert.Equal("Marca", item.TextoTipoDetalle);
        Assert.Equal("CVET 0", item.TextoCvet);
        Assert.Equal("", item.Detalle);
        Assert.False(item.HayDetalle);   // una marca sola no tiene evento: la fila no lleva un renglón vacío
        Assert.False(item.HayObservacion);
    }

    [Fact]
    public void Sin_cvet_no_se_muestra_nada_de_cvet()
    {
        var item = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Marca, cvet: ""));

        Assert.False(item.HayCvet);
        Assert.Equal("", item.TextoCvet);
    }

    [Fact]
    public void Un_cierre_sigue_hablando_de_su_manga_porque_es_un_movimiento_de_manga()
    {
        var item = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Cierre, caravana: null));

        Assert.Equal("Cierre de manga", item.Tipo);
        Assert.Equal("Manga 1", item.Titulo);
        Assert.False(item.HayCvet);
    }

    // ------------------------------------------------------------------ editar un tacto

    private static readonly EstadoActivoDto Prenada = new(Guid.Parse("33333333-0000-0000-0000-000000000001"), "PREÑADA");
    private static readonly EstadoActivoDto Vacia = new(Guid.Parse("33333333-0000-0000-0000-000000000002"), "VACIA");

    private static async Task<(Banco Banco, MovimientoItem Tacto)> BancoConUnTactoAsync(bool puedeDeshacer = true)
    {
        var b = new Banco();
        b.Campo.Estados = [Prenada, Vacia];
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Tacto, puedeDeshacer: puedeDeshacer, caravana: "1014", detalle: "PREÑADA", cvet: "PRE", observaciones: "Cojera")];
        await b.Vm.CargarAsync();
        return (b, b.Vm.Movimientos[0]);
    }

    [Fact]
    public async Task Solo_se_puede_editar_un_tacto_que_se_puede_deshacer()
    {
        var (_, editable) = await BancoConUnTactoAsync();
        var (_, viejo) = await BancoConUnTactoAsync(puedeDeshacer: false);
        var marca = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Marca));
        var cierre = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Cierre));
        var anulado = new MovimientoItem(Datos.Movimiento(TipoMovimiento.Tacto, anulado: true));

        Assert.True(editable.PuedeEditar);
        Assert.False(viejo.PuedeEditar);
        Assert.False(marca.PuedeEditar);
        Assert.False(cierre.PuedeEditar);
        Assert.False(anulado.PuedeEditar);
    }

    [Fact]
    public async Task Editar_abre_la_edicion_con_los_eventos_el_actual_elegido_y_la_observacion_actual()
    {
        var (b, tacto) = await BancoConUnTactoAsync();

        await b.Vm.EditarCommand.ExecuteAsync(tacto);

        Assert.True(b.Vm.HayEdicion);
        Assert.Equal([Prenada, Vacia, TrabajoViewModel.EventoRechazo], b.Vm.EstadosParaEditar);   // el Rechazo siempre está al final
        Assert.Equal(Prenada, b.Vm.EventoEditado);
        Assert.Equal("Cojera", b.Vm.ObservacionEditada);
        Assert.Equal("Editar el tacto de 1014", b.Vm.TituloEdicion);
        Assert.True(b.Vm.GuardarEdicionCommand.CanExecute(null));   // el evento actual viene elegido: se puede guardar (por ejemplo, solo la observación)
    }

    [Fact]
    public async Task Editar_no_hace_nada_con_un_movimiento_que_no_se_puede_editar()
    {
        var (b, tacto) = await BancoConUnTactoAsync(puedeDeshacer: false);

        await b.Vm.EditarCommand.ExecuteAsync(tacto);

        Assert.False(b.Vm.HayEdicion);
    }

    [Fact]
    public async Task Guardar_la_edicion_manda_la_correccion_con_el_evento_y_la_observacion_y_vuelve_a_leer_la_lista()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        await b.Vm.EditarCommand.ExecuteAsync(tacto);
        b.Vm.ElegirEventoEditadoCommand.Execute(Vacia);
        b.Vm.ObservacionEditada = "  Se secó ";
        var consultas = b.Campo.ConsultasDeMovimientos;

        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);

        var cmd = Assert.Single(b.Campo.RegistrosCorregidos);
        Assert.Equal((tacto.Dto.RegistroId, Vacia.Id, "Se secó", Datos.UsuarioId), (cmd.RegistroId, cmd.Evento, cmd.Observaciones, cmd.UsuarioId));
        Assert.False(b.Vm.HayEdicion);
        Assert.Equal("Se corrigió el registro.", b.Vm.MensajeInfo);
        Assert.Equal(consultas + 1, b.Campo.ConsultasDeMovimientos);
    }

    [Fact]
    public async Task Una_observacion_vacia_se_manda_como_nula()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        await b.Vm.EditarCommand.ExecuteAsync(tacto);
        b.Vm.ObservacionEditada = "   ";

        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(b.Campo.RegistrosCorregidos).Observaciones);
    }

    [Fact]
    public async Task Si_la_correccion_se_rechaza_la_edicion_sigue_abierta_con_el_motivo()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        b.Campo.OnCorregirRegistro = _ => Resultado.Error<Guid>(CodigoError.TactoNoEsElUltimo, "Solo se puede anular el último registro vigente del animal.");
        await b.Vm.EditarCommand.ExecuteAsync(tacto);

        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);

        Assert.True(b.Vm.HayEdicion);
        Assert.Equal("Solo se puede anular el último registro vigente del animal.", b.Vm.MensajeEdicion);
        Assert.Null(b.Vm.MensajeInfo);
    }

    [Fact]
    public async Task Cancelar_la_edicion_la_cierra_sin_guardar_nada()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        await b.Vm.EditarCommand.ExecuteAsync(tacto);

        b.Vm.CancelarEdicionCommand.Execute(null);

        Assert.False(b.Vm.HayEdicion);
        Assert.Empty(b.Campo.RegistrosCorregidos);
    }

    [Fact]
    public async Task Sin_evento_elegido_no_se_puede_guardar()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        b.Campo.Estados = [Vacia];   // el evento actual (PREÑADA) ya no está entre los activos
        await b.Vm.EditarCommand.ExecuteAsync(tacto);
        Assert.Null(b.Vm.EventoEditado);

        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);

        Assert.Empty(b.Campo.RegistrosCorregidos);
        Assert.True(b.Vm.HayEdicion);
    }

    // ------------------------------------------------------------------ rechazo en la lista y en "Editar" (spec 004, RF-7, RF-8)

    private static MovimientoItem Rechazo(string? motivo = "Pata", bool puedeDeshacer = true, bool anulado = false)
        => new(Datos.Movimiento(TipoMovimiento.Rechazo, puedeDeshacer: puedeDeshacer, anulado: anulado, caravana: "1014", detalle: "Rechazo", cvet: "PRE", observaciones: motivo));

    [Fact]
    public void Un_rechazo_muestra_Rechazo_y_el_motivo_en_el_segundo_renglon_sin_repetirlo()
    {
        var item = Rechazo("Pata");

        Assert.Equal("Rechazo", item.Tipo);
        Assert.Equal("Rechazo · Pata", item.TextoTipoDetalle);
        Assert.False(item.HayObservacion);
        Assert.Equal("CVET PRE", item.TextoCvet);
        Assert.Equal("Rechazo", Rechazo(null).TextoTipoDetalle);
    }

    [Fact]
    public void Un_rechazo_se_puede_editar_si_se_puede_deshacer_y_no_esta_anulado()
    {
        Assert.True(Rechazo().PuedeEditar);
        Assert.False(Rechazo(puedeDeshacer: false).PuedeEditar);
        Assert.False(Rechazo(anulado: true).PuedeEditar);
    }

    [Fact]
    public async Task Deshacer_un_rechazo_manda_el_tipo_Rechazo()
    {
        var b = new Banco();
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Rechazo)];
        await b.Vm.CargarAsync();

        await b.Vm.DeshacerCommand.ExecuteAsync(b.Vm.Movimientos[0]);

        Assert.Equal(TipoDeshacer.Rechazo, Assert.Single(b.Campo.Deshaceres).Tipo);
    }

    [Fact]
    public async Task Editar_un_rechazo_abre_la_edicion_con_Rechazo_elegido_y_el_motivo()
    {
        var b = new Banco();
        b.Campo.Estados = [Prenada, Vacia];
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1014", detalle: "Rechazo", observaciones: "Pata")];
        await b.Vm.CargarAsync();

        await b.Vm.EditarCommand.ExecuteAsync(b.Vm.Movimientos[0]);

        Assert.Equal(TrabajoViewModel.EventoRechazo, b.Vm.EventoEditado);
        Assert.Equal("Pata", b.Vm.ObservacionEditada);
        Assert.Equal("Editar el rechazo de 1014", b.Vm.TituloEdicion);
    }

    [Fact]
    public async Task Cambiar_un_tacto_por_un_rechazo_y_un_rechazo_por_un_tacto_manda_el_evento_elegido()
    {
        var (b, tacto) = await BancoConUnTactoAsync();
        await b.Vm.EditarCommand.ExecuteAsync(tacto);
        b.Vm.ElegirEventoEditadoCommand.Execute(TrabajoViewModel.EventoRechazo);
        b.Vm.ObservacionEditada = "Ubre";
        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);
        var aRechazo = Assert.Single(b.Campo.RegistrosCorregidos);
        Assert.Equal((EventosCampo.Rechazo, "Ubre"), (aRechazo.Evento, aRechazo.Observaciones));

        // y al revés: un rechazo que pasa a ser un tacto
        b.Campo.Movimientos = [Datos.Movimiento(TipoMovimiento.Rechazo, caravana: "1014", detalle: "Rechazo", observaciones: "Ubre")];
        await b.Vm.CargarAsync();
        await b.Vm.EditarCommand.ExecuteAsync(b.Vm.Movimientos[0]);
        b.Vm.ElegirEventoEditadoCommand.Execute(Vacia);
        await b.Vm.GuardarEdicionCommand.ExecuteAsync(null);
        Assert.Equal(Vacia.Id, b.Campo.RegistrosCorregidos[1].Evento);
    }

    private sealed class Fallo : FakeCampoService
    {
        public override Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(Guid rodeoId, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }
}
