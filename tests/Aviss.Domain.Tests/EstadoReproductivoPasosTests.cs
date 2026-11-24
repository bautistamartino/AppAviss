using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Tests;

/// <summary>Spec 005 (segunda vuelta): pasos, destino y los diez estándar (RF-1 a RF-3, RF-26, RF-27).</summary>
public class EstadoReproductivoPasosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
    private static EstadoInicial Inicial(string codigo) => EstadosIniciales.Todos.Single(x => x.Codigo == codigo);

    [Fact]
    public void Los_diez_estandar_con_los_siete_principales_en_el_orden_de_RF3()
    {
        Assert.Equal(10, EstadosIniciales.Todos.Count);
        var principales = EstadosIniciales.Todos.Where(x => x.EsPrincipal).OrderBy(x => x.Orden).Select(x => x.Codigo);
        Assert.Equal(["REVISAR_NUEVAMENTE", "OK", "VACIA", "PRENADA", "PRENADA_FECHA_PREVIA", "ABORTO", "NOINSEM"], principales);
        Assert.Equal(["OK", "REVISAR_NUEVAMENTE"],
            EstadosIniciales.Todos.Where(x => x.Tipo == TipoEvento.SoloEvento).Select(x => x.Codigo).Order(StringComparer.Ordinal));
        Assert.Equal("Preñada fecha correcta", Inicial("PRENADA").Nombre);
        Assert.Equal("PREÑADA", Inicial("PRENADA").CodigoDairyComp);
        Assert.Equal("PREÑADA FECHA PREVIA", Inicial("PRENADA_FECHA_PREVIA").CodigoDairyComp);
        Assert.Equal("PRENADA", Inicial("PRENADA_FECHA_PREVIA").DestinoClave);
        Assert.Equal("DiasPreniez", Inicial("PRENADA_FECHA_PREVIA").Pasos);
        Assert.Equal("DiasAborto,ResultadoAborto", Inicial("ABORTO").Pasos);
        Assert.Equal(TipoEvento.Estado, Inicial("ABORTO").Tipo);
        Assert.Equal(6, Inicial("ABORTO").Orden);
        Assert.Equal(7, Inicial("NOINSEM").Orden);
        // Todos son válidos para crear (código interno solo A-Z, 0-9 y guion bajo).
        foreach (var i in EstadosIniciales.Todos) EstadoReproductivo.CrearEstandar(i, Ahora);
    }

    [Fact]
    public void CrearEstandar_copia_pasos_y_destino()
    {
        var destino = Guid.NewGuid();
        var e = EstadoReproductivo.CrearEstandar(Inicial("PRENADA_FECHA_PREVIA"), Ahora, destino);
        Assert.Equal("DiasPreniez", e.Pasos);
        Assert.Equal(destino, e.EstadoDestinoId);
        Assert.Equal([PasoEvento.DiasPreniez], e.ListarPasos());

        var aborto = EstadoReproductivo.CrearEstandar(Inicial("ABORTO"), Ahora);
        Assert.Equal([PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], aborto.ListarPasos());
        Assert.Null(aborto.EstadoDestinoId);
        Assert.Empty(EstadoReproductivo.Crear("A", "A", "A", Ahora).ListarPasos());
    }

    [Fact]
    public void Modificar_conserva_id_clave_pasos_y_destino()
    {
        var destino = Guid.NewGuid();
        var e = EstadoReproductivo.CrearEstandar(Inicial("PRENADA_FECHA_PREVIA"), Ahora, destino);
        var id = e.Id;

        e.Modificar("Fecha previa", "FECHA PREVIA DC", Ahora.AddMinutes(1));

        Assert.Equal(id, e.Id);
        Assert.Equal("PRENADA_FECHA_PREVIA", e.ClaveEstandar);
        Assert.Equal("DiasPreniez", e.Pasos);
        Assert.Equal(destino, e.EstadoDestinoId);
        Assert.Equal(5, e.Orden);
    }

    [Fact]
    public void Adoptar_pone_pasos_sin_cambiar_nombre_codigo_ni_activo()
    {
        var aMano = EstadoReproductivo.Crear("ABORTO", "aborto", "Aborto vaca", Ahora);
        aMano.Desactivar(Ahora);

        aMano.AdoptarComoEstandar(Inicial("ABORTO"), Ahora.AddMinutes(5));

        Assert.Equal("aborto", aMano.Nombre);
        Assert.Equal("Aborto vaca", aMano.CodigoDairyComp);
        Assert.False(aMano.Activo);
        Assert.Equal(TipoEvento.Estado, aMano.Tipo);
        Assert.Equal("DiasAborto,ResultadoAborto", aMano.Pasos);
        Assert.Equal(6, aMano.Orden);
    }

    [Fact]
    public void Reconciliar_solo_toca_si_hay_cambios()
    {
        var e = EstadoReproductivo.CrearEstandar(Inicial("ABORTO"), Ahora);
        Assert.False(e.Reconciliar(Inicial("ABORTO"), null, Ahora.AddMinutes(1)));
        Assert.Equal(Ahora, e.ActualizadoEnUtc);

        var viejo = EstadoReproductivo.Crear("ABORTO", "Aborto", "ABORTO", Ahora);
        viejo.AdoptarComoEstandar(new EstadoInicial("ABORTO", "Aborto", "ABORTO", TipoEvento.SoloEvento, true, 5), Ahora);
        Assert.True(viejo.Reconciliar(Inicial("ABORTO"), null, Ahora.AddMinutes(2)));
        Assert.Equal(TipoEvento.Estado, viejo.Tipo);
        Assert.Equal(6, viejo.Orden);
        Assert.Equal("DiasAborto,ResultadoAborto", viejo.Pasos);
    }

    [Fact]
    public void Un_tacto_lleva_evento_y_dias_validos()
    {
        var animal = Guid.NewGuid(); var ev = Guid.NewGuid(); var est = Guid.NewGuid();
        var t = Tacto.Registrar(animal, Ahora, 1, null, est, null, null, Ahora, ev, diasPreniezInformados: 130);
        Assert.Equal(ev, t.EventoId);
        Assert.Equal(130, t.DiasPreniezInformados);
        Assert.Null(t.DiasAborto);

        var a = Tacto.Registrar(animal, Ahora, 2, null, est, null, null, Ahora, ev, diasAborto: 0);
        Assert.Equal(0, a.DiasAborto);

        Assert.Throws<DominioException>(() => Tacto.Registrar(animal, Ahora, 3, null, est, null, null, Ahora, ev, diasPreniezInformados: 0));
        Assert.Throws<DominioException>(() => Tacto.Registrar(animal, Ahora, 3, null, est, null, null, Ahora, ev, diasPreniezInformados: 401));
        Assert.Throws<DominioException>(() => Tacto.Registrar(animal, Ahora, 3, null, est, null, null, Ahora, ev, diasAborto: 401));
        Assert.Throws<DominioException>(() => Tacto.Registrar(animal, Ahora, 3, null, est, null, null, Ahora, ev, diasPreniezInformados: 5, diasAborto: 5));
        Assert.Throws<DominioException>(() => Tacto.Registrar(animal, Ahora, 3, null, est, null, null, Ahora, null, diasAborto: 5)); // días sin evento
    }

    [Fact]
    public void Un_rechazo_lleva_dias_de_aborto_solo_con_evento()
    {
        var animal = Guid.NewGuid(); var ev = Guid.NewGuid();
        Assert.Equal(45, Rechazo.Registrar(animal, Ahora, 1, null, Ahora, ev, 45).DiasAborto);
        Assert.Null(Rechazo.Registrar(animal, Ahora, 2, null, Ahora, ev).DiasAborto);
        Assert.Throws<DominioException>(() => Rechazo.Registrar(animal, Ahora, 3, null, Ahora, ev, 401));
        Assert.Throws<DominioException>(() => Rechazo.Registrar(animal, Ahora, 3, null, Ahora, null, 5));
    }

    [Fact]
    public void La_anulacion_no_copia_datos_del_evento()
    {
        var animal = Guid.NewGuid(); var ev = Guid.NewGuid(); var est = Guid.NewGuid();
        var t = Tacto.Registrar(animal, Ahora, 1, null, est, null, null, Ahora, ev, diasAborto: 45);
        var anul = Tacto.CrearAnulacion(t, est, Ahora, 2, Ahora);
        Assert.Null(anul.EventoId);
        Assert.Null(anul.DiasAborto);
        Assert.Equal(45, t.DiasAborto);
    }
}
