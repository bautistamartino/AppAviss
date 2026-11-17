using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Domain.Entidades;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>Spec 003, RF-1: el nombre de un veterinario no se repite, en la app y en la base.</summary>
public sealed class UsuarioNombreUnicoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new(conDatosBase: false);
    public void Dispose() => _e.Dispose();

    private Task<Resultado<Guid>> Crear(string nombre) => _e.Servicio<IUsuarioService>().CrearAsync(nombre, null);

    [Theory]
    [InlineData("Dra. Pérez", "Dra. Pérez")]
    [InlineData("Dra. Pérez", "DRA. PÉREZ")]      // otras mayúsculas (con tilde)
    [InlineData("Dra. Pérez", "dra. pérez")]
    [InlineData("Dra. Pérez", "  Dra. Pérez  ")]  // espacios de los extremos
    [InlineData("Ñandú", "ñANDÚ")]
    public async Task Un_nombre_repetido_se_rechaza_como_duplicado(string primero, string segundo)
    {
        Assert.True((await Crear(primero)).EsCorrecto);
        var r = await Crear(segundo);
        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task El_mensaje_es_fijo_y_no_lleva_el_nombre()
    {
        await Crear("Dra. Pérez");
        var r = await Crear("dra. pérez");
        Assert.Equal("Ya hay un veterinario con ese nombre.", r.Mensaje);
        Assert.DoesNotContain("rez", r.Mensaje);
    }

    [Fact]
    public async Task Un_nombre_parecido_pero_distinto_se_acepta()
    {
        Assert.True((await Crear("Dra. Pérez")).EsCorrecto);
        Assert.True((await Crear("Dra. Perez")).EsCorrecto);   // sin tilde: otro nombre
        Assert.True((await Crear("Dra. Pérez 2")).EsCorrecto);
        Assert.Equal(3, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task Se_rechaza_tambien_contra_un_usuario_de_baja()
    {
        var id = (await Crear("Dr. López")).Valor;
        await _e.Servicio<IUsuarioService>().DarDeBajaAsync(id);

        var r = await Crear("dr. LÓPEZ");
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task Se_rechaza_contra_Sin_asignar()
    {
        _e.Base.Usuario(Usuario.NombreSinAsignar, id: Usuario.SinAsignarId);
        var r = await Crear("SIN ASIGNAR");
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(CodigoError.Duplicado, (await Crear(" sin asignar ")).Codigo);
        Assert.Equal(1, _e.Cuenta("Usuario"));
    }

    [Fact]
    public async Task Un_nombre_vacio_sigue_siendo_datos_invalidos_y_no_duplicado()
    {
        await Crear("A");
        Assert.Equal(CodigoError.DatosInvalidos, (await Crear("   ")).Codigo);
    }

    [Fact]
    public async Task El_chequeo_de_la_app_es_coherente_con_el_indice_de_la_base()
    {
        // Todo nombre que la app acepta tiene que poder insertarse en la base, y viceversa.
        var nombres = new[] { "Ana", "ANA ", "Ñu", "ñU", "Ángel", "ángel", "Ünico", "ünico", "Çesar", "çesar" };
        foreach (var n in nombres)
        {
            var r = await Crear(n);
            if (r.EsCorrecto)
                Assert.Throws<SqliteException>(() => _e.Base.Usuario(n));   // la base rechaza el mismo nombre otra vez
            else
                Assert.Equal(CodigoError.Duplicado, r.Codigo);
        }
        // "Ana" y "ANA " chocan; "Ñu"/"ñU", "Ángel"/"ángel" y "Ünico"/"ünico" también; "Çesar" y "çesar" NO
        // (la regla solo conoce ñ, tildes y ü, igual que la caravana): quedan como dos veterinarios.
        Assert.Equal(6, _e.Cuenta("Usuario"));
    }

    // ---------- Directo en la base (RF-1: "también en la base") ----------

    [Theory]
    [InlineData("Vet", "VET")]
    [InlineData("Vet", "  vet  ")]
    [InlineData("Ñandú", "ñandú")]
    [InlineData("Pérez", "PÉREZ")]
    public void La_base_rechaza_un_nombre_repetido_aunque_no_pase_por_la_app(string primero, string segundo)
    {
        _e.Base.Usuario(primero);
        Assert.Throws<SqliteException>(() => _e.Base.Usuario(segundo));
        Assert.Equal(1, _e.Base.Contar("SELECT COUNT(*) FROM Usuario"));
    }

    [Fact]
    public void La_base_rechaza_tambien_a_un_usuario_de_baja_y_a_Sin_asignar()
    {
        _e.Base.Usuario("Baja", activo: false);
        Assert.Throws<SqliteException>(() => _e.Base.Usuario("baja"));
        _e.Base.Usuario(Usuario.NombreSinAsignar, id: Usuario.SinAsignarId);
        Assert.Throws<SqliteException>(() => _e.Base.Usuario("sin asignar"));
    }

    [Fact]
    public void La_base_rechaza_renombrar_a_un_nombre_que_ya_existe()
    {
        _e.Base.Usuario("Uno");
        var dos = _e.Base.Usuario("Dos");
        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("UPDATE Usuario SET Nombre = 'UNO ' WHERE Id = $id", ("$id", dos)));
    }
}
