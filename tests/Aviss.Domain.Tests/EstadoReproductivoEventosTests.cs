using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;

namespace Aviss.Domain.Tests;

/// <summary>Spec 005: tipo, principal, orden e identidad estándar del catálogo (RF-1 a RF-5, RF-20).</summary>
public class EstadoReproductivoEventosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static EstadoInicial Inicial(string codigo) => EstadosIniciales.Todos.Single(x => x.Codigo == codigo);

    [Fact]
    public void Crear_a_mano_deja_estado_no_principal_sin_clave_RF4()
    {
        var e = EstadoReproductivo.Crear("PROSTAG", "PROSTAG", "PROSTAG", Ahora);
        Assert.Equal(TipoEvento.Estado, e.Tipo);
        Assert.False(e.EsPrincipal);
        Assert.Equal(0, e.Orden);
        Assert.Null(e.ClaveEstandar);
    }

    [Fact]
    public void CrearEstandar_copia_clave_tipo_principal_y_orden()
    {
        var e = EstadoReproductivo.CrearEstandar(Inicial("OK"), Ahora);
        Assert.Equal("OK", e.ClaveEstandar);
        Assert.Equal("OK", e.Codigo);
        Assert.Equal(TipoEvento.SoloEvento, e.Tipo);
        Assert.True(e.EsPrincipal);
        Assert.Equal(2, e.Orden);
        Assert.True(e.Activo);
    }

    [Fact]
    public void Los_nueve_estandar_cumplen_RF1_y_RF3()
    {
        Assert.Equal(9, EstadosIniciales.Todos.Count);
        var principales = EstadosIniciales.Todos.Where(x => x.EsPrincipal).OrderBy(x => x.Orden).Select(x => x.Codigo);
        Assert.Equal(["REVISAR_NUEVAMENTE", "OK", "VACIA", "PRENADA", "ABORTO", "NOINSEM"], principales);
        Assert.Equal(
            ["ABORTO", "OK", "REVISAR_NUEVAMENTE"],
            EstadosIniciales.Todos.Where(x => x.Tipo == TipoEvento.SoloEvento).Select(x => x.Codigo).Order(StringComparer.Ordinal));
        Assert.Equal("REVISAR NUEVAMENTE", Inicial("REVISAR_NUEVAMENTE").CodigoDairyComp);
        Assert.Equal("ABORTO", Inicial("ABORTO").CodigoDairyComp);
        Assert.Equal("OK", Inicial("OK").CodigoDairyComp);
        foreach (var i in EstadosIniciales.Todos) EstadoReproductivo.CrearEstandar(i, Ahora); // todos son válidos
    }

    [Fact]
    public void Modificar_conserva_id_y_la_identidad_estandar_RF1_RF5()
    {
        var e = EstadoReproductivo.CrearEstandar(Inicial("ABORTO"), Ahora);
        var id = e.Id;

        e.Modificar("Aborto vaca", "ABORTO_REAL", Ahora.AddMinutes(1));

        Assert.Equal(id, e.Id);
        Assert.Equal("ABORTO", e.ClaveEstandar);
        Assert.Equal("ABORTO", e.Codigo);
        Assert.Equal(TipoEvento.SoloEvento, e.Tipo);
        Assert.True(e.EsPrincipal);
        Assert.Equal(5, e.Orden);
    }

    [Fact]
    public void Adoptar_no_cambia_nombre_codigo_ni_activo_RF20()
    {
        var aMano = EstadoReproductivo.Crear("ABORTO", "aborto", "Aborto vaca", Ahora);
        aMano.Desactivar(Ahora);

        aMano.AdoptarComoEstandar(Inicial("ABORTO"), Ahora.AddMinutes(5));

        Assert.Equal("aborto", aMano.Nombre);
        Assert.Equal("ABORTO", aMano.Codigo);
        Assert.Equal("Aborto vaca", aMano.CodigoDairyComp);
        Assert.False(aMano.Activo);
        Assert.Equal("ABORTO", aMano.ClaveEstandar);
        Assert.Equal(TipoEvento.SoloEvento, aMano.Tipo);
        Assert.True(aMano.EsPrincipal);
        Assert.Equal(5, aMano.Orden);
    }

    [Fact]
    public void Un_rechazo_puede_llevar_evento_y_por_defecto_no()
    {
        var animal = Guid.NewGuid();
        var vet = Guid.NewGuid();
        var disp = Guid.NewGuid();
        var evento = Guid.NewGuid();
        Assert.Null(Rechazo.Registrar(animal, Ahora, 1, null, vet, disp, Ahora).EventoId);
        Assert.Equal(evento, Rechazo.Registrar(animal, Ahora, 2, null, vet, disp, Ahora, evento).EventoId);
    }
}
