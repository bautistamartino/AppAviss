using Aviss.Application.Importacion;
using Aviss.Domain.Reglas;

namespace Aviss.Excel.Tests;

/// <summary>
/// La muestra de 226 vacas se parece al Excel real de Dairy Comp (spec 003, RF-14): las fechas son texto con espacios
/// y <c>VINS</c> es un número (cantidad de inseminaciones), no una fecha.
/// </summary>
public class FechasMuestraTests
{
    private static LecturaExcel LeerMuestra()
    {
        var r = new PerfilDairyComp().Leer(GeneradorMuestras.Muestra226Vacas(), LimitesImportacion.MaxFilas);
        Assert.True(r.EsCorrecto);
        return r.Lectura!;
    }

    [Theory]
    [InlineData("FPART")]
    [InlineData("FCELO")]
    [InlineData("FAPAR")]
    public void Todas_las_fechas_de_la_muestra_se_interpretan_con_FechaExcel(string columna)
    {
        var lectura = LeerMuestra();
        Assert.Equal(226, lectura.Filas.Count);

        foreach (var fila in lectura.Filas)
        {
            var texto = fila.Celdas[columna].Texto;
            Assert.True(FechaExcel.TryParsear(texto, out _), $"Fila {fila.NumeroFila}: la fecha de {columna} no se interpreta");
        }
    }

    [Fact]
    public void VINS_es_un_numero_entero_y_no_una_fecha()
    {
        var lectura = LeerMuestra();

        foreach (var fila in lectura.Filas)
        {
            var celda = fila.Celdas["VINS"];
            Assert.Equal(TipoCelda.Numerica, celda.Tipo);
            Assert.NotNull(celda.Numero);
            // Entero y razonable: cantidad de inseminaciones de una vaca (0 a 6).
            Assert.Equal(Math.Floor(celda.Numero!.Value), celda.Numero.Value);
            Assert.InRange(celda.Numero.Value, 0, 6);
            Assert.DoesNotContain("/", celda.Texto);
        }
    }
}
