using Aviss.Application.Campo;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.Campo;

/// <summary>Spec 005 (segunda vuelta): la descripción de los eventos con pasos, la elección y la lista con pasos y resultados de aborto.</summary>
public sealed class EventosConPasosApplicationTests
{
    private static readonly DateTime Ahora = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static List<EstadoReproductivo> Estandar() =>
        EstadosIniciales.Todos.Select(i => EstadoReproductivo.CrearEstandar(i, Ahora, i.DestinoClave is null ? null : Guid.NewGuid())).ToList();

    [Theory]
    [InlineData("Aborto", null, 45, ResultadoAborto.VaciaOk, "Aborto, 45 días, Vacía OK")]
    [InlineData("Aborto", null, 0, ResultadoAborto.Rechazo, "Aborto, 0 días, Rechazo")]
    [InlineData("Aborto", null, 1, ResultadoAborto.Fresca, "Aborto, 1 día, Fresca")]
    [InlineData("Preñada fecha previa", 130, null, null, "Preñada fecha previa, 130 días")]
    [InlineData("Aborto", null, null, null, "Aborto")] // un Aborto viejo, sin datos
    [InlineData("VACIA", null, null, null, "VACIA")]
    public void La_descripcion_arma_el_texto_completo(string nombre, int? dp, int? da, ResultadoAborto? r, string esperado)
        => Assert.Equal(esperado, DescripcionEvento.Armar(nombre, dp, da, r));

    [Fact]
    public void RegistraComoRechazo_es_el_Rechazo_fijo_o_el_Aborto_con_Rechazo()
    {
        Assert.True(new EleccionEvento(EventosCampo.Rechazo).RegistraComoRechazo);
        Assert.True(new EleccionEvento(Guid.NewGuid(), null, 10, ResultadoAborto.Rechazo).RegistraComoRechazo);
        Assert.False(new EleccionEvento(Guid.NewGuid(), null, 10, ResultadoAborto.Seca).RegistraComoRechazo);
        Assert.False(new EleccionEvento(Guid.NewGuid()).RegistraComoRechazo);
    }

    [Fact]
    public void El_dto_trae_pasos_destino_y_clave_estandar()
    {
        var lista = ArmadorListaEventos.Armar(Estandar());
        var aborto = lista.Principales.Single(e => e.Nombre == "Aborto");
        var previa = lista.Principales.Single(e => e.Nombre == "Preñada fecha previa");
        var ok = lista.Principales.Single(e => e.Nombre == "OK");

        Assert.Equal([PasoEvento.DiasAborto, PasoEvento.ResultadoAborto], aborto.Pasos);
        Assert.Equal("ABORTO", aborto.ClaveEstandar);
        Assert.Equal([PasoEvento.DiasPreniez], previa.Pasos);
        Assert.NotNull(previa.EstadoDestinoId);
        Assert.False(ok.TienePasos);
    }

    [Fact]
    public void Los_resultados_del_aborto_son_los_estados_activos_y_Rechazo_siempre()
    {
        var catalogo = Estandar();
        Assert.Equal(ReglasResultadoAborto.Todos, ArmadorListaEventos.Armar(catalogo).ResultadosAborto);

        catalogo.Single(e => e.ClaveEstandar == "SECA").Desactivar(Ahora);
        catalogo.Single(e => e.ClaveEstandar == "VACIA").Desactivar(Ahora);
        var lista = ArmadorListaEventos.Armar(catalogo);

        Assert.Equal([ResultadoAborto.Inseminada, ResultadoAborto.Fresca, ResultadoAborto.Rechazo], lista.ResultadosAborto);
    }
}
