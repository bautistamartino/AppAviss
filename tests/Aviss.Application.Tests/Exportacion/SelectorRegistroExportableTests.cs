using Aviss.Application.Exportacion;

namespace Aviss.Application.Tests.Exportacion;

/// <summary>Spec 007, RF-12: de los registros vigentes de una vaca sale el último (fecha y hora; si empatan, el que se guardó después).</summary>
public class SelectorRegistroExportableTests
{
    [Fact]
    public void Sin_registros_no_hay_nada_que_exportar()
        => Assert.Null(SelectorRegistroExportable.Elegir([]));

    [Fact]
    public void Un_solo_registro_es_el_elegido()
    {
        var r = R.Reg();
        Assert.Same(r, SelectorRegistroExportable.Elegir([r]));
    }

    [Fact]
    public void Gana_el_de_fecha_y_hora_mas_reciente_aunque_se_haya_guardado_antes()
    {
        var viejoGuardadoDespues = R.Reg(fecha: R.Utc(2026, 11, 5, 9), orden: 9, evento: "VACIA");
        var nuevo = R.Reg(fecha: R.Utc(2026, 11, 5, 10), orden: 2, evento: "OK");
        Assert.Same(nuevo, SelectorRegistroExportable.Elegir([viejoGuardadoDespues, nuevo]));
    }

    [Fact]
    public void Con_la_misma_fecha_y_hora_gana_el_que_se_guardo_despues()
    {
        var a = R.Reg(orden: 5, evento: "VACIA");
        var b = R.Reg(orden: 6, evento: "OK");
        Assert.Same(b, SelectorRegistroExportable.Elegir([a, b]));
        Assert.Same(b, SelectorRegistroExportable.Elegir([b, a]));   // no depende del orden de la lista
    }

    [Fact]
    public void Un_tacto_corregido_con_Editar_exporta_el_nuevo()
    {
        // "Editar" anula el viejo (que la consulta ya no trae) y guarda uno nuevo: llegan solo los vigentes.
        var nuevo = R.Reg(fecha: R.Utc(2026, 11, 5, 11), orden: 8, evento: "Aborto", diasAborto: 30);
        Assert.Same(nuevo, SelectorRegistroExportable.Elegir([nuevo]));
    }

    [Fact]
    public void Compara_tactos_y_rechazos_juntos()
    {
        var tacto = R.Reg(orden: 1, evento: "VACIA");
        var rechazo = R.Reg(orden: 2, evento: "RECHAZO", tipo: TipoRegistroExportable.Rechazo);
        Assert.Same(rechazo, SelectorRegistroExportable.Elegir([tacto, rechazo]));
    }
}
