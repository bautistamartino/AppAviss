using Aviss.Application.Comandos;
using Aviss.Application.Importacion;
using Aviss.Excel;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 010, T2: importar la muestra con EID de punta a punta (perfil real de Dairy Comp + SQLite real).
/// RF-1 (guardado), RF-8 (conteos), RF-9, RF-10, RF-11, RF-12 y RF-19.
/// </summary>
public sealed class ImportacionEidTests : IDisposable
{
    private readonly EscenarioImportacion _e = new(new PerfilDairyComp());
    public void Dispose() => _e.Dispose();

    private static string Ruta(string archivo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "muestras"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "muestras", archivo);
    }

    private async Task<VistaPreviaImportacion> PrepararAsync(string archivo)
    {
        var bytes = await File.ReadAllBytesAsync(Ruta(archivo));
        var r = await _e.Importacion.PrepararAsync(archivo, new MemoryStream(bytes));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private async Task<ResultadoImportacion> ImportarAsync(string archivo, Guid? establecimientoId = null, bool repetido = false)
    {
        var vista = await PrepararAsync(archivo);
        var r = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(
            vista.Id, establecimientoId, establecimientoId is null ? "Tambo de prueba" : null, "Rodeo", repetido));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private long ConEidEn(Guid rodeoId)
        => _e.Cuenta("Animal", $"RodeoId = '{rodeoId.ToString().ToUpperInvariant()}' AND IdElectronico IS NOT NULL");

    private long SinEidEn(Guid rodeoId)
        => _e.Cuenta("Animal", $"RodeoId = '{rodeoId.ToString().ToUpperInvariant()}' AND IdElectronico IS NULL");

    [Fact]
    public async Task La_muestra_se_importa_con_272_animales_con_EID_y_10_sin_EID()
    {
        var vista = await PrepararAsync("dairy-comp-eid-282-vacas.csv");

        // La vista previa ya trae los conteos (RF-8) y ninguna advertencia por el EID.
        Assert.True(vista.HayColumnaEid);
        Assert.Equal(272, vista.ConEid);
        Assert.Equal(10, vista.SinEid);
        Assert.DoesNotContain(vista.Advertencias, a => a.Tipo is TipoAdvertencia.EidInvalido or TipoAdvertencia.EidRepetido);

        var r = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(vista.Id, null, "Tambo", "Rodeo", false));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(282, r.Valor.AnimalesImportados);
        Assert.True(r.Valor.HayColumnaEid);
        Assert.Equal(272, r.Valor.ConEid);
        Assert.Equal(10, r.Valor.SinEid);
        Assert.Equal(272, ConEidEn(r.Valor.RodeoId));
        Assert.Equal(10, SinEidEn(r.Valor.RodeoId));
    }

    [Fact]
    public async Task El_EID_se_guarda_sin_espacios_y_la_fresca_con_guion_queda_sin_EID()
    {
        var r = await ImportarAsync("dairy-comp-eid-282-vacas.csv");

        Assert.Equal("999000458013169", _e.Base.Escalar("SELECT IdElectronico FROM Animal WHERE NumeroCaravana = '30092'"));
        Assert.Equal(1, _e.Cuenta("Animal", "NumeroCaravana = '30036' AND IdElectronico IS NULL"));
        Assert.Equal(0, _e.Cuenta("Animal", "IdElectronico LIKE '% %'"));
        Assert.Equal(282, _e.Cuenta("Animal", $"RodeoId = '{r.RodeoId.ToString().ToUpperInvariant()}'"));
    }

    [Fact]
    public async Task Los_datos_originales_conservan_EID_SCDAT_y_SCTIM_tal_cual()
    {
        await ImportarAsync("dairy-comp-eid-282-vacas.csv");

        var json = (string)_e.Base.Escalar("SELECT DatosOriginalesJson FROM Animal WHERE NumeroCaravana = '30001'")!;

        Assert.Contains("\"EID\"", json);
        Assert.Contains("\"SCDAT\"", json);
        Assert.Contains("\"SCTIM\"", json);
        Assert.Contains("999000458000000", json);
    }

    [Fact]
    public async Task Importar_el_mismo_archivo_dos_veces_crea_dos_rodeos_con_los_mismos_EID_sin_chocar()
    {
        var primero = await ImportarAsync("dairy-comp-eid-282-vacas.csv");
        var establecimiento = Guid.Parse((string)_e.Base.Escalar("SELECT Id FROM Establecimiento")!);
        var segundo = await ImportarAsync("dairy-comp-eid-282-vacas.csv", establecimiento, repetido: true);

        Assert.NotEqual(primero.RodeoId, segundo.RodeoId);
        Assert.Equal(2, _e.Cuenta("Rodeo"));
        Assert.Equal(272, ConEidEn(primero.RodeoId));
        Assert.Equal(272, ConEidEn(segundo.RodeoId));
        // El EID se repite entre rodeos (544 filas, 272 valores distintos): el índice único es por rodeo.
        Assert.Equal(272, _e.Base.Contar("SELECT COUNT(DISTINCT IdElectronico) FROM Animal"));
    }

    [Fact]
    public async Task Un_rodeo_viejo_sin_columna_EID_no_cambia_al_importar_el_archivo_con_EID_y_no_pide_baja_ni_avisa_repetido()
    {
        // Rodeo viejo: la muestra anterior, sin columna EID.
        var viejo = await ImportarAsync("dairy-comp-282-vacas.csv");
        Assert.False(viejo.HayColumnaEid);
        var fotoViejo = _e.Base.Escalar(
            $"SELECT group_concat(Id || ':' || COALESCE(IdElectronico,'') || ':' || ActualizadoEnUtc) FROM (SELECT * FROM Animal WHERE RodeoId = '{viejo.RodeoId.ToString().ToUpperInvariant()}' ORDER BY Id)");
        var establecimiento = Guid.Parse((string)_e.Base.Escalar("SELECT Id FROM Establecimiento")!);

        // El archivo nuevo es distinto (otra huella): no salta el aviso de archivo repetido (RF-12) y entra como rodeo nuevo.
        var vista = await PrepararAsync("dairy-comp-eid-282-vacas.csv");
        Assert.False(vista.EsArchivoRepetido);
        var nuevo = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(vista.Id, establecimiento, null, "Rodeo con EID", false));

        Assert.True(nuevo.EsCorrecto, nuevo.Mensaje);
        Assert.Equal(2, _e.Cuenta("Rodeo"));
        Assert.Equal(0, _e.Cuenta("Rodeo", "Estado <> 'Abierto'"));   // el viejo no se dio de baja
        Assert.Equal(0, ConEidEn(viejo.RodeoId));                      // y sigue sin EID (RF-11)
        Assert.Equal(fotoViejo, _e.Base.Escalar(
            $"SELECT group_concat(Id || ':' || COALESCE(IdElectronico,'') || ':' || ActualizadoEnUtc) FROM (SELECT * FROM Animal WHERE RodeoId = '{viejo.RodeoId.ToString().ToUpperInvariant()}' ORDER BY Id)"));
        Assert.Equal(272, ConEidEn(nuevo.Valor.RodeoId));
    }

    [Fact]
    public async Task El_archivo_sin_columna_EID_importa_sin_EID_y_sin_conteos()
    {
        var r = await ImportarAsync("dairy-comp-282-vacas.csv");

        Assert.False(r.HayColumnaEid);
        Assert.Equal(0, r.ConEid);
        Assert.Equal(0, r.SinEid);
        Assert.Equal(0, _e.Cuenta("Animal", "IdElectronico IS NOT NULL"));
    }

    [Fact]
    public async Task Las_advertencias_y_la_vista_previa_no_traen_ningun_EID()
    {
        var vista = await PrepararAsync("dairy-comp-eid-282-vacas.csv");

        var textos = vista.Advertencias.SelectMany(a => new[] { a.Valor ?? "", a.Mensaje });
        Assert.DoesNotContain(textos, t => t.Contains("999000"));
    }
}
