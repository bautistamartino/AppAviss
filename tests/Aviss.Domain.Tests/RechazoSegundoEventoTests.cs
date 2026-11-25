using Aviss.Domain.Entidades;

namespace Aviss.Domain.Tests;

/// <summary>Spec 011 (RF-10, RF-13): un registro de "solo evento" (tabla Rechazo) también guarda un Evento 2.</summary>
public class RechazoSegundoEventoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Animal = Guid.NewGuid();
    private static readonly Guid Evento1 = Guid.NewGuid();
    private static readonly Guid Evento2 = Guid.NewGuid();

    private static Rechazo Registrar(Guid? eventoId = null, int? diasAborto = null, Guid? evento2 = null, string? obs2 = null)
        => Rechazo.Registrar(Animal, Ahora, 1, "obs1", Ahora, eventoId, diasAborto, evento2, obs2);

    [Fact]
    public void Un_solo_evento_sigue_sin_Evento_2()
    {
        var r = Registrar(eventoId: Evento1);
        Assert.Null(r.Evento2Id);
        Assert.Null(r.Observaciones2);
    }

    [Fact]
    public void Guarda_el_Evento_2_y_su_observacion()
    {
        var r = Registrar(eventoId: Evento1, evento2: Evento2, obs2: " dosis ");
        Assert.Equal(Evento2, r.Evento2Id);
        Assert.Equal("dosis", r.Observaciones2);
        Assert.Equal("obs1", r.Observaciones);
    }

    [Fact]
    public void El_Rechazo_fijo_no_admite_Evento_2()
        => Assert.Throws<DominioException>(() => Registrar(eventoId: null, evento2: Evento2));

    [Fact]
    public void Un_Aborto_con_resultado_Rechazo_no_admite_Evento_2()
        => Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, diasAborto: 30, evento2: Evento2));

    [Fact]
    public void Rechaza_el_mismo_evento_dos_veces()
        => Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, evento2: Evento1));

    [Fact]
    public void Rechaza_una_observacion_2_sin_Evento_2()
        => Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, obs2: "suelta"));

    [Fact]
    public void Acepta_300_y_rechaza_301_caracteres_en_la_observacion_2()
    {
        Assert.NotNull(Registrar(eventoId: Evento1, evento2: Evento2, obs2: new string('a', 300)).Observaciones2);
        Assert.Throws<DominioException>(() => Registrar(eventoId: Evento1, evento2: Evento2, obs2: new string('a', 301)));
    }
}
