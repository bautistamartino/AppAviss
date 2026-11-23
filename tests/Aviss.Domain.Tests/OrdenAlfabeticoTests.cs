using Aviss.Domain.Reglas;

namespace Aviss.Domain.Tests;

/// <summary>Spec 005, RF-7: orden alfabético sin mayúsculas ni tildes, con la Ñ entre la N y la O.</summary>
public class OrdenAlfabeticoTests
{
    private static string[] Ordenar(params string[] nombres) => nombres.OrderBy(n => n, OrdenAlfabetico.Comparador).ToArray();

    [Fact]
    public void Ordena_los_ejemplos_de_RF7()
    {
        var r = Ordenar("SECA", "PROSTAG", "INSEMIN", "ANESTRO", "FRESCA", "ENDOM1");
        Assert.Equal(["ANESTRO", "ENDOM1", "FRESCA", "INSEMIN", "PROSTAG", "SECA"], r);
    }

    [Fact]
    public void No_distingue_mayusculas_ni_tildes()
    {
        Assert.Equal(OrdenAlfabetico.Clave("Abortó"), OrdenAlfabetico.Clave("ABORTO"));
        Assert.Equal(OrdenAlfabetico.Clave("revisar"), OrdenAlfabetico.Clave("REVISAR"));
        // "Árbol" va con la A, no después de la Z como pasaría comparando por código de carácter.
        Assert.Equal(["Árbol", "Bola", "Zeta"], Ordenar("Zeta", "Bola", "Árbol"));
    }

    [Fact]
    public void La_enie_queda_entre_la_N_y_la_O()
    {
        var r = Ordenar("OTRO", "Ñandú", "NZ", "NA", "MA");
        Assert.Equal(["MA", "NA", "NZ", "Ñandú", "OTRO"], r);
    }

    [Fact]
    public void La_enie_descompuesta_se_trata_igual()
    {
        var descompuesta = "ñu"; // n + tilde combinante
        Assert.Equal(OrdenAlfabetico.Clave("ñu"), OrdenAlfabetico.Clave(descompuesta));
    }

    [Fact]
    public void Dos_nombres_iguales_con_esta_comparacion_dan_la_misma_clave()
    {
        // El desempate por creación lo hace quien ordena (OrderBy es estable); acá solo se comprueba el empate.
        Assert.Equal(0, OrdenAlfabetico.Comparador.Compare("Aborto", "ABORTÓ"));
    }

    [Fact]
    public void Nulo_o_vacio_da_clave_vacia()
    {
        Assert.Equal("", OrdenAlfabetico.Clave(null));
        Assert.Equal("", OrdenAlfabetico.Clave(""));
    }
}
