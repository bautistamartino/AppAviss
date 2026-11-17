using Aviss.Application.Importacion;

namespace Aviss.Application.Tests.Importacion;

public class AlmacenVistasPreviasTests
{
    private static VistaPreviaImportacion Vista(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "rodeo.xls", "abc", [1, 2, 3], "Dairy Comp", 1,
        [], [], [], [], []);

    [Fact]
    public void Guardada_queda_Pendiente_y_se_puede_tomar()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        Assert.Equal(EstadoVistaPrevia.Pendiente, almacen.EstadoDe(v.Id));

        Assert.Equal(ResultadoToma.Tomada, almacen.Tomar(v.Id, out var tomada));
        Assert.Same(v, tomada);
        Assert.Equal(EstadoVistaPrevia.Confirmando, almacen.EstadoDe(v.Id));
    }

    [Fact]
    public void Tomar_una_vista_desconocida_no_esta_disponible()
    {
        var almacen = new AlmacenVistasPrevias();
        Assert.Equal(ResultadoToma.NoDisponible, almacen.Tomar(Guid.NewGuid(), out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Tomar_mientras_confirma_devuelve_en_curso()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        almacen.Tomar(v.Id, out _);
        Assert.Equal(ResultadoToma.EnCurso, almacen.Tomar(v.Id, out var segunda));
        Assert.Null(segunda);
    }

    [Fact]
    public void Consumir_marca_Consumida_y_libera_los_bytes()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        almacen.Tomar(v.Id, out _);

        Assert.True(almacen.Consumir(v.Id));
        Assert.Equal(EstadoVistaPrevia.Consumida, almacen.EstadoDe(v.Id));
        Assert.Equal(ResultadoToma.YaConfirmada, almacen.Tomar(v.Id, out var otra));
        Assert.Null(otra);
    }

    [Fact]
    public void Devolver_tras_un_fallo_permite_reintentar()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        almacen.Tomar(v.Id, out _);

        Assert.True(almacen.Devolver(v.Id));
        Assert.Equal(EstadoVistaPrevia.Pendiente, almacen.EstadoDe(v.Id));
        Assert.Equal(ResultadoToma.Tomada, almacen.Tomar(v.Id, out _));
    }

    [Fact]
    public void Consumir_o_devolver_sin_haber_tomado_no_hace_nada()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        Assert.False(almacen.Consumir(v.Id));
        Assert.False(almacen.Devolver(v.Id));
        Assert.Equal(EstadoVistaPrevia.Pendiente, almacen.EstadoDe(v.Id));
    }

    [Fact]
    public void Guardar_una_segunda_descarta_la_primera()
    {
        var almacen = new AlmacenVistasPrevias();
        var a = Vista();
        var b = Vista();
        almacen.Guardar(a);
        almacen.Guardar(b);
        Assert.Null(almacen.EstadoDe(a.Id));
        Assert.Equal(ResultadoToma.NoDisponible, almacen.Tomar(a.Id, out _));
        Assert.Equal(ResultadoToma.Tomada, almacen.Tomar(b.Id, out _));
    }

    [Fact]
    public void Descartar_borra_una_pendiente_pero_no_una_que_se_esta_confirmando()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);
        Assert.True(almacen.Descartar(v.Id));
        Assert.Null(almacen.EstadoDe(v.Id));
        Assert.Equal(ResultadoToma.NoDisponible, almacen.Tomar(v.Id, out _));

        var w = Vista();
        almacen.Guardar(w);
        almacen.Tomar(w.Id, out _);
        Assert.False(almacen.Descartar(w.Id));
        Assert.Equal(EstadoVistaPrevia.Confirmando, almacen.EstadoDe(w.Id));
    }

    [Fact]
    public void Descartar_una_desconocida_devuelve_falso()
    {
        Assert.False(new AlmacenVistasPrevias().Descartar(Guid.NewGuid()));
    }

    [Fact]
    public async Task Tomas_concurrentes_de_la_misma_vista_solo_las_gana_una()
    {
        var almacen = new AlmacenVistasPrevias();
        var v = Vista();
        almacen.Guardar(v);

        using var arranque = new ManualResetEventSlim(false);
        var tareas = Enumerable.Range(0, 32).Select(n => Task.Run(() =>
        {
            arranque.Wait();
            return almacen.Tomar(v.Id, out _);
        })).ToArray();
        arranque.Set();
        var resultados = await Task.WhenAll(tareas);

        Assert.Equal(1, resultados.Count(r => r == ResultadoToma.Tomada));
        Assert.Equal(31, resultados.Count(r => r == ResultadoToma.EnCurso));
    }
}
