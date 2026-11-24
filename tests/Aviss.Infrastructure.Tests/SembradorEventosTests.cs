using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>Spec 005, RF-1, RF-2, RF-3, RF-19, RF-20: la siembra de los nueve eventos estándar y la adopción.</summary>
public sealed class SembradorEventosTests : IDisposable
{
    private readonly BaseDeDatosDePrueba _db = new();
    public void Dispose() => _db.Dispose();

    private async Task SembrarAsync()
    {
        using var ctx = _db.NuevoContexto();
        await new SembradorInicial(_db.Reloj).SembrarEstadosAsync(ctx);
    }

    private (string Nombre, string Dairy, string Tipo, long Principal, long Orden, bool Activo) Fila(string clave)
    {
        using var cmd = _db.Cruda.CreateCommand();
        cmd.CommandText = "SELECT Nombre, CodigoDairyComp, Tipo, EsPrincipal, Orden, Activo FROM EstadoReproductivo WHERE ClaveEstandar = $k";
        cmd.Parameters.AddWithValue("$k", clave);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read(), $"falta el estándar {clave}");
        return (r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5) == 1);
    }

    [Fact]
    public async Task Una_base_nueva_tiene_los_diez_con_tipo_principal_y_orden_de_RF3()
    {
        await SembrarAsync();

        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = Codigo AND Activo = 1"));

        // Principales, en orden, y los códigos provisorios de los tres nuevos (RF-1).
        Assert.Equal(("Revisar Nuevamente", "REVISAR NUEVAMENTE", "SoloEvento", 1L, 1L, true), Fila("REVISAR_NUEVAMENTE"));
        Assert.Equal(("OK", "OK", "SoloEvento", 1L, 2L, true), Fila("OK"));
        Assert.Equal(("VACIA", "VACIA", "Estado", 1L, 3L, true), Fila("VACIA"));
        Assert.Equal(("Preñada fecha correcta", "PREÑADA", "Estado", 1L, 4L, true), Fila("PRENADA"));
        Assert.Equal(("Preñada fecha previa", "PREÑADA FECHA PREVIA", "Estado", 1L, 5L, true), Fila("PRENADA_FECHA_PREVIA"));
        Assert.Equal(("Aborto", "ABORTO", "Estado", 1L, 6L, true), Fila("ABORTO"));
        Assert.Equal(("NOINSEM", "NOINSEM", "Estado", 1L, 7L, true), Fila("NOINSEM"));
        // De "Otro evento".
        foreach (var k in new[] { "INSEMIN", "SECA", "FRESCA" })
        {
            var f = Fila(k);
            Assert.Equal(("Estado", 0L, 0L), (f.Tipo, f.Principal, f.Orden));
        }
        Assert.Equal(2, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Tipo = 'SoloEvento'"));
    }

    [Fact]
    public async Task Dos_arranques_seguidos_no_duplican_nada()
    {
        await SembrarAsync();
        await SembrarAsync();

        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
        Assert.Equal(10, _db.Contar("SELECT COUNT(DISTINCT ClaveEstandar) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task Un_Aborto_a_mano_se_adopta_sin_cambiar_nombre_codigo_ni_activo()
    {
        // Creado a mano: código interno derivado del nombre, código de Dairy Comp propio, y desactivado.
        _db.Estado("ABORTO", "aborto vaca", "ABORT-DC", activo: false);

        await SembrarAsync();

        Assert.Equal(1, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE Codigo = 'ABORTO'"));
        var f = Fila("ABORTO");
        Assert.Equal(("aborto vaca", "ABORT-DC", "Estado", 1L, 6L, false), f);
        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo")); // adoptó en vez de crear otro
    }

    [Fact]
    public async Task Se_adopta_por_nombre_sin_distinguir_mayusculas()
    {
        _db.Estado("MI_OK", "ok", "CODIGO-PROPIO");

        await SembrarAsync();

        Assert.Equal("MI_OK", _db.Escalar("SELECT Codigo FROM EstadoReproductivo WHERE ClaveEstandar = 'OK'"));
        Assert.Equal("CODIGO-PROPIO", Fila("OK").Dairy);
        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task Se_adopta_por_codigo_de_dairy_comp_sin_distinguir_mayusculas()
    {
        _db.Estado("OTRO_NOMBRE", "Algo distinto", "revisar nuevamente");

        await SembrarAsync();

        Assert.Equal("OTRO_NOMBRE", _db.Escalar("SELECT Codigo FROM EstadoReproductivo WHERE ClaveEstandar = 'REVISAR_NUEVAMENTE'"));
        Assert.Equal("Algo distinto", Fila("REVISAR_NUEVAMENTE").Nombre);
        Assert.Equal(10, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task El_mismo_codigo_interno_manda_sobre_el_mismo_nombre()
    {
        var t = _db.Reloj.UtcAhora;
        _db.Estado("PRIMERO", "Aborto", "X1", creadoEnUtc: t.AddDays(-5));  // mismo nombre, más viejo
        _db.Estado("ABORTO", "OTRO", "ABORTO", creadoEnUtc: t.AddDays(-1)); // mismo Codigo (paso 1)

        await SembrarAsync();

        // El paso 1 (mismo Codigo) manda sobre el de mismo nombre; el de nombre "Aborto" queda como estado común.
        Assert.Equal("ABORTO", _db.Escalar("SELECT Codigo FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        Assert.Null(_db.Escalar("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Codigo = 'PRIMERO'"));
    }

    [Fact]
    public async Task Un_estado_no_se_adopta_para_dos_estandares()
    {
        // Nombre "OK" y código de Dairy Comp "ABORTO": podría servir a los dos estándares, pero solo se adopta una vez.
        _db.Estado("MIXTO", "OK", "ABORTO");

        await SembrarAsync();

        // OK va antes que ABORTO en la lista: adopta al MIXTO por nombre. ABORTO ya no tiene candidato libre y no se puede
        // crear (su código de Dairy Comp lo tiene el MIXTO, activo), así que se omite sin fallar.
        Assert.Equal("OK", _db.Escalar("SELECT ClaveEstandar FROM EstadoReproductivo WHERE Codigo = 'MIXTO'"));
        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'ABORTO'"));
        Assert.Equal(9, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task Si_crear_un_estandar_chocara_con_una_unicidad_se_omite_sin_fallar()
    {
        // "INSEMIN" es el nombre de un estado que ya es el estándar SECA (tiene su clave): no hay candidato libre para
        // INSEMIN y su nombre está tomado, así que INSEMIN se omite.
        _db.Estado("SECA", "INSEMIN", "SECA", clave: "SECA");

        await SembrarAsync();

        Assert.Equal(0, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo WHERE ClaveEstandar = 'INSEMIN'"));
        Assert.Equal("INSEMIN", _db.Escalar("SELECT Nombre FROM EstadoReproductivo WHERE ClaveEstandar = 'SECA'"));
        Assert.Equal(9, _db.Contar("SELECT COUNT(*) FROM EstadoReproductivo"));
    }

    [Fact]
    public async Task La_adopcion_no_reactiva_un_inicial_desactivado_ni_pisa_sus_cambios()
    {
        _db.Estado("SECA", "Seca larga", "SECA2", activo: false);

        await SembrarAsync();

        var f = Fila("SECA");
        Assert.Equal(("Seca larga", "SECA2", false), (f.Nombre, f.Dairy, f.Activo));
    }
}
