using Aviss.Domain.Entidades;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.EntityFrameworkCore;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-6 y RF-7b: los datos iniciales se agregan por código, sin pisar lo que el veterinario cambió.</summary>
public sealed class SembradorInicialTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _base = new();

    public void Dispose() => _base.Dispose();

    private async Task SembrarEstadosAsync()
    {
        using var ctx = _base.NuevoContexto();
        await new SembradorInicial(_base.Reloj).SembrarEstadosAsync(ctx);
    }

    [Fact]
    public async Task Siembra_los_seis_estados_con_codigo_de_dairy_comp_igual_al_nombre()
    {
        await SembrarEstadosAsync();

        Assert.Equal(6, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal("PREÑADA", _base.Escalar("SELECT Nombre FROM EstadoReproductivo WHERE Codigo = 'PRENADA'"));
        Assert.Equal("PREÑADA", _base.Escalar("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Codigo = 'PRENADA'"));
        Assert.Equal(6, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Nombre = CodigoDairyComp AND Activo = 1"));
    }

    [Fact]
    public async Task La_segunda_corrida_no_duplica()
    {
        await SembrarEstadosAsync();
        await SembrarEstadosAsync();

        Assert.Equal(6, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task No_pisa_un_estado_modificado_ni_reactiva_uno_desactivado()
    {
        await SembrarEstadosAsync();
        _base.Ejecutar("UPDATE EstadoReproductivo SET Nombre = 'Seca larga', CodigoDairyComp = 'SECA2' WHERE Codigo = 'SECA'");
        _base.Ejecutar("UPDATE EstadoReproductivo SET Activo = 0 WHERE Codigo = 'VACIA'");

        await SembrarEstadosAsync();

        Assert.Equal(6, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal("Seca larga", _base.Escalar("SELECT Nombre FROM EstadoReproductivo WHERE Codigo = 'SECA'"));
        Assert.Equal("SECA2", _base.Escalar("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Codigo = 'SECA'"));
        Assert.Equal(0, _base.Contar("SELECT Activo FROM EstadoReproductivo WHERE Codigo = 'VACIA'"));
    }

    [Fact]
    public async Task Agrega_solo_los_que_faltan()
    {
        _base.Estado("SECA", "SECA", "SECA");

        await SembrarEstadosAsync();

        Assert.Equal(6, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal(1, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Codigo = 'SECA'"));
    }

    [Fact]
    public async Task Un_nombre_ya_usado_por_otro_estado_no_frena_el_arranque()
    {
        // Un estado propio con el nombre "Insemin" no debe romper la siembra de "INSEMIN" por la unicidad del nombre.
        _base.Estado("PROPIO", "Insemin", "PROPIO");

        await SembrarEstadosAsync();

        Assert.Equal(0, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Codigo = 'INSEMIN'"));
        Assert.Equal(5, _base.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Codigo <> 'PROPIO'"));
    }
}
