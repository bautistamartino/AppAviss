using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Tests;

/// <summary>Spec 011 (RF-10, RF-11, RF-12, RF-13): el tacto guarda el Evento 2 y su observación en el mismo registro.</summary>
public class TactoSegundoEventoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Animal = Guid.NewGuid();
    private static readonly Guid Estado = Guid.NewGuid();
    private static readonly Guid Evento1 = Guid.NewGuid();
    private static readonly Guid Evento2 = Guid.NewGuid();

    private static Tacto Registrar(Guid? eventoId = null, int? diasPreniezInformados = null, int? diasAborto = null,
        Guid? evento2 = null, string? obs2 = null, Guid? estadoNuevo = null)
        => Tacto.Registrar(Animal, Ahora, 1, null, estadoNuevo ?? Estado, null, "obs1", Ahora,
            eventoId, diasPreniezInformados, diasAborto, evento2, obs2);

    [Fact]
    public void Un_tacto_de_un_solo_evento_queda_sin_Evento_2()
    {
        var t = Registrar();
        Assert.Null(t.Evento2Id);
        Assert.Null(t.Observaciones2);
    }

    [Fact]
    public void Guarda_el_Evento_2_con_su_observacion_aparte_de_la_del_Evento_1()
    {
        var t = Registrar(evento2: Evento2, obs2: "  se le dio  ");
        Assert.Equal(Evento2, t.Evento2Id);
        Assert.Equal("se le dio", t.Observaciones2);   // recortada como la otra
        Assert.Equal("obs1", t.Observaciones);
    }

    [Fact]
    public void La_observacion_del_Evento_2_es_opcional_y_en_blanco_queda_nula()
    {
        Assert.Null(Registrar(evento2: Evento2).Observaciones2);
        Assert.Null(Registrar(evento2: Evento2, obs2: "   ").Observaciones2);
    }

    [Fact]
    public void Acepta_300_caracteres_y_rechaza_301_en_la_observacion_del_Evento_2()
    {
        Assert.Equal(300, Registrar(evento2: Evento2, obs2: new string('a', 300)).Observaciones2!.Length);
        Assert.Throws<DominioException>(() => Registrar(evento2: Evento2, obs2: new string('a', 301)));
    }

    [Fact]
    public void Rechaza_una_observacion_2_sin_Evento_2()
        => Assert.Throws<DominioException>(() => Registrar(obs2: "suelta"));

    [Fact]
    public void Rechaza_un_Evento_2_igual_al_estado_nuevo_o_al_evento_elegido()
    {
        Assert.Throws<DominioException>(() => Registrar(evento2: Estado));
        Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, evento2: Evento1));
        // Con evento elegido, el estado nuevo sigue siendo el estado final: el Evento 2 tampoco puede ser ese estado.
        Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, evento2: Estado));
    }

    [Fact]
    public void Un_evento_con_pasos_no_admite_Evento_2()
    {
        Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, diasPreniezInformados: 100, evento2: Evento2));
        Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, diasAborto: 10, evento2: Evento2));
    }

    [Fact]
    public void El_Evento_1_de_un_tacto_no_gana_tope_de_300_en_el_dominio()
    {
        // Lo hace valer el servicio (RF-12), no el dominio.
        var t = Tacto.Registrar(Animal, Ahora, 1, null, Estado, null, new string('x', 500), Ahora);
        Assert.Equal(500, t.Observaciones!.Length);
    }

    [Fact]
    public void La_anulacion_no_copia_el_Evento_2_y_el_anulado_lo_conserva()
    {
        var original = Registrar(evento2: Evento2, obs2: "x");
        var anulacion = Tacto.CrearAnulacion(original, Estado, Ahora, 2, Ahora);
        Assert.Null(anulacion.Evento2Id);
        Assert.Null(anulacion.Observaciones2);
        original.MarcarAnulado(Ahora);
        Assert.Equal(Evento2, original.Evento2Id);
        Assert.Equal("x", original.Observaciones2);
        Assert.Equal(TipoTacto.Anulacion, anulacion.Tipo);
    }
}
