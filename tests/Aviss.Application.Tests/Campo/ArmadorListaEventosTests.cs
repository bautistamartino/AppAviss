using Aviss.Application.Campo;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;

namespace Aviss.Application.Tests.Campo;

/// <summary>Spec 005, RF-5, RF-6, RF-7, RF-11, RF-13: la lista de dos niveles que arma la función pura.</summary>
public sealed class ArmadorListaEventosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static List<EstadoReproductivo> Estandar() =>
        EstadosIniciales.Todos.Select(i => EstadoReproductivo.CrearEstandar(i, Ahora, i.DestinoClave is null ? null : Guid.NewGuid())).ToList();

    private static EstadoReproductivo ABano(string nombre, int minutos = 0) =>
        EstadoReproductivo.Crear("E" + Guid.NewGuid().ToString("N").ToUpperInvariant(), nombre, nombre, Ahora.AddMinutes(minutos));

    [Fact]
    public void Los_principales_van_en_su_orden_y_Otros_empieza_con_Rechazo_y_sigue_alfabetico()
    {
        var lista = ArmadorListaEventos.Armar(Estandar());

        Assert.Equal(["Revisar Nuevamente", "OK", "VACIA", "Preñada fecha correcta", "Preñada fecha previa", "Aborto", "NOINSEM"], lista.Principales.Select(e => e.Nombre));
        Assert.Equal(["Rechazo", "FRESCA", "INSEMIN", "SECA"], lista.Otros.Select(e => e.Nombre));
        Assert.Equal(EventosCampo.Rechazo, lista.Otros[0].Id);
    }

    [Fact]
    public void CambiaEstado_distingue_estados_de_solo_eventos()
    {
        var lista = ArmadorListaEventos.Armar(Estandar());
        var porNombre = lista.Principales.Concat(lista.Otros).ToDictionary(e => e.Nombre);

        Assert.False(porNombre["OK"].CambiaEstado);
        Assert.True(porNombre["Aborto"].CambiaEstado);       // desde la segunda vuelta Aborto cambia el estado (según su resultado)
        Assert.False(porNombre["Rechazo"].CambiaEstado);
        Assert.True(porNombre["VACIA"].CambiaEstado);
        Assert.True(porNombre["SECA"].CambiaEstado);
        Assert.All(lista.Principales, e => Assert.True(e.EsPrincipal));
        Assert.All(lista.Otros, e => Assert.False(e.EsPrincipal));
    }

    [Fact]
    public void Los_de_Otro_evento_van_por_nombre_sin_tildes_ni_mayusculas_con_la_Enie_entre_N_y_O()
    {
        var catalogo = Estandar();
        catalogo.AddRange([ABano("Oro"), ABano("Ñandú"), ABano("Nube"), ABano("ánimo"), ABano("zeta")]);

        var otros = ArmadorListaEventos.Armar(catalogo).Otros.Select(e => e.Nombre).ToList();

        Assert.Equal(["Rechazo", "ánimo", "FRESCA", "INSEMIN", "Nube", "Ñandú", "Oro", "SECA", "zeta"], otros);
    }

    [Fact]
    public void Con_el_mismo_nombre_desempata_por_creacion()
    {
        var a = ABano("Celo", 5);
        var b = EstadoReproductivo.Crear("CELO_2", "celo", "CELO2", Ahora.AddMinutes(1));

        var otros = ArmadorListaEventos.Armar([a, b]).Otros;

        Assert.Equal([b.Id, a.Id], otros.Skip(1).Select(e => e.Id));
    }

    [Fact]
    public void Un_desactivado_no_aparece_y_no_se_reemplaza()
    {
        var catalogo = Estandar();
        catalogo.Single(e => e.ClaveEstandar == "OK").Desactivar(Ahora);
        catalogo.Single(e => e.ClaveEstandar == "SECA").Desactivar(Ahora);

        var lista = ArmadorListaEventos.Armar(catalogo);

        Assert.Equal(6, lista.Principales.Count);
        Assert.DoesNotContain(lista.Principales, e => e.Nombre == "OK");
        Assert.DoesNotContain(lista.Otros, e => e.Nombre == "SECA");
    }

    [Fact]
    public void Renombrar_un_principal_no_lo_mueve()
    {
        var catalogo = Estandar();
        catalogo.Single(e => e.ClaveEstandar == "OK").Modificar("ZZ Todo bien", "OK", Ahora);

        var nombres = ArmadorListaEventos.Armar(catalogo).Principales.Select(e => e.Nombre).ToList();

        Assert.Equal(1, nombres.IndexOf("ZZ Todo bien"));
    }

    [Fact]
    public void Con_todos_los_principales_desactivados_la_lista_queda_vacia_y_Otros_conserva_Rechazo()
    {
        var catalogo = Estandar();
        foreach (var e in catalogo.Where(e => e.EsPrincipal)) e.Desactivar(Ahora);

        var lista = ArmadorListaEventos.Armar(catalogo);

        Assert.Empty(lista.Principales);
        Assert.Equal("Rechazo", lista.Otros[0].Nombre);
    }

    [Fact]
    public void Un_evento_creado_a_mano_va_a_Otros_como_estado()
    {
        var lista = ArmadorListaEventos.Armar([ABano("Celo")]);

        Assert.Empty(lista.Principales);
        Assert.Equal(["Rechazo", "Celo"], lista.Otros.Select(e => e.Nombre));
        Assert.True(lista.Otros[1].CambiaEstado);
    }
}
