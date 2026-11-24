using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 005, segunda vuelta: la siembra de los diez estándar, el renombre de PREÑADA, la adopción de "Preñada fecha previa"
/// y la reconciliación (RF-1 a RF-3, RF-19, RF-20, RF-26).
/// </summary>
public sealed class SembradorPasosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    private async Task SembrarAsync()
    {
        using var ctx = _db.NuevoContexto();
        await new SembradorInicial(_db.Reloj).SembrarEstadosAsync(ctx);
    }

    private string Texto(string sql, params (string, object?)[] p) => _db.Escalar(sql, p)?.ToString() ?? "(nulo)";

    [Fact]
    public async Task Una_base_nueva_tiene_los_diez_activos_con_pasos_destino_y_los_siete_principales_en_orden()
    {
        await SembrarAsync();

        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Activo = 1 AND ClaveEstandar = Codigo"));

        // Los siete principales en el orden de RF-3.
        using var cmd = _db.Cruda.CreateCommand();
        cmd.CommandText = "SELECT ClaveEstandar FROM EstadoReproductivo WHERE EsPrincipal = 1 ORDER BY Orden";
        using var r = cmd.ExecuteReader();
        var principales = new List<string>();
        while (r.Read()) principales.Add(r.GetString(0));
        Assert.Equal(["REVISAR_NUEVAMENTE", "OK", "VACIA", "PRENADA", "PRENADA_FECHA_PREVIA", "ABORTO", "NOINSEM"], principales);

        // De "Otro evento": los tres de siempre, activos y no principales.
        Assert.Equal(3, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar IN ('INSEMIN','SECA','FRESCA') AND EsPrincipal = 0 AND Activo = 1"));

        // Fecha previa: código provisorio propio, un paso y destino PREÑADA.
        Assert.Equal("PREÑADA FECHA PREVIA", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA_FECHA_PREVIA'"));
        Assert.Equal("DiasPreniez", Texto("SELECT Pasos FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA_FECHA_PREVIA'"));
        Assert.Equal(1, _db.Contar(@"SELECT COUNT(*) FROM EstadoReproductivo f JOIN EstadoReproductivo p ON p.Id = f.EstadoDestinoId
                                      WHERE f.ClaveEstandar = 'PRENADA_FECHA_PREVIA' AND p.ClaveEstandar = 'PRENADA'"));
        // Aborto: Estado con sus dos pasos y sin destino.
        Assert.Equal("Estado", Texto("SELECT Tipo FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        Assert.Equal("DiasAborto,ResultadoAborto", Texto("SELECT Pasos FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        Assert.Equal("(nulo)", Texto("SELECT EstadoDestinoId FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        // El resto no pide pasos.
        Assert.Equal(8, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Pasos IS NULL AND EstadoDestinoId IS NULL"));
        Assert.Equal("Preñada fecha correcta", Texto("SELECT Nombre FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA'"));
    }

    [Fact]
    public async Task Dos_arranques_seguidos_no_duplican_ni_tocan_nada()
    {
        await SembrarAsync();
        var antes = Texto("SELECT group_concat(Id || Nombre || ActualizadoEnUtc, ';') FROM (SELECT * FROM EstadoReproductivo ORDER BY Id)");
        _db.Reloj.Avanzar(TimeSpan.FromHours(1));

        await SembrarAsync();

        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal(antes, Texto("SELECT group_concat(Id || Nombre || ActualizadoEnUtc, ';') FROM (SELECT * FROM EstadoReproductivo ORDER BY Id)"));
    }

    [Fact]
    public async Task PRENADA_se_renombra_a_fecha_correcta_conservando_su_identidad()
    {
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);

        await SembrarAsync();

        Assert.Equal("Preñada fecha correcta", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
        Assert.Equal("PREÑADA", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
        Assert.Equal("PRENADA", Texto("SELECT Codigo FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Id = $id AND ClaveEstandar = 'PRENADA'", ("$id", prenada)));
    }

    [Fact]
    public async Task El_renombre_se_aplica_aunque_el_nombre_estuviera_cambiado_a_mano()
    {
        var prenada = _db.Estado("PRENADA", "Mi preñada", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);

        await SembrarAsync();

        Assert.Equal("Preñada fecha correcta", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
    }

    [Fact]
    public async Task Si_el_nombre_ya_esta_tomado_se_omite_el_renombre_sin_fallar()
    {
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);
        // Otro evento, aunque esté desactivado y escrito distinto en mayúsculas, ya usa el nombre.
        _db.Estado("OTRA", "PREÑADA FECHA CORRECTA", "OTRO-DC", activo: false);

        await SembrarAsync();   // no tiene que lanzar

        Assert.Equal("PREÑADA", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
        // Lo demás se sembró igual.
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA_FECHA_PREVIA'"));
    }

    [Fact]
    public async Task El_renombre_se_aplica_una_sola_vez_y_no_pisa_un_cambio_posterior()
    {
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);
        await SembrarAsync();

        // Después el veterinario la renombra a su gusto.
        _db.Ejecutar("UPDATE EstadoReproductivo SET Nombre = 'Preñada confirmada' WHERE Id = $id", ("$id", prenada));
        await SembrarAsync();

        Assert.Equal("Preñada confirmada", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", prenada)));
    }

    [Fact]
    public async Task Una_Preniada_fecha_previa_a_mano_se_adopta_sin_cambiar_nombre_codigo_ni_activo()
    {
        var prenada = _db.Estado("PRENADA", "PREÑADA", "PREÑADA", tipo: "Estado", clave: "PRENADA", principal: true, orden: 4);
        var aMano = _db.Estado("PFP", "preñada fecha previa", "PFP-DC", activo: false);

        await SembrarAsync();

        Assert.Equal("PRENADA_FECHA_PREVIA", Texto("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)));
        Assert.Equal("preñada fecha previa", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)));
        Assert.Equal("PFP-DC", Texto("SELECT CodigoDairyComp FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)));
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)));   // no se reactiva
        Assert.Equal("DiasPreniez", Texto("SELECT Pasos FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)));
        Assert.Equal(prenada.ToString().ToUpperInvariant(), Texto("SELECT EstadoDestinoId FROM EstadoReproductivo WHERE Id = $id", ("$id", aMano)).ToUpperInvariant());
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'PRENADA_FECHA_PREVIA'"));
        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));   // adoptó en vez de crear otro
    }

    [Fact]
    public async Task Un_Aborto_estandar_con_la_configuracion_vieja_se_reconcilia_sin_reactivar_nada()
    {
        // Como lo dejó la primera vuelta: solo evento, orden 5, sin pasos; el veterinario lo desactivó.
        var aborto = _db.Estado("ABORTO", "Aborto", "ABORTO", activo: false, tipo: "SoloEvento", clave: "ABORTO", principal: true, orden: 5);
        var noinsem = _db.Estado("NOINSEM", "NOINSEM", "NOINSEM", tipo: "Estado", clave: "NOINSEM", principal: true, orden: 6);

        await SembrarAsync();

        Assert.Equal("Estado", Texto("SELECT Tipo FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal("DiasAborto,ResultadoAborto", Texto("SELECT Pasos FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal(6, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
        Assert.Equal(7, _db.Contar("SELECT Orden FROM EstadoReproductivo WHERE Id = $id", ("$id", noinsem)));
        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", aborto)));
    }

    [Fact]
    public async Task Un_inicial_desactivado_sigue_desactivado_y_un_nombre_cambiado_se_respeta()
    {
        var vacia = _db.Estado("VACIA", "Vacía", "VACIA", activo: false, tipo: "Estado", clave: "VACIA", principal: true, orden: 3);

        await SembrarAsync();

        Assert.Equal(0, _db.Contar("SELECT Activo FROM EstadoReproductivo WHERE Id = $id", ("$id", vacia)));
        Assert.Equal("Vacía", Texto("SELECT Nombre FROM EstadoReproductivo WHERE Id = $id", ("$id", vacia)));
    }

    [Fact]
    public async Task Sin_PRENADA_no_se_crea_la_fecha_previa_pero_el_arranque_no_falla()
    {
        // PREÑADA no se puede crear porque su código de Dairy Comp ya lo usa otro estado activo; sin destino no hay fecha previa.
        _db.Estado("OTRA", "Otra cosa", "PREÑADA", clave: "OTRA");

        await SembrarAsync();

        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar IN ('PRENADA','PRENADA_FECHA_PREVIA')"));
        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
    }
}
