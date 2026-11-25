using Aviss.Application.Campo;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Tests.Campo;

/// <summary>
/// Spec 011 (dos eventos por tacto): las banderas de la lista de eventos (RF-1, RF-3), el texto con los dos eventos unidos
/// por " + " (RF-17) y los mensajes de rechazo del servicio (RF-12, RF-24).
/// </summary>
public sealed class DosEventosApplicationTests
{
    private static readonly DateTime Ahora = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private static List<EstadoReproductivo> Estandar() =>
        EstadosIniciales.Todos.Select(i => EstadoReproductivo.CrearEstandar(i, Ahora, i.DestinoClave is null ? null : Guid.NewGuid())).ToList();

    // ---------- ArmadorListaEventos: las banderas ----------

    [Theory]
    [InlineData("OK", true, true)]
    [InlineData("Revisar Nuevamente", true, true)]
    [InlineData("Preñada fecha correcta", true, false)]   // admite un segundo pero no puede ser el segundo (estándar de estado)
    [InlineData("VACIA", true, false)]
    [InlineData("NOINSEM", true, false)]
    [InlineData("INSEMIN", true, false)]
    [InlineData("SECA", true, false)]
    [InlineData("FRESCA", true, false)]
    [InlineData("Aborto", false, false)]                  // tiene pasos
    [InlineData("Preñada fecha previa", false, false)]    // tiene pasos y destino
    public void Las_banderas_del_catalogo_estandar(string nombre, bool admite, bool puedeSer)
    {
        var lista = ArmadorListaEventos.Armar(Estandar());
        var porNombre = lista.Principales.Concat(lista.Otros).ToDictionary(e => e.Nombre);
        // INSEMIN, SECA y FRESCA no están en la lista (van ocultos en "Otro evento"): se arman con el DTO directo.
        if (!porNombre.TryGetValue(nombre, out var dto))
        {
            var clave = nombre;
            var e = Estandar().Single(x => x.Nombre == clave);
            var datos = DatosEvento.De(e);
            Assert.Equal(admite, ReglasSegundoEvento.AdmiteSegundo(datos));
            Assert.Equal(puedeSer, ReglasSegundoEvento.PuedeSerSegundo(datos));
            return;
        }
        Assert.Equal(admite, dto.AdmiteSegundo);
        Assert.Equal(puedeSer, dto.PuedeSerSegundo);
    }

    [Fact]
    public void El_Rechazo_fijo_no_admite_ni_puede_ser_segundo()
    {
        Assert.False(ArmadorListaEventos.EventoRechazo.AdmiteSegundo);
        Assert.False(ArmadorListaEventos.EventoRechazo.PuedeSerSegundo);
    }

    [Fact]
    public void Un_evento_creado_a_mano_admite_y_puede_ser_segundo()
    {
        var propio = EstadoReproductivo.Crear("PROSTAG", "PROSTAG", "PROSTAG", Ahora);

        var dto = ArmadorListaEventos.Armar([propio]).Otros.Single(e => e.Nombre == "PROSTAG");

        Assert.True(dto.AdmiteSegundo);
        Assert.True(dto.PuedeSerSegundo);
    }

    // ---------- DescripcionEvento: la unión con " + " ----------

    [Theory]
    [InlineData("OK", "PROSTAG", "OK + PROSTAG")]
    [InlineData("VACIA", "OK", "VACIA + OK")]
    public void Los_dos_eventos_se_unen_con_mas(string primero, string segundo, string esperado)
        => Assert.Equal(esperado, DescripcionEvento.Armar(primero, nombreEvento2: segundo));

    [Fact]
    public void Sin_segundo_evento_el_texto_es_el_de_siempre()
    {
        Assert.Equal("OK", DescripcionEvento.Armar("OK", nombreEvento2: null));
        Assert.Equal("OK", DescripcionEvento.Armar("OK", nombreEvento2: "  "));
        Assert.Equal("Aborto, 45 días, Vacía OK", DescripcionEvento.Armar("Aborto", null, 45, ResultadoAborto.VaciaOk));
    }

    // ---------- Mensajes ----------

    [Theory]
    [InlineData(MotivoSegundoEvento.SegundoIgualAlPrimero)]
    [InlineData(MotivoSegundoEvento.PrimeroNoAdmiteSegundo)]
    [InlineData(MotivoSegundoEvento.SegundoNoCombinable)]
    [InlineData(MotivoSegundoEvento.SegundoDeBaja)]
    [InlineData(MotivoSegundoEvento.SegundoInexistente)]
    [InlineData(MotivoSegundoEvento.ObservacionLarga)]
    public void Cada_motivo_tiene_mensaje_sin_datos_del_tambo(MotivoSegundoEvento motivo)
    {
        var (_, mensaje) = MensajesCampo.Para(motivo);
        Assert.False(string.IsNullOrWhiteSpace(mensaje));
    }
}
