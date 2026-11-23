using Aviss.Domain;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

public class ReglasEsperaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, true)]   // entra en tacto y no está en espera: se puede
    [InlineData(false, false, false)] // CVET 0: no
    [InlineData(true, true, false)]   // ya en espera: no
    [InlineData(false, true, false)]
    public void Poner_en_espera_exige_que_entre_en_tacto_y_que_no_este_ya(bool entra, bool yaEnEspera, bool esperado)
        => Assert.Equal(esperado, ReglasEspera.PuedePonerseEnEspera(entra, yaEnEspera));

    [Fact]
    public void Una_vaca_en_espera_no_se_puede_desmarcar()
    {
        Assert.False(ReglasEspera.PuedeDesmarcarse(enEspera: true));
        Assert.True(ReglasEspera.PuedeDesmarcarse(enEspera: false));
    }

    // ---- Entidad Espera ----

    private static Espera Nueva() => Espera.Crear(Guid.NewGuid(), Ahora, Guid.NewGuid(), Guid.NewGuid(), Ahora);

    [Fact]
    public void Una_espera_nueva_esta_pendiente()
    {
        var e = Nueva();
        Assert.Equal(EstadoEspera.Pendiente, e.Estado);
        Assert.Null(e.ResueltaEnUtc);
        Assert.Null(e.RegistroResolucionId);
    }

    [Fact]
    public void Resolver_guarda_quien_cuando_y_el_registro()
    {
        var e = Nueva();
        var registro = Guid.NewGuid();
        var vet = Guid.NewGuid();
        e.Resolver(registro, vet, Ahora.AddMinutes(5));
        Assert.Equal(EstadoEspera.Resuelta, e.Estado);
        Assert.Equal(registro, e.RegistroResolucionId);
        Assert.Equal(vet, e.ResueltaPorUsuarioId);
        Assert.Equal(Ahora.AddMinutes(5), e.ResueltaEnUtc);
    }

    [Fact]
    public void Saltear_no_guarda_registro()
    {
        var e = Nueva();
        e.Saltear(Guid.NewGuid(), Ahora.AddMinutes(1));
        Assert.Equal(EstadoEspera.Salteada, e.Estado);
        Assert.Null(e.RegistroResolucionId);
        Assert.NotNull(e.ResueltaEnUtc);
    }

    [Fact]
    public void Una_espera_cerrada_no_se_vuelve_a_cerrar()
    {
        var e = Nueva();
        e.Saltear(Guid.NewGuid(), Ahora);
        Assert.Throws<DominioException>(() => e.Saltear(Guid.NewGuid(), Ahora));
        Assert.Throws<DominioException>(() => e.Resolver(Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Crear_valida_ids_y_fecha_utc()
    {
        Assert.Throws<DominioException>(() => Espera.Crear(Guid.Empty, Ahora, Guid.NewGuid(), Guid.NewGuid(), Ahora));
        Assert.Throws<DominioException>(() => Espera.Crear(Guid.NewGuid(), DateTime.Now, Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    // ---- Entidad Rechazo ----

    [Fact]
    public void Rechazo_recorta_la_observacion_y_limita_el_largo()
    {
        var r = Rechazo.Registrar(Guid.NewGuid(), Ahora, 1, "  cojera  ", Guid.NewGuid(), Guid.NewGuid(), Ahora);
        Assert.Equal("cojera", r.Observaciones);
        Assert.Null(Rechazo.Registrar(Guid.NewGuid(), Ahora, 2, "  ", Guid.NewGuid(), Guid.NewGuid(), Ahora).Observaciones);
        Assert.Throws<DominioException>(() =>
            Rechazo.Registrar(Guid.NewGuid(), Ahora, 3, new string('x', 301), Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Rechazo_se_anula_una_sola_vez()
    {
        var r = Rechazo.Registrar(Guid.NewGuid(), Ahora, 1, null, Guid.NewGuid(), Guid.NewGuid(), Ahora);
        var vet = Guid.NewGuid();
        r.MarcarAnulado(vet, Ahora.AddMinutes(1));
        Assert.True(r.Anulado);
        Assert.Equal(vet, r.AnuladoPorUsuarioId);
        Assert.Throws<DominioException>(() => r.MarcarAnulado(vet, Ahora));
    }
}
