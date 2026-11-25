using Aviss.App.ViewModels;
using Aviss.Application.Campo;

namespace Aviss.Application.Tests.ViewModels;

// Spec 011 (RF-3): el selector en "modo segundo evento".
public class SelectorEventosViewModelSegundoTests
{
    private static readonly EventoCampoDto Ok = new(Guid.NewGuid(), "OK", false, true, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Revisar = new(Guid.NewGuid(), "Revisar Nuevamente", false, true, admiteSegundo: true, puedeSerSegundo: true);
    private static readonly EventoCampoDto Vacia = new(Guid.NewGuid(), "VACIA", true, true, admiteSegundo: true, puedeSerSegundo: false);
    private static readonly EventoCampoDto Prostag = new(Guid.NewGuid(), "PROSTAG", true, false, admiteSegundo: true, puedeSerSegundo: true);

    private static SelectorEventosViewModel Selector(params EventoCampoDto[] eventos)
    {
        var s = new SelectorEventosViewModel(permiteNuevo: true);
        s.Actualizar(new ListaEventosDto(
            eventos.Where(e => e.EsPrincipal).ToList(),
            [ArmadorListaEventos.EventoRechazo, .. eventos.Where(e => !e.EsPrincipal)]));
        return s;
    }

    [Fact]
    public void El_modo_normal_muestra_todo_y_el_modo_segundo_filtra()
    {
        var s = Selector(Ok, Revisar, Vacia, Prostag);
        Assert.Contains(s.Principales, e => e.Nombre == "VACIA");

        s.AbrirSegundo(null, Ok.Id);

        Assert.True(s.ModoSegundo);
        Assert.Equal(["Revisar Nuevamente"], s.Principales.Select(e => e.Nombre));
        Assert.Equal(["PROSTAG"], s.Otros.Select(e => e.Nombre));   // sin el Rechazo fijo
        Assert.False(s.ListaSegundoVacia);
    }

    [Fact]
    public void Abrir_en_modo_normal_despues_del_modo_segundo_restaura_las_listas()
    {
        var s = Selector(Ok, Revisar, Vacia, Prostag);
        s.AbrirSegundo(null, Ok.Id);

        s.Abrir(Ok.Id);

        Assert.False(s.ModoSegundo);
        Assert.Contains(s.Principales, e => e.Nombre == "OK");
        Assert.Contains(s.Principales, e => e.Nombre == "VACIA");
        Assert.Contains(s.Otros, e => e.Id == EventosCampo.Rechazo);
    }

    [Fact]
    public void LimpiarEleccion_saca_del_modo_segundo()
    {
        var s = Selector(Ok, Vacia);
        s.AbrirSegundo(null, Ok.Id);

        s.LimpiarEleccion();

        Assert.False(s.ModoSegundo);
        Assert.Contains(s.Principales, e => e.Nombre == "VACIA");
    }

    [Fact]
    public void Con_lista_vacia_dice_el_texto_fijo()
    {
        var s = Selector(Ok, Vacia);   // solo OK (el Evento 1) y un estándar: nada para combinar

        s.AbrirSegundo(null, Ok.Id);

        Assert.Empty(s.Principales);
        Assert.Empty(s.Otros);
        Assert.True(s.ListaSegundoVacia);
        Assert.Equal("No hay otros eventos para combinar", s.TextoListaVacia);
    }

    [Fact]
    public void Un_evento_nuevo_combinable_aparece_en_modo_segundo_al_actualizar_las_listas()
    {
        var s = Selector(Ok, Vacia);
        s.AbrirSegundo(null, Ok.Id);
        Assert.True(s.ListaSegundoVacia);

        s.Actualizar(new ListaEventosDto([Ok, Vacia], [ArmadorListaEventos.EventoRechazo, Prostag]));   // se creó PROSTAG

        Assert.True(s.ModoSegundo);
        Assert.False(s.ListaSegundoVacia);
        Assert.Equal(["PROSTAG"], s.Otros.Select(e => e.Nombre));
    }

    [Fact]
    public void Buscar_encuentra_un_evento_aunque_el_modo_segundo_lo_esconda()
    {
        var s = Selector(Ok, Vacia);
        s.AbrirSegundo(null, Ok.Id);

        Assert.Equal(Vacia, s.Buscar(Vacia.Id));
        Assert.Equal(Ok, s.Buscar(Ok.Id));
    }

    [Fact]
    public void El_resumen_une_los_dos_eventos_con_mas()
    {
        Assert.Equal("OK + PROSTAG", SelectorEventosViewModel.Resumen(Ok, null, Prostag));
        Assert.Equal("OK", SelectorEventosViewModel.Resumen(Ok, null));
    }
}
