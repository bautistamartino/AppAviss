using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;

namespace Aviss.Infrastructure.Tests;

/// <summary>Spec 007, RF-21: guardar la constancia y encontrar la última de un rodeo, por el repositorio real y dentro de la unidad de trabajo.</summary>
public sealed class ExportacionRepositoryTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string Hash(char c = 'a') => new(c, 64);

    [Fact]
    public async Task Guardar_una_constancia_la_deja_en_la_base_con_todos_sus_datos()
    {
        var fecha = new DateTime(2026, 11, 24, 15, 30, 0, DateTimeKind.Utc);

        // Mismo scope para el repositorio y la unidad de trabajo (comparten el DbContext).
        var scope = _e.NuevoScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IExportacionRepository>();
        await uow.EjecutarAsync(() =>
        {
            repo.Agregar(Exportacion.Registrar(_e.RodeoId, fecha, "a_aviss_20261124_1230.xls", FormatoExportacion.Xls, Hash('d'), 226, 150, fecha));
            return Task.CompletedTask;
        });

        Assert.Equal(1, _e.Cuenta("Exportacion"));
        Assert.Equal("Xls", _e.Base.Escalar("SELECT Formato FROM Exportacion"));
        Assert.Equal("a_aviss_20261124_1230.xls", _e.Base.Escalar("SELECT ArchivoNombre FROM Exportacion"));
        Assert.Equal(226L, _e.Base.Escalar("SELECT CantidadFilas FROM Exportacion"));
        Assert.Equal(150L, _e.Base.Escalar("SELECT CantidadVacasConEvento FROM Exportacion"));
        Assert.Equal(Hash('d'), _e.Base.Escalar("SELECT Hash FROM Exportacion"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(fecha), _e.Base.Escalar("SELECT FechaUtc FROM Exportacion"));
    }

    [Fact]
    public async Task La_ultima_es_la_de_fecha_mas_reciente_del_rodeo_y_no_la_de_otro_rodeo()
    {
        var otro = _e.Base.Rodeo();
        var f = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        _e.Base.Exportacion(_e.RodeoId, f, "vieja.csv");
        _e.Base.Exportacion(_e.RodeoId, f.AddHours(5), "nueva.csv", "Xls");
        _e.Base.Exportacion(_e.RodeoId, f.AddHours(2), "media.csv");
        _e.Base.Exportacion(otro, f.AddHours(9), "ajena.csv");

        var ultima = await _e.Servicio<IExportacionRepository>().ObtenerUltimaAsync(_e.RodeoId);

        Assert.NotNull(ultima);
        Assert.Equal("nueva.csv", ultima!.ArchivoNombre);
        Assert.Equal(FormatoExportacion.Xls, ultima.Formato);
        Assert.Equal(f.AddHours(5), ultima.FechaUtc);
    }

    [Fact]
    public async Task Un_rodeo_que_nunca_se_exporto_no_tiene_ultima()
        => Assert.Null(await _e.Servicio<IExportacionRepository>().ObtenerUltimaAsync(_e.RodeoId));

    [Fact]
    public async Task Si_la_operacion_falla_la_constancia_no_queda()
    {
        var scope = _e.NuevoScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IExportacionRepository>();
        var fecha = new DateTime(2026, 11, 24, 15, 30, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.EjecutarAsync(() =>
        {
            repo.Agregar(Exportacion.Registrar(_e.RodeoId, fecha, "x.csv", FormatoExportacion.Csv, Hash(), 1, 1, fecha));
            throw new InvalidOperationException("falla");
        }));

        Assert.Equal(0, _e.Cuenta("Exportacion"));
    }
}
