using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Servicios;
using Aviss.Domain.Reglas;
using Aviss.Infrastructure.Tests.Soporte;
using Microsoft.Data.Sqlite;

namespace Aviss.Infrastructure.Tests;

/// <summary>RF-8, RF-8b y RF-9: registrar un tacto en una sola transacción.</summary>
public sealed class RegistrarTactoTests : IDisposable
{
    private readonly EscenarioConServicios _e = new();
    public void Dispose() => _e.Dispose();

    private static string T(Guid? id) => EscenarioConServicios.Texto(id);

    private Task<Resultado<Guid>> Registrar(Guid animal, Guid estado, int? dias = null, string? obs = null,
        Guid? usuario = null, EstadoEsperado? esperado = null)
        => _e.Servicio<ITrabajoCampoService>().RegistrarTactoAsync(
            new RegistrarTactoCommand(animal, estado, dias, obs, usuario ?? _e.UsuarioId, esperado));

    // ---------- Camino feliz ----------

    [Fact]
    public async Task Actualiza_animal_guarda_tacto_y_un_solo_historial()
    {
        var animal = _e.AnimalCampo("100", _e.EstadoSeca);
        var r = await Registrar(animal, _e.EstadoPrenada, dias: 45, obs: "  buena  ");
        Assert.True(r.EsCorrecto, r.Mensaje);

        // Animal: estado nuevo, fecha del tacto y fecha de modificación.
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", animal, "ActualizadoEnUtc"));

        // Tacto con todos sus datos.
        var t = r.Valor;
        Assert.Equal(T(animal), _e.Col("Tacto", t, "AnimalId"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Tacto", t, "EstadoAnteriorId"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Col("Tacto", t, "EstadoNuevoId"));
        Assert.Equal("45", _e.Col("Tacto", t, "DiasPrenez"));
        Assert.Equal("buena", _e.Col("Tacto", t, "Observaciones"));
        Assert.Equal(T(_e.UsuarioId), _e.Col("Tacto", t, "UsuarioId"));
        Assert.Equal(T(_e.DispositivoId), _e.Col("Tacto", t, "DispositivoId"));
        Assert.Equal("Normal", _e.Col("Tacto", t, "Tipo"));
        Assert.Null(_e.Col("Tacto", t, "TactoAnuladoId"));
        Assert.Equal("0", _e.Col("Tacto", t, "Anulado"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Tacto", t, "FechaHoraUtc"));

        // Exactamente un registro de historial, con los estados por id.
        Assert.Equal(1, _e.Cuenta("Historial"));
        Assert.Equal("Tacto", _e.Base.Escalar("SELECT Entidad FROM Historial"));
        Assert.Equal(T(t), _e.Base.Escalar("SELECT EntidadId FROM Historial"));
        Assert.Equal("Estado", _e.Base.Escalar("SELECT Campo FROM Historial"));
        Assert.Equal(T(_e.EstadoSeca), _e.Base.Escalar("SELECT ValorAnterior FROM Historial"));
        Assert.Equal(T(_e.EstadoPrenada), _e.Base.Escalar("SELECT ValorNuevo FROM Historial"));
        Assert.Equal("Tacto", _e.Base.Escalar("SELECT Accion FROM Historial"));
        Assert.Equal(T(_e.UsuarioId), _e.Base.Escalar("SELECT UsuarioId FROM Historial"));
    }

    [Fact]
    public async Task Un_animal_sin_estado_tiene_estado_anterior_vacio_y_su_fecha_estaba_vacia()
    {
        var animal = _e.AnimalImportado("7");
        Assert.Null(_e.Col("Animal", animal, "FechaUltimoTactoUtc")); // vacía hasta el primer tacto (RF-3)

        var r = await Registrar(animal, _e.EstadoSeca);
        Assert.True(r.EsCorrecto);
        Assert.Null(_e.Col("Tacto", r.Valor, "EstadoAnteriorId"));
        Assert.Null(_e.Base.Escalar("SELECT ValorAnterior FROM Historial"));
        Assert.Equal(BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora), _e.Col("Animal", animal, "FechaUltimoTactoUtc"));
    }

    [Fact]
    public async Task Un_tacto_sin_cambio_de_estado_es_valido()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        var r = await Registrar(animal, _e.EstadoSeca);
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Tacto", r.Valor, "EstadoAnteriorId"));
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Tacto", r.Valor, "EstadoNuevoId"));
    }

    [Fact]
    public async Task Dos_tactos_seguidos_con_la_misma_hora_son_validos_y_se_ordenan_por_insercion()
    {
        var animal = _e.AnimalCampo();
        var a = await _e.TactoAsync(animal, _e.EstadoSeca);
        var b = await _e.TactoAsync(animal, _e.EstadoPrenada); // mismo instante del reloj
        Assert.Equal(2, _e.Cuenta("Tacto"));
        Assert.Equal(2, _e.Cuenta("Historial"));
        Assert.True(Convert.ToInt64(_e.Col("Tacto", b, "OrdenInsercion")) > Convert.ToInt64(_e.Col("Tacto", a, "OrdenInsercion")));
    }

    [Fact]
    public async Task Informar_el_estado_anterior_correcto_o_vacio_se_acepta()
    {
        var conEstado = _e.AnimalCampo("1", _e.EstadoSeca);
        var sinEstado = _e.AnimalCampo("2");
        Assert.True((await Registrar(conEstado, _e.EstadoPrenada, esperado: new EstadoEsperado(_e.EstadoSeca))).EsCorrecto);
        Assert.True((await Registrar(sinEstado, _e.EstadoPrenada, esperado: new EstadoEsperado(null))).EsCorrecto);
    }

    // ---------- Rechazos (RF-8b): nunca cambian nada ----------

    private async Task AssertRechazaSinCambiosAsync(Task<Resultado<Guid>> operacion, CodigoError esperado)
    {
        var antes = _e.Foto();
        var r = await operacion;
        Assert.False(r.EsCorrecto);
        Assert.Equal(esperado, r.Codigo);
        Assert.False(string.IsNullOrWhiteSpace(r.Mensaje));
        Assert.Equal(antes, _e.Foto());
    }

    [Fact]
    public async Task Rechaza_si_el_estado_anterior_informado_no_coincide()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        await AssertRechazaSinCambiosAsync(
            Registrar(animal, _e.EstadoPrenada, esperado: new EstadoEsperado(_e.EstadoPrenada)), CodigoError.EstadoAnteriorNoCoincide);
        await AssertRechazaSinCambiosAsync(
            Registrar(animal, _e.EstadoPrenada, esperado: new EstadoEsperado(null)), CodigoError.EstadoAnteriorNoCoincide);
    }

    [Fact]
    public async Task Rechaza_un_animal_anulado()
    {
        var animal = _e.Base.Animal(_e.RodeoId, "9", anulado: true);
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoSeca), CodigoError.AnimalAnulado);
    }

    [Fact]
    public async Task Rechaza_si_el_rodeo_o_el_establecimiento_estan_de_baja()
    {
        var animal = _e.AnimalCampo();
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 0");
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoSeca), CodigoError.RodeoDeBaja);
        _e.Base.Ejecutar("UPDATE Rodeo SET Activo = 1");
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0");
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoSeca), CodigoError.EstablecimientoDeBaja);
    }

    [Fact]
    public async Task Rechaza_un_estado_nuevo_dado_de_baja_o_un_usuario_dado_de_baja()
    {
        var animal = _e.AnimalCampo();
        var inactivo = _e.Base.Estado("VIEJO", "Viejo", "VIEJO", activo: false);
        await AssertRechazaSinCambiosAsync(Registrar(animal, inactivo), CodigoError.EstadoDeBaja);

        var vetBaja = _e.Base.Usuario("Baja", activo: false);
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoSeca, usuario: vetBaja), CodigoError.UsuarioDeBaja);
    }

    [Fact]
    public async Task Rechaza_ids_inexistentes()
    {
        var animal = _e.AnimalCampo();
        await AssertRechazaSinCambiosAsync(Registrar(Guid.NewGuid(), _e.EstadoSeca), CodigoError.NoEncontrado);
        await AssertRechazaSinCambiosAsync(Registrar(animal, Guid.NewGuid()), CodigoError.NoEncontrado);
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoSeca, usuario: Guid.NewGuid()), CodigoError.NoEncontrado);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task Rechaza_dias_de_prenez_fuera_de_rango(int dias)
    {
        var animal = _e.AnimalCampo();
        await AssertRechazaSinCambiosAsync(Registrar(animal, _e.EstadoPrenada, dias: dias), CodigoError.DiasPrenezFueraDeRango);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public async Task Acepta_los_bordes_de_dias_de_prenez(int dias)
    {
        var animal = _e.AnimalCampo();
        Assert.True((await Registrar(animal, _e.EstadoPrenada, dias: dias)).EsCorrecto);
    }

    [Fact]
    public async Task Rechaza_si_el_reloj_quedo_antes_del_ultimo_tacto_y_el_mensaje_sugiere_revisar_la_hora()
    {
        var animal = _e.AnimalCampo();
        await _e.TactoAsync(animal, _e.EstadoSeca);

        _e.Reloj.Avanzar(TimeSpan.FromMinutes(-1)); // reloj mal puesto: retrocedió
        var antes = _e.Foto();
        var r = await Registrar(animal, _e.EstadoPrenada);
        Assert.Equal(CodigoError.FechaAnteriorAlUltimoTacto, r.Codigo);
        Assert.Contains("hora", r.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, _e.Foto());
    }

    // ---------- Atomicidad (RF-8, RF-9) ----------

    [Fact]
    public async Task Una_falla_a_mitad_de_la_operacion_no_deja_ningun_cambio()
    {
        var animal = _e.AnimalCampo("1", _e.EstadoSeca);
        var antes = _e.Foto();

        // Trigger de prueba: el INSERT del historial falla DESPUÉS de haber hecho el UPDATE del animal y el INSERT del tacto.
        _e.Base.Ejecutar("CREATE TRIGGER falla_historial BEFORE INSERT ON Historial BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");

        await Assert.ThrowsAnyAsync<Exception>(() => Registrar(animal, _e.EstadoPrenada));

        Assert.Equal(antes, _e.Foto());
        Assert.Equal(T(_e.EstadoSeca), _e.Col("Animal", animal, "EstadoReproductivoId"));
        Assert.Null(_e.Col("Animal", animal, "FechaUltimoTactoUtc"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(0, _e.Cuenta("Historial"));

        // Sin el trigger, el mismo tacto sale bien: la base no quedó trabada.
        _e.Base.Ejecutar("DROP TRIGGER falla_historial");
        Assert.True((await Registrar(animal, _e.EstadoPrenada)).EsCorrecto);
    }

    [Fact]
    public async Task El_tacto_queda_confirmado_en_el_momento_y_lo_ve_una_conexion_nueva()
    {
        var animal = _e.AnimalCampo();
        var t = await _e.TactoAsync(animal, _e.EstadoSeca);

        // Conexión nueva, sin pool, sin pasar por la app (RF-9: no hay acción de "guardar").
        using var otra = new SqliteConnection($"Data Source={_e.Base.RutaDb};Pooling=False;Mode=ReadOnly");
        otra.Open();
        using var cmd = otra.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Tacto WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", t);
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Cerrar_la_conexion_con_la_transaccion_sin_confirmar_no_deja_nada_corte_simulado()
    {
        var animal = _e.AnimalCampo();
        var tactosAntes = _e.Cuenta("Tacto");

        using (var otra = new SqliteConnection($"Data Source={_e.Base.RutaDb};Pooling=False"))
        {
            otra.Open();
            using var pf = otra.CreateCommand(); pf.CommandText = "PRAGMA foreign_keys = ON"; pf.ExecuteNonQuery();
            using var tx = otra.BeginTransaction();
            using var cmd = otra.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, UsuarioId, DispositivoId, Tipo, Anulado, CreadoEnUtc, ActualizadoEnUtc)
                                VALUES ($id, $a, $f, 999, $e, $u, $d, 'Normal', 0, $f, $f)";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("$a", animal);
            cmd.Parameters.AddWithValue("$f", BaseDeDatosDePrueba.Fecha(_e.Reloj.UtcAhora));
            cmd.Parameters.AddWithValue("$e", _e.EstadoSeca);
            cmd.Parameters.AddWithValue("$u", _e.UsuarioId);
            cmd.Parameters.AddWithValue("$d", Guid.NewGuid());
            cmd.ExecuteNonQuery();
            // Se sale sin Commit: como si se cortara la energía.
        }

        Assert.Equal(tactosAntes, _e.Cuenta("Tacto"));
    }
}
