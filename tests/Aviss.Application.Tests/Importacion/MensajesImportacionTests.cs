using Aviss.Application.Comun;
using Aviss.Application.Importacion;

namespace Aviss.Application.Tests.Importacion;

/// <summary>RF-24: todos los mensajes fijos existen, están en español y no llevan datos de animales ni de tambos.</summary>
public class MensajesImportacionTests
{
    private static readonly CodigoError[] CodigosNuevos =
    [
        CodigoError.ArchivoRechazado, CodigoError.VistaPreviaNoDisponible, CodigoError.ImportacionEnCurso,
        CodigoError.ImportacionYaConfirmada, CodigoError.ArchivoRepetidoSinConfirmar, CodigoError.SinFilasValidas
    ];

    // Datos de prueba que jamás deben aparecer en un mensaje fijo.
    private static readonly string[] DatosProhibidos = ["12345", "Tambo", "La Estancia", "ñ1"];

    public static IEnumerable<object[]> TodosLosMensajes()
    {
        foreach (var m in Enum.GetValues<MotivoFila>())
            yield return new object[] { $"MotivoFila.{m}", MensajesImportacion.Para(m) };
        foreach (var m in Enum.GetValues<MotivoRechazoArchivo>())
            yield return new object[] { $"MotivoRechazoArchivo.{m}", MensajesImportacion.Para(m) };
        foreach (var c in CodigosNuevos)
            yield return new object[] { $"CodigoError.{c}", MensajesImportacion.Para(c) };
        yield return new object[] { "EstablecimientoRepetido", MensajesImportacion.EstablecimientoRepetido };
    }

    [Theory]
    [MemberData(nameof(TodosLosMensajes))]
    public void Cada_mensaje_existe_esta_en_espanol_y_sin_datos(string origen, string mensaje)
    {
        Assert.False(string.IsNullOrWhiteSpace(mensaje), origen);
        Assert.True(mensaje.Length > 15, origen);
        Assert.EndsWith(".", mensaje, StringComparison.Ordinal);
        // Español: alguna palabra de uso común (los mensajes están redactados con "no", "el", "la", "se", etc.).
        Assert.Matches(@"(?i)\b(no|el|la|los|las|se|un|una|ya|de|del|con|sin)\b", mensaje);
        foreach (var dato in DatosProhibidos)
            Assert.DoesNotContain(dato, mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Los_codigos_de_motivo_de_fila_se_corresponden_con_los_de_la_spec()
    {
        Assert.Equal(
            ["IdVacio", "IdCaracteres", "IdLargo", "IdNumeroInvalido", "IdTipoInvalido", "IdRepetido"],
            Enum.GetNames<MotivoFila>());
    }

    [Fact]
    public void Para_un_codigo_que_no_es_de_importacion_lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MensajesImportacion.Para(CodigoError.DatosInvalidos));
    }

    [Fact]
    public void Mensaje_de_columna_faltante_nombra_la_columna_pero_el_resto_es_fijo()
    {
        var m = MensajesImportacion.Para(MotivoRechazoArchivo.FaltaColumna, "RPRO");
        Assert.Contains("RPRO", m);
        Assert.Equal(MensajesImportacion.Para(MotivoRechazoArchivo.FaltaColumna), MensajesImportacion.Para(MotivoRechazoArchivo.FaltaColumna, null));
    }

    [Fact]
    public void El_mensaje_de_establecimiento_repetido_es_el_de_la_spec()
    {
        Assert.Equal("Ya existe un establecimiento con ese nombre; elegilo de la lista.", MensajesImportacion.EstablecimientoRepetido);
    }
}
