using Aviss.Application.Campo;
using Aviss.Application.Comun;
using Aviss.Domain.Enums;

namespace Aviss.Application.Tests.Campo;

/// <summary>RF-33: todos los códigos del flujo de campo tienen un mensaje fijo en español, sin datos de animales ni tambos.</summary>
public class MensajesCampoTests
{
    private static readonly CodigoError[] CodigosDeCampo =
    [
        CodigoError.NoHayMangaAbierta, CodigoError.CantidadInvalida, CodigoError.MarcaYaAnulada, CodigoError.MarcaNoEsLaUltima,
        CodigoError.AnimalConMarcasVigentes, CodigoError.CierreYaAnulado, CodigoError.CierreNoEsElUltimo,
        CodigoError.CierreAutomaticoNoSeDeshace, CodigoError.CierreDeOtroDia, CodigoError.HayOtraMangaAbierta,
        CodigoError.FechaAnteriorAlUltimoRegistro
    ];

    // Datos que jamás deben aparecer en un mensaje fijo.
    private static readonly string[] DatosProhibidos = ["12345", "Tambo", "La Estancia", "ñ1", "Vet Uno"];

    public static IEnumerable<object[]> Todos()
    {
        foreach (var c in CodigosDeCampo)
            yield return new object[] { $"CodigoError.{c}", MensajesCampo.Para(c) };
        foreach (var m in Enum.GetValues<MotivoRechazoMarca>())
            yield return new object[] { $"MotivoRechazoMarca.{m}", MensajesCampo.Para(m).Mensaje };
        foreach (var m in Enum.GetValues<MotivoNoReabrir>())
            yield return new object[] { $"MotivoNoReabrir.{m}", MensajesCampo.Para(m).Mensaje };
    }

    [Theory]
    [MemberData(nameof(Todos))]
    public void Cada_mensaje_existe_esta_en_espanol_y_sin_datos(string origen, string mensaje)
    {
        Assert.False(string.IsNullOrWhiteSpace(mensaje), origen);
        Assert.True(mensaje.Length > 15, origen);
        Assert.EndsWith(".", mensaje, StringComparison.Ordinal);
        Assert.Matches(@"(?i)\b(no|el|la|los|las|se|un|una|ya|de|del|con|sin)\b", mensaje);
        foreach (var dato in DatosProhibidos)
            Assert.DoesNotContain(dato, mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Los_motivos_se_traducen_al_codigo_que_corresponde()
    {
        Assert.Equal(CodigoError.FechaFutura, MensajesCampo.Para(MotivoRechazoMarca.FechaFutura).Codigo);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoRegistro, MensajesCampo.Para(MotivoRechazoMarca.FechaAnteriorAlUltimoRegistro).Codigo);
        Assert.Equal(CodigoError.AnimalAnulado, MensajesCampo.Para(MotivoRechazoMarca.AnimalAnulado).Codigo);
        Assert.Equal(CodigoError.HayOtraMangaAbierta, MensajesCampo.Para(MotivoNoReabrir.HayOtraMangaAbierta).Codigo);
        Assert.Equal(CodigoError.CierreAutomaticoNoSeDeshace, MensajesCampo.Para(MotivoNoReabrir.CierreAutomatico).Codigo);
    }

    [Fact]
    public void Los_mensajes_de_reloj_sugieren_revisar_la_hora()
    {
        Assert.Contains("hora", MensajesCampo.Para(MotivoRechazoMarca.FechaFutura).Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hora", MensajesCampo.Para(MotivoRechazoMarca.FechaAnteriorAlUltimoRegistro).Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hora", MensajesCampo.Para(CodigoError.FechaAnteriorAlUltimoRegistro), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Para_un_codigo_que_no_es_de_campo_lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MensajesCampo.Para(CodigoError.DatosInvalidos));
    }
}
