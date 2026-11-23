using Aviss.Application.Campo;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-38: cada acción nueva queda en el historial con su veterinario y su fecha, y el historial no se modifica.</summary>
public sealed class HistorialCampoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    /// <summary>Todas las filas de una acción: (Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo, Fecha, Usuario).</summary>
    private List<(string Entidad, string EntidadId, string Campo, string? Anterior, string? Nuevo, string Fecha, string Usuario)> Filas(string accion)
    {
        var filas = new List<(string, string, string, string?, string?, string, string)>();
        using var cmd = _e.Base.Cruda.CreateCommand();
        cmd.CommandText = "SELECT Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo, FechaHoraUtc, UsuarioId FROM Historial WHERE Accion = $a";
        cmd.Parameters.AddWithValue("$a", accion);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            filas.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6)));
        return filas;
    }

    private void ConVeterinarioYFecha((string Entidad, string EntidadId, string Campo, string? Anterior, string? Nuevo, string Fecha, string Usuario) f)
    {
        Assert.Equal(T(_e.UsuarioId), f.Usuario);
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), f.Fecha);
    }

    [Fact]
    public async Task La_marca_la_manga_y_su_anulacion_quedan_en_el_historial()
    {
        var (_, lectura) = await _e.VacaMarcadaAsync("100");
        var marca = lectura.MarcaId!.Value;
        var manga = _e.Base.Escalar("SELECT Id FROM Manga")!.ToString()!;

        var creada = Assert.Single(Filas("MarcaPasada"));
        ConVeterinarioYFecha(creada);
        Assert.Equal(("MarcaPasada", T(marca)), (creada.Entidad, creada.EntidadId));

        var abierta = Assert.Single(Filas("MangaAbierta"));
        ConVeterinarioYFecha(abierta);
        Assert.Equal(("Manga", manga), (abierta.Entidad, abierta.EntidadId));
        Assert.Equal("1", abierta.Nuevo); // el número de manga

        await _e.DeshacerAsync(TipoDeshacer.Marca, marca);
        var anulada = Assert.Single(Filas("AnulacionMarca"));
        ConVeterinarioYFecha(anulada);
        Assert.Equal(("MarcaPasada", T(marca), "0", "1"), (anulada.Entidad, anulada.EntidadId, anulada.Anterior, anulada.Nuevo));
    }

    [Fact]
    public async Task Cada_tipo_de_cierre_y_la_anulacion_del_cierre_quedan_en_el_historial()
    {
        await _e.VacaMarcadaAsync("1");
        var manual = (await _e.FinalizarAsync()).Cierre!;
        await _e.VacaMarcadaAsync("2");
        var termino = (await _e.Campo.TerminarElTactoAsync(new TerminarElTactoCommand(_e.RodeoId, null, _e.UsuarioId))).Valor.Cierre!;
        await _e.DeshacerAsync(TipoDeshacer.Cierre, termino.CierreId);

        var cerradas = Filas("MangaCerrada");
        Assert.Equal(2, cerradas.Count);
        Assert.All(cerradas, ConVeterinarioYFecha);
        Assert.All(cerradas, f => Assert.Equal("CierreManga", f.Entidad));
        Assert.Contains(cerradas, f => f.EntidadId == T(manual.CierreId) && f.Nuevo == "Manual");
        Assert.Contains(cerradas, f => f.EntidadId == T(termino.CierreId) && f.Nuevo == "TerminoElTacto");

        var anulacion = Assert.Single(Filas("AnulacionCierre"));
        ConVeterinarioYFecha(anulacion);
        Assert.Equal(("CierreManga", T(termino.CierreId)), (anulacion.Entidad, anulacion.EntidadId));
    }

    [Fact]
    public async Task El_cierre_automatico_se_registra_como_tal()
    {
        var ayer = _e.Reloj.UtcAhora.AddDays(-1);
        var animal = _e.AnimalImportado("100");
        var manga = _e.Base.Manga(_e.RodeoId, 1, _e.UsuarioId, creadaEn: ayer);
        _e.Base.MarcaPasada(animal, manga, _e.UsuarioId, fechaHora: ayer);

        await _e.Campo.IniciarTrabajoAsync(_e.RodeoId, _e.UsuarioId);

        var f = Assert.Single(Filas("MangaCerrada"));
        ConVeterinarioYFecha(f);
        Assert.Equal("Automatico", f.Nuevo);
    }

    [Fact]
    public async Task Las_cinco_acciones_de_la_spec_004_quedan_en_el_historial_con_veterinario_y_fecha()
    {
        var animal = _e.AnimalImportado("100", _e.EstadoSeca);
        var espera = (await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(animal, _e.UsuarioId))).Valor;
        var rechazo = (await _e.Campo.RegistrarRechazoAsync(new RegistrarRechazoCommand(animal, "pata", _e.UsuarioId))).Valor;
        await _e.DeshacerAsync(TipoDeshacer.Rechazo, rechazo);
        var otra = _e.AnimalImportado("200", _e.EstadoSeca);
        await _e.Campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(otra, _e.UsuarioId));
        await _e.Campo.SaltearEsperaAsync(new SaltearEsperaCommand(otra, _e.UsuarioId));

        var creada = Filas("EsperaCreada");
        Assert.Equal(2, creada.Count);
        creada.ForEach(ConVeterinarioYFecha);
        Assert.Contains(creada, f => (f.Entidad, f.EntidadId, f.Campo, f.Anterior, f.Nuevo) == ("Espera", T(espera), "Estado", null, "Pendiente"));

        var resuelta = Assert.Single(Filas("EsperaResuelta"));
        ConVeterinarioYFecha(resuelta);
        Assert.Equal(("Espera", T(espera), "Pendiente", "Resuelta"), (resuelta.Entidad, resuelta.EntidadId, resuelta.Anterior, resuelta.Nuevo));

        var salteada = Assert.Single(Filas("EsperaSalteada"));
        ConVeterinarioYFecha(salteada);
        Assert.Equal(("Pendiente", "Salteada"), (salteada.Anterior, salteada.Nuevo));

        var rechazoCreado = Assert.Single(Filas("RechazoCreado"));
        ConVeterinarioYFecha(rechazoCreado);
        Assert.Equal(("Rechazo", T(rechazo), "pata"), (rechazoCreado.Entidad, rechazoCreado.EntidadId, rechazoCreado.Nuevo));

        var anulado = Assert.Single(Filas("RechazoAnulado"));
        ConVeterinarioYFecha(anulado);
        Assert.Equal(("Rechazo", T(rechazo), "0", "1"), (anulado.Entidad, anulado.EntidadId, anulado.Anterior, anulado.Nuevo));
    }

    [Fact]
    public async Task El_historial_nuevo_no_se_puede_modificar_ni_borrar_en_la_base()
    {
        await _e.VacaMarcadaAsync("100");
        var id = _e.Base.Escalar("SELECT Id FROM Historial WHERE Accion = 'MarcaPasada'")!.ToString()!;

        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("UPDATE Historial SET ValorNuevo = 'x' WHERE Id = $i", ("$i", id)));
        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("DELETE FROM Historial WHERE Id = $i", ("$i", id)));
        Assert.Throws<SqliteException>(() => _e.Base.Ejecutar("UPDATE Historial SET Accion = 'Alta' WHERE Id = $i", ("$i", id)));
    }
}
