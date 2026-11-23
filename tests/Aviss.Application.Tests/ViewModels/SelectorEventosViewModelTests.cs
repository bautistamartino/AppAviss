using Aviss.App.ViewModels;
using Aviss.Application.Campo;

namespace Aviss.Application.Tests.ViewModels;

// Spec 005 (RF-6 a RF-9, RF-11, RF-14): el selector de eventos de dos niveles, sin pantalla.
public class SelectorEventosViewModelTests
{
    private static EventoCampoDto Evento(string nombre, bool principal, bool cambiaEstado = true) =>
        new(Guid.NewGuid(), nombre, cambiaEstado, principal);

    private static readonly EventoCampoDto Revisar = Evento("Revisar Nuevamente", true, false);
    private static readonly EventoCampoDto Ok = Evento("OK", true, false);
    private static readonly EventoCampoDto Vacia = Evento("VACIA", true);
    private static readonly EventoCampoDto Insemin = Evento("INSEMIN", false);
    private static readonly EventoCampoDto Seca = Evento("SECA", false);

    private static ListaEventosDto Lista() =>
        new([Revisar, Ok, Vacia], [ArmadorListaEventos.EventoRechazo, Insemin, Seca]);

    private static SelectorEventosViewModel Selector(bool permiteNuevo = true)
    {
        var s = new SelectorEventosViewModel(permiteNuevo);
        s.Actualizar(Lista());
        return s;
    }

    [Fact]
    public void Se_abre_siempre_en_los_principales_aunque_se_haya_dejado_en_Otro_evento()
    {
        var s = Selector();
        s.VerOtrosCommand.Execute(null);
        Assert.True(s.MostrandoOtros);

        s.Abrir(null);

        Assert.False(s.MostrandoOtros);
        Assert.True(s.MostrandoPrincipales);
    }

    [Fact]
    public void Otro_evento_muestra_Rechazo_primero_y_Volver_regresa_a_los_principales()
    {
        var s = Selector();

        s.VerOtrosCommand.Execute(null);

        Assert.True(s.MostrandoOtros);
        Assert.Equal(EventosCampo.Rechazo, s.Otros[0].Id);
        s.VolverCommand.Execute(null);
        Assert.False(s.MostrandoOtros);
    }

    [Fact]
    public void Resalta_un_principal_y_Otro_evento_no()
    {
        var s = Selector();

        s.Abrir(Vacia.Id);

        Assert.Equal(Vacia.Id, s.Resaltado);
        Assert.False(s.OtroEventoResaltado);
    }

    [Fact]
    public void Un_evento_de_Otro_evento_resalta_el_boton_Otro_evento()
    {
        var s = Selector();

        s.Abrir(Insemin.Id);

        Assert.True(s.OtroEventoResaltado);
    }

    [Fact]
    public void El_Rechazo_vigente_tambien_resalta_Otro_evento()
    {
        var s = Selector();

        s.Abrir(EventosCampo.Rechazo);

        Assert.True(s.OtroEventoResaltado);
    }

    [Fact]
    public void Un_evento_desactivado_no_resalta_Otro_evento_porque_no_esta_en_ninguna_lista()
    {
        var s = Selector();
        var desactivado = Guid.NewGuid();

        s.Abrir(desactivado);

        Assert.False(s.OtroEventoResaltado);
        Assert.Null(s.Buscar(desactivado));
    }

    [Fact]
    public async Task Elegir_resalta_el_evento_y_avisa_sin_guardar_nada()
    {
        var s = Selector();
        EventoCampoDto? avisado = null;
        s.AlElegir = e => { avisado = e; return Task.CompletedTask; };

        await s.ElegirCommand.ExecuteAsync(Ok);

        Assert.Equal(Ok, avisado);
        Assert.Equal(Ok.Id, s.Resaltado);
    }

    [Fact]
    public async Task Elegir_nulo_no_hace_nada()
    {
        var s = Selector();
        var llamado = false;
        s.AlElegir = _ => { llamado = true; return Task.CompletedTask; };

        await s.ElegirCommand.ExecuteAsync(null);

        Assert.False(llamado);
        Assert.Null(s.Resaltado);
    }

    [Fact]
    public void Nuevo_evento_solo_se_ofrece_desde_Otro_evento_y_si_el_selector_lo_permite()
    {
        var conNuevo = Selector(permiteNuevo: true);
        var sinNuevo = Selector(permiteNuevo: false);

        Assert.False(conNuevo.PuedeCrearNuevo);   // en los principales no
        conNuevo.VerOtrosCommand.Execute(null);
        Assert.True(conNuevo.PuedeCrearNuevo);

        sinNuevo.VerOtrosCommand.Execute(null);
        Assert.False(sinNuevo.PuedeCrearNuevo);   // la edición de movimientos no crea eventos
    }

    [Fact]
    public void Con_todos_los_principales_desactivados_queda_solo_Otro_evento()
    {
        var s = new SelectorEventosViewModel(true);

        s.Actualizar(new ListaEventosDto([], [ArmadorListaEventos.EventoRechazo, Insemin]));

        Assert.False(s.HayPrincipales);
        Assert.Empty(s.Principales);
        Assert.Equal(2, s.Otros.Count);
    }

    [Fact]
    public void Actualizar_no_cambia_la_vista_ni_el_resaltado()
    {
        var s = Selector();
        s.VerOtrosCommand.Execute(null);
        s.Resaltado = Seca.Id;

        s.Actualizar(Lista());

        Assert.True(s.MostrandoOtros);
        Assert.Equal(Seca.Id, s.Resaltado);
    }

    [Fact]
    public async Task Cuenta_de_toques_un_principal_son_2_y_uno_de_Otro_evento_son_3()
    {
        // Toque 1: el casillero "Evento" (abre el selector en los principales). Toque 2: el principal. Un evento de "Otro evento" suma el
        // toque de "Otro evento" antes: 3 en total. Ningún evento activo necesita más (RF-14).
        var s = Selector();
        var tocados = new List<string>();
        s.AlElegir = e => { tocados.Add(e.Nombre); return Task.CompletedTask; };

        s.Abrir(null);                                 // toque 1
        await s.ElegirCommand.ExecuteAsync(Ok);        // toque 2
        s.Abrir(null);                                 // toque 1
        s.VerOtrosCommand.Execute(null);               // toque 2
        await s.ElegirCommand.ExecuteAsync(Seca);      // toque 3

        Assert.Equal(["OK", "SECA"], tocados);
    }
}
