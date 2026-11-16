using Aviss.Domain.Catalogo;
using Aviss.Domain.Entidades;

namespace Aviss.Domain.Tests;

public class EstadoReproductivoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Crea_un_estado_valido_activo()
    {
        var e = EstadoReproductivo.Crear("PRENADA", "PREÑADA", "PREÑADA", Ahora);
        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal("PRENADA", e.Codigo);
        Assert.Equal("PREÑADA", e.Nombre);
        Assert.Equal("PREÑADA", e.CodigoDairyComp);
        Assert.True(e.Activo);
        Assert.Equal(Ahora, e.CreadoEnUtc);
        Assert.Equal(Ahora, e.ActualizadoEnUtc);
    }

    [Theory]
    [InlineData("A_1")]
    [InlineData("VACIA")]
    [InlineData("X9")]
    public void Acepta_codigos_en_mayuscula_sin_acentos_ni_espacios(string codigo)
    {
        Assert.Equal(codigo, EstadoReproductivo.Crear(codigo, "Algo", "ALGO", Ahora).Codigo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("prenada")]   // minúsculas
    [InlineData("PREÑADA")]   // Ñ no es A-Z
    [InlineData("EN CELO")]   // espacio
    [InlineData("ÁREA")]      // acento
    [InlineData("A-B")]       // guion
    public void Rechaza_codigos_con_formato_invalido(string codigo)
    {
        Assert.Throws<DominioException>(() => EstadoReproductivo.Crear(codigo, "Algo", "ALGO", Ahora));
    }

    [Fact]
    public void Rechaza_nombre_o_codigo_dairy_comp_vacios()
    {
        Assert.Throws<DominioException>(() => EstadoReproductivo.Crear("A", " ", "A", Ahora));
        Assert.Throws<DominioException>(() => EstadoReproductivo.Crear("A", "A", " ", Ahora));
    }

    [Fact]
    public void Rechaza_fecha_que_no_es_utc()
    {
        Assert.Throws<DominioException>(() =>
            EstadoReproductivo.Crear("A", "A", "A", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Nombre_clave_ignora_mayusculas_incluida_la_enie()
    {
        var a = EstadoReproductivo.Crear("A", "Preñada", "A", Ahora);
        var b = EstadoReproductivo.Crear("B", "PREÑADA", "B", Ahora);
        Assert.Equal(a.NombreClave, b.NombreClave);
    }

    [Fact]
    public void Modificar_no_cambia_el_codigo_interno_y_actualiza_la_fecha()
    {
        var e = EstadoReproductivo.Crear("PRENADA", "PREÑADA", "PREÑADA", Ahora);
        var despues = Ahora.AddHours(1);
        e.Modificar("Preñada confirmada", "PREÑ", despues);
        Assert.Equal("PRENADA", e.Codigo);
        Assert.Equal("Preñada confirmada", e.Nombre);
        Assert.Equal("PREÑ", e.CodigoDairyComp);
        Assert.Equal(despues, e.ActualizadoEnUtc);
        Assert.Equal(Ahora, e.CreadoEnUtc);
    }

    [Fact]
    public void Desactivar_y_activar()
    {
        var e = EstadoReproductivo.Crear("A", "A", "A", Ahora);
        e.Desactivar(Ahora.AddMinutes(1));
        Assert.False(e.Activo);
        e.Activar(Ahora.AddMinutes(2));
        Assert.True(e.Activo);
    }

    [Fact]
    public void Estados_iniciales_son_los_seis_de_dairy_comp_con_codigo_igual_al_nombre()
    {
        Assert.Equal(6, EstadosIniciales.Todos.Count);
        Assert.Contains(EstadosIniciales.Todos, x => x.Codigo == "PRENADA" && x.Nombre == "PREÑADA");
        foreach (var inicial in EstadosIniciales.Todos)
        {
            Assert.Equal(inicial.Nombre, inicial.CodigoDairyComp);
            // todos deben poder crearse con las reglas del catálogo
            EstadoReproductivo.Crear(inicial.Codigo, inicial.Nombre, inicial.CodigoDairyComp, Ahora);
        }
        var codigos = EstadosIniciales.Todos.Select(x => x.Codigo).ToList();
        Assert.Equal(new[] { "FRESCA", "INSEMIN", "NOINSEM", "PRENADA", "SECA", "VACIA" }, codigos.Order(StringComparer.Ordinal));
    }
}
