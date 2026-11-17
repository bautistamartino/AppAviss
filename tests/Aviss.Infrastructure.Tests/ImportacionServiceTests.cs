using System.Security.Cryptography;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Excel;
using Aviss.Infrastructure.Arranque;
using Aviss.Infrastructure.Tests.Soporte;

namespace Aviss.Infrastructure.Tests;

/// <summary>
/// Spec 002, T3: importar sobre la base real (SQLite) con un perfil falso, y de punta a punta con el perfil de
/// Dairy Comp y la muestra de 226 vacas. RF-1, 2, 13 a 22, 24 y 25.
/// </summary>
public sealed class ImportacionServiceTests : IDisposable
{
    private const string TamboSecreto = "Tambo Secreto La Esperanza";

    private readonly PerfilFalso _perfil = new("Otro Sistema", PerfilFalso.Vacas(3));
    private readonly EscenarioImportacion _e;

    public ImportacionServiceTests() => _e = new EscenarioImportacion(_perfil);
    public void Dispose() => _e.Dispose();

    // ---------- Ayudas ----------

    private static byte[] Bytes(byte semilla = 1, int largo = 16)
        => Enumerable.Range(0, largo).Select(i => (byte)(semilla + i)).ToArray();

    private async Task<VistaPreviaImportacion> PrepararAsync(byte[]? contenido = null, string nombre = "tambo-uno.xls")
    {
        var r = await _e.Importacion.PrepararAsync(nombre, new MemoryStream(contenido ?? Bytes()));
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private Task<Resultado<ResultadoImportacion>> ConfirmarAsync(
        VistaPreviaImportacion vista, Guid? establecimientoId = null, string? nuevo = "Tambo Nuevo",
        string rodeo = "Rodeo Mayo", bool repetido = false)
        => _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(
            vista.Id, establecimientoId, establecimientoId is null ? nuevo : null, rodeo, repetido));

    private async Task<ResultadoImportacion> ConfirmarBienAsync(VistaPreviaImportacion vista, Guid? establecimientoId = null, string? nuevo = "Tambo Nuevo")
    {
        var r = await ConfirmarAsync(vista, establecimientoId, nuevo);
        Assert.True(r.EsCorrecto, r.Mensaje);
        return r.Valor;
    }

    private void AfirmarNadaGuardado()
    {
        Assert.Equal(0, _e.Cuenta("Establecimiento"));
        Assert.Equal(0, _e.Cuenta("Rodeo"));
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    private object? DeRodeo(string columna, Guid rodeoId)
        => _e.Base.Escalar($"SELECT {columna} FROM Rodeo WHERE Id = $i", ("$i", rodeoId));

    // ---------- RF-1: los bytes y la huella ----------

    [Fact]
    public async Task Huella_es_la_de_los_bytes_leidos_en_minuscula()
    {
        var contenido = Bytes(7, 100);

        var vista = await PrepararAsync(contenido);

        var esperado = Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();
        Assert.Equal(esperado, vista.Hash);
        Assert.Equal(64, vista.Hash.Length);
    }

    [Fact]
    public async Task Rodeo_guarda_los_mismos_bytes_la_huella_y_el_nombre_del_archivo()
    {
        var contenido = Bytes(3, 200);
        var vista = await PrepararAsync(contenido, "mi-tambo.xls");

        var res = await ConfirmarBienAsync(vista);

        Assert.Equal(contenido, (byte[])DeRodeo("ArchivoOrigenBlob", res.RodeoId)!);
        Assert.Equal(vista.Hash, DeRodeo("ArchivoOrigenHash", res.RodeoId));
        Assert.Equal("mi-tambo.xls", DeRodeo("ArchivoOrigenNombre", res.RodeoId));
        Assert.Equal("Abierto", DeRodeo("Estado", res.RodeoId));
    }

    // ---------- RF-25: otro formato sin tocar el resto ----------

    [Fact]
    public async Task Con_un_perfil_falso_se_importa_igual_y_el_rodeo_guarda_el_nombre_del_perfil()
    {
        var res = await ConfirmarBienAsync(await PrepararAsync());

        Assert.Equal("Otro Sistema", DeRodeo("PerfilExcel", res.RodeoId));
        Assert.Equal(3, _e.Cuenta("Animal"));
    }

    // ---------- RF-2: tamaño y orden de validación ----------

    [Fact]
    public async Task Un_archivo_de_exactamente_25_MB_se_acepta()
    {
        var r = await _e.Importacion.PrepararAsync("grande.xls", new MemoryStream(new byte[LimitesImportacion.MaxBytes]));

        Assert.True(r.EsCorrecto, r.Mensaje);
    }

    [Fact]
    public async Task Un_archivo_de_25_MB_mas_un_byte_se_rechaza_antes_de_leer_el_formato()
    {
        var r = await _e.Importacion.PrepararAsync("grande.xls", new MemoryStream(new byte[LimitesImportacion.MaxBytes + 1]));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.ArchivoRechazado, r.Codigo);
        Assert.Equal(MensajesImportacion.Para(MotivoRechazoArchivo.Excede25Mb), r.Mensaje);
        Assert.Equal(0, _perfil.Llamadas);   // el tamaño se mira antes que el formato: el perfil ni se llama
    }

    [Fact]
    public async Task Un_rechazo_del_perfil_vuelve_como_archivo_rechazado_con_su_mensaje_y_no_guarda_nada()
    {
        var rechazador = new PerfilFalso("Otro", _ => ResultadoLectura.Rechazado(MotivoRechazoArchivo.FormatoNoSoportado));
        using var e = new EscenarioImportacion(rechazador);

        var r = await e.Importacion.PrepararAsync("x.xls", new MemoryStream(Bytes()));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.ArchivoRechazado, r.Codigo);
        Assert.Equal(MensajesImportacion.Para(MotivoRechazoArchivo.FormatoNoSoportado), r.Mensaje);
        Assert.Equal(0, e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Una_lectura_sin_filas_de_datos_se_rechaza()
    {
        using var e = new EscenarioImportacion(new PerfilFalso("Otro", PerfilFalso.Lectura([])));

        var r = await e.Importacion.PrepararAsync("x.xls", new MemoryStream(Bytes()));

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.ArchivoRechazado, r.Codigo);
        Assert.Equal(MensajesImportacion.Para(MotivoRechazoArchivo.SinFilasDeDatos), r.Mensaje);
    }

    [Fact]
    public async Task Un_archivo_rechazado_no_reemplaza_la_vista_previa_que_ya_habia()
    {
        var vista = await PrepararAsync();
        var rechazo = await _e.Importacion.PrepararAsync("x.xls", new MemoryStream(new byte[LimitesImportacion.MaxBytes + 1]));
        Assert.False(rechazo.EsCorrecto);

        var r = await ConfirmarAsync(vista);

        Assert.True(r.EsCorrecto, r.Mensaje);
    }

    // ---------- RF-14: establecimiento existente, nuevo o ninguno ----------

    [Fact]
    public async Task Establecimiento_existente_activo_recibe_el_rodeo_y_no_se_crea_otro()
    {
        var est = _e.Base.Establecimiento("Tambo Uno");

        var res = await ConfirmarBienAsync(await PrepararAsync(), est);

        Assert.Equal(1, _e.Cuenta("Establecimiento"));
        Assert.Equal(est, Guid.Parse((string)DeRodeo("EstablecimientoId", res.RodeoId)!));
    }

    [Fact]
    public async Task Establecimiento_nuevo_se_crea_con_el_nombre_recortado()
    {
        await ConfirmarBienAsync(await PrepararAsync(), nuevo: "  Tambo Nuevo  ");

        Assert.Equal("Tambo Nuevo", _e.Base.Escalar("SELECT Nombre FROM Establecimiento"));
        Assert.Equal(1, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Sin_ningun_establecimiento_la_lista_viene_vacia_y_se_puede_crear_uno()
    {
        Assert.Empty(await _e.Importacion.ListarEstablecimientosActivosAsync());

        await ConfirmarBienAsync(await PrepararAsync(), nuevo: "Primero");

        var lista = await _e.Importacion.ListarEstablecimientosActivosAsync();
        Assert.Equal("Primero", Assert.Single(lista).Nombre);
    }

    [Fact]
    public async Task La_lista_de_establecimientos_solo_trae_activos_y_ordenados_por_nombre()
    {
        _e.Base.Establecimiento("Zeta");
        _e.Base.Establecimiento("Alfa");
        var baja = _e.Base.Establecimiento("Beta");
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0 WHERE Id = $i", ("$i", baja));

        var lista = await _e.Importacion.ListarEstablecimientosActivosAsync();

        Assert.Equal(new[] { "Alfa", "Zeta" }, lista.Select(x => x.Nombre).ToArray());
    }

    // ---------- RF-15: nombres ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nombre_de_rodeo_vacio_se_rechaza_sin_guardar_y_sin_gastar_la_vista(string nombre)
    {
        var vista = await PrepararAsync();

        var r = await ConfirmarAsync(vista, rodeo: nombre);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        AfirmarNadaGuardado();
        Assert.True((await ConfirmarAsync(vista)).EsCorrecto);   // la vista previa sigue Pendiente
    }

    [Fact]
    public async Task Nombre_de_rodeo_de_101_caracteres_se_rechaza_y_de_100_se_acepta()
    {
        var vista = await PrepararAsync();

        var largo = await ConfirmarAsync(vista, rodeo: new string('r', 101));
        Assert.Equal(CodigoError.DatosInvalidos, largo.Codigo);
        AfirmarNadaGuardado();

        var justo = await ConfirmarAsync(vista, rodeo: "  " + new string('r', 100) + "  ");   // se recorta antes de contar
        Assert.True(justo.EsCorrecto, justo.Mensaje);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Establecimiento_nuevo_sin_nombre_se_rechaza(string? nombre)
    {
        var r = await ConfirmarAsync(await PrepararAsync(), nuevo: nombre);

        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        AfirmarNadaGuardado();
    }

    [Fact]
    public async Task Establecimiento_nuevo_de_101_caracteres_se_rechaza()
    {
        var r = await ConfirmarAsync(await PrepararAsync(), nuevo: new string('e', 101));

        Assert.Equal(CodigoError.DatosInvalidos, r.Codigo);
        AfirmarNadaGuardado();
    }

    [Theory]
    [InlineData("tambo secreto la esperanza")]    // otra capitalización
    [InlineData("  TAMBO SECRETO LA ESPERANZA ")] // con espacios de borde
    public async Task Establecimiento_nuevo_repetido_da_el_error_fijo_sin_el_nombre_y_no_guarda_nada(string nuevo)
    {
        _e.Base.Establecimiento(TamboSecreto);

        var r = await ConfirmarAsync(await PrepararAsync(), nuevo: nuevo);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(MensajesImportacion.EstablecimientoRepetido, r.Mensaje);
        Assert.DoesNotContain("secreto", r.Mensaje, StringComparison.OrdinalIgnoreCase);   // RF-24
        Assert.Equal(1, _e.Cuenta("Establecimiento"));
        Assert.Equal(0, _e.Cuenta("Rodeo"));
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Establecimiento_repetido_con_enie_en_otra_capitalizacion_tambien_cuenta()
    {
        _e.Base.Establecimiento("Ñandú Norte");

        var r = await ConfirmarAsync(await PrepararAsync(), nuevo: "ñandú norte");

        Assert.Equal(CodigoError.Duplicado, r.Codigo);
    }

    [Fact]
    public async Task Establecimiento_repetido_dado_de_baja_tambien_cuenta()
    {
        var baja = _e.Base.Establecimiento("Tambo Viejo");
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0 WHERE Id = $i", ("$i", baja));

        var r = await ConfirmarAsync(await PrepararAsync(), nuevo: "Tambo Viejo");

        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Establecimiento"));
    }

    [Fact]
    public async Task Se_puede_repetir_el_nombre_de_un_rodeo()
    {
        var est = _e.Base.Establecimiento("Tambo Uno");
        await ConfirmarBienAsync(await PrepararAsync(Bytes(1)), est);

        var r = await ConfirmarAsync(await PrepararAsync(Bytes(2)), est);   // mismo nombre de rodeo ("Rodeo Mayo")

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(2, _e.Cuenta("Rodeo", "Nombre = 'Rodeo Mayo'"));
    }

    // ---------- RF-20: lo que cambia entre la vista previa y la confirmación ----------

    [Fact]
    public async Task Establecimiento_dado_de_baja_entre_la_vista_previa_y_la_confirmacion_no_guarda_nada()
    {
        var est = _e.Base.Establecimiento("Tambo Uno");
        var vista = await PrepararAsync();
        _e.Base.Ejecutar("UPDATE Establecimiento SET Activo = 0 WHERE Id = $i", ("$i", est));

        var r = await ConfirmarAsync(vista, est);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.EstablecimientoDeBaja, r.Codigo);
        Assert.Equal(0, _e.Cuenta("Rodeo"));
        Assert.Equal(0, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Establecimiento_inexistente_no_guarda_nada()
    {
        var r = await ConfirmarAsync(await PrepararAsync(), Guid.NewGuid());

        Assert.Equal(CodigoError.NoEncontrado, r.Codigo);
        AfirmarNadaGuardado();
    }

    [Fact]
    public async Task Dos_vistas_previas_con_el_mismo_establecimiento_nuevo_solo_crean_uno()
    {
        await ConfirmarBienAsync(await PrepararAsync(Bytes(1)), nuevo: "Tambo Doble");

        var r = await ConfirmarAsync(await PrepararAsync(Bytes(2)), nuevo: "tambo doble");

        Assert.Equal(CodigoError.Duplicado, r.Codigo);
        Assert.Equal(1, _e.Cuenta("Establecimiento"));
        Assert.Equal(1, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Un_rechazo_deja_la_vista_pendiente_y_se_puede_reintentar_con_otro_establecimiento()
    {
        var existente = _e.Base.Establecimiento("Tambo Uno");
        var vista = await PrepararAsync();

        Assert.Equal(CodigoError.Duplicado, (await ConfirmarAsync(vista, nuevo: "Tambo Uno")).Codigo);

        Assert.True((await ConfirmarAsync(vista, existente)).EsCorrecto);
    }

    // ---------- RF-16: misma huella y sin filas válidas ----------

    [Fact]
    public async Task Misma_huella_avisa_con_nombre_y_fecha_y_exige_confirmacion_explicita()
    {
        var contenido = Bytes(9);
        var est = _e.Base.Establecimiento("Tambo Uno");
        var primera = await ConfirmarBienAsync(await PrepararAsync(contenido), est);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var otra = await PrepararAsync(contenido);

        Assert.True(otra.EsArchivoRepetido);
        var aviso = Assert.Single(otra.ArchivoRepetido);
        Assert.Equal("Rodeo Mayo", aviso.Nombre);
        // La fecha es la de la primera importacion (el reloj de prueba avanza un segundo por cada copia de seguridad).
        Assert.InRange(aviso.FechaImportacionUtc, new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 12, 0, 30, DateTimeKind.Utc));

        var sinConfirmar = await ConfirmarAsync(otra, est);
        Assert.Equal(CodigoError.ArchivoRepetidoSinConfirmar, sinConfirmar.Codigo);
        Assert.Equal(1, _e.Cuenta("Rodeo"));

        var confirmada = await ConfirmarAsync(otra, est, rodeo: "Rodeo Junio", repetido: true);
        Assert.True(confirmada.EsCorrecto, confirmada.Mensaje);
        Assert.Equal(2, _e.Cuenta("Rodeo"));
        Assert.Equal(6, _e.Cuenta("Animal"));
        // El rodeo viejo no se toca.
        Assert.Equal("Rodeo Mayo", DeRodeo("Nombre", primera.RodeoId));
    }

    [Fact]
    public async Task Un_archivo_distinto_no_avisa_de_repetido()
    {
        await ConfirmarBienAsync(await PrepararAsync(Bytes(1)));

        Assert.False((await PrepararAsync(Bytes(2))).EsArchivoRepetido);
    }

    [Fact]
    public async Task Sin_ninguna_fila_valida_la_vista_se_muestra_pero_no_se_puede_confirmar()
    {
        var sinId = PerfilFalso.Lectura([PerfilFalso.Fila(2, null, "SECA", "algo"), PerfilFalso.Fila(3, null, "SECA", "otro")]);
        using var e = new EscenarioImportacion(new PerfilFalso("Otro", sinId));
        var r = await e.Importacion.PrepararAsync("x.xls", new MemoryStream(Bytes()));
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.False(r.Valor.PuedeConfirmar);
        Assert.Equal(2, r.Valor.Excluidas.Count);

        var conf = await e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(r.Valor.Id, null, "Tambo", "Rodeo", false));

        Assert.Equal(CodigoError.SinFilasValidas, conf.Codigo);
        Assert.Equal(0, e.Cuenta("Rodeo"));
        Assert.Equal(0, e.Cuenta("Establecimiento"));
    }

    // ---------- RF-17: copia a pedido primero ----------

    [Fact]
    public async Task La_copia_a_pedido_se_pide_antes_de_guardar_nada()
    {
        var vista = await PrepararAsync();
        long rodeosAlPedirla = -1, estabAlPedirla = -1;
        _e.Backup.Antes = _ =>
        {
            rodeosAlPedirla = _e.Cuenta("Rodeo");
            estabAlPedirla = _e.Cuenta("Establecimiento");
            return Task.FromResult<Resultado?>(null);
        };

        await ConfirmarBienAsync(vista);

        Assert.Equal(new[] { MotivoBackup.APedido }, _e.Backup.Pedidos);
        Assert.Equal(0, rodeosAlPedirla);
        Assert.Equal(0, estabAlPedirla);
        Assert.Single(Directory.GetFiles(_e.Rutas.DirectorioBackups, "*-a-pedido.db"));
    }

    [Fact]
    public async Task Sin_espacio_para_la_copia_no_queda_nada_y_se_puede_reintentar()
    {
        var vista = await PrepararAsync();
        _e.Espacio.Bytes = 0;

        var r = await ConfirmarAsync(vista);

        Assert.False(r.EsCorrecto);
        Assert.Equal(CodigoError.SinEspacioParaCopia, r.Codigo);
        AfirmarNadaGuardado();

        _e.Espacio.Bytes = long.MaxValue / 2;
        Assert.True((await ConfirmarAsync(vista)).EsCorrecto);   // la misma vista previa
        Assert.Equal(1, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Con_la_copia_fallida_no_queda_nada_y_se_puede_reintentar()
    {
        var vista = await PrepararAsync();
        _e.Backup.Antes = _ => Task.FromResult<Resultado?>(Resultado.Error(CodigoError.CopiaFallida, "No se pudo hacer la copia de seguridad."));

        var r = await ConfirmarAsync(vista);

        Assert.Equal(CodigoError.CopiaFallida, r.Codigo);
        AfirmarNadaGuardado();

        _e.Backup.Antes = null;
        Assert.True((await ConfirmarAsync(vista)).EsCorrecto);
    }

    [Fact]
    public async Task La_copia_a_pedido_no_desplaza_a_la_previa_a_actualizacion_mas_reciente()
    {
        // Una previa a actualización, la más vieja de todas.
        var previa = await _e.Backup.CrearAsync(MotivoBackup.PreviaActualizacion);
        Assert.True(previa.EsCorrecto);

        // Doce importaciones: sobran copias, y la previa (la más antigua) igual tiene que sobrevivir.
        for (var i = 0; i < 12; i++)
        {
            _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
            await ConfirmarBienAsync(await PrepararAsync(Bytes((byte)(i + 1))), nuevo: $"Tambo {i}");
        }

        Assert.Single(Directory.GetFiles(_e.Rutas.DirectorioBackups, "*-previa-actualizacion.db"));
        // 10 rotativas en total: la previa (que quedó dentro de las 10 más nuevas o se conserva aparte) y las a pedido.
        var aPedido = Directory.GetFiles(_e.Rutas.DirectorioBackups, "*-a-pedido.db").Length;
        Assert.InRange(aPedido, 9, 10);
    }

    // ---------- RF-18: una sola transacción ----------

    [Fact]
    public async Task Guarda_establecimiento_rodeo_y_animales_importados_sin_historial()
    {
        var res = await ConfirmarBienAsync(await PrepararAsync());

        Assert.Equal(1, _e.Cuenta("Establecimiento"));
        Assert.Equal(1, _e.Cuenta("Rodeo"));
        Assert.Equal(3, _e.Cuenta("Animal", "Origen = 'Importado'"));
        Assert.Equal(0, _e.Cuenta("Historial"));
        Assert.Equal(0, _e.Cuenta("Tacto"));
        Assert.Equal(3, _e.Cuenta("Animal", $"RodeoId = '{res.RodeoId.ToString().ToUpperInvariant()}'"));
        Assert.Equal(3, _e.Cuenta("Animal", "DatosOriginalesJson IS NOT NULL AND IdElectronico IS NULL AND Observaciones IS NULL AND FechaUltimoTactoUtc IS NULL"));
    }

    [Fact]
    public async Task Una_falla_a_mitad_no_deja_establecimiento_rodeo_ni_animales_y_se_puede_reintentar()
    {
        // La fila 3 (caravana "BOOM") hace fallar la base al insertar: los animales anteriores ya estaban "agregados".
        var lectura = PerfilFalso.Lectura([
            PerfilFalso.Fila(2, "1", "SECA"), PerfilFalso.Fila(3, "BOOM", "SECA"), PerfilFalso.Fila(4, "3", "SECA")]);
        using var e = new EscenarioImportacion(new PerfilFalso("Otro", lectura));
        e.Base.Ejecutar("CREATE TRIGGER TR_Prueba_Falla BEFORE INSERT ON Animal WHEN NEW.NumeroCaravana = 'BOOM' BEGIN SELECT RAISE(ABORT, 'falla forzada'); END");
        var vista = (await e.Importacion.PrepararAsync("x.xls", new MemoryStream(Bytes()))).Valor;
        var comando = new ConfirmarImportacionCommand(vista.Id, null, "Tambo Nuevo", "Rodeo", false);

        await Assert.ThrowsAnyAsync<Exception>(() => e.Importacion.ConfirmarAsync(comando));

        Assert.Equal(0, e.Cuenta("Establecimiento"));
        Assert.Equal(0, e.Cuenta("Rodeo"));
        Assert.Equal(0, e.Cuenta("Animal"));

        // Vuelve a Pendiente: arreglado el problema, la misma vista previa se confirma.
        e.Base.Ejecutar("DROP TRIGGER TR_Prueba_Falla");
        var ok = await e.Importacion.ConfirmarAsync(comando);
        Assert.True(ok.EsCorrecto, ok.Mensaje);
        Assert.Equal(3, e.Cuenta("Animal"));
    }

    // ---------- RF-19: una sola confirmación por vista previa ----------

    [Fact]
    public async Task Confirmar_dos_veces_la_misma_vista_deja_un_solo_rodeo()
    {
        var vista = await PrepararAsync();
        await ConfirmarBienAsync(vista);

        var otra = await ConfirmarAsync(vista);

        Assert.Equal(CodigoError.ImportacionYaConfirmada, otra.Codigo);
        Assert.Equal(1, _e.Cuenta("Rodeo"));
        Assert.Equal(3, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Mientras_se_confirma_otra_confirmacion_de_la_misma_vista_se_rechaza()
    {
        var vista = await PrepararAsync();
        var entro = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        _e.Backup.Antes = async _ => { entro.SetResult(); await soltar.Task; return null; };

        var primera = ConfirmarAsync(vista);
        await entro.Task;                                   // la primera está en curso (frenada en la copia)
        var segunda = await ConfirmarAsync(vista);
        Assert.Equal(CodigoError.ImportacionEnCurso, segunda.Codigo);

        soltar.SetResult();
        Assert.True((await primera).EsCorrecto);
        Assert.Equal(1, _e.Cuenta("Rodeo"));
    }

    [Fact]
    public async Task Muchas_confirmaciones_a_la_vez_crean_un_solo_rodeo()
    {
        var vista = await PrepararAsync();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ConfirmarAsync(vista))));

        Assert.Equal(1, resultados.Count(r => r.EsCorrecto));
        Assert.All(resultados.Where(r => !r.EsCorrecto),
            r => Assert.True(r.Codigo is CodigoError.ImportacionEnCurso or CodigoError.ImportacionYaConfirmada));
        Assert.Equal(1, _e.Cuenta("Rodeo"));
        Assert.Equal(3, _e.Cuenta("Animal"));
    }

    [Fact]
    public async Task Confirmar_una_vista_que_no_existe_se_rechaza()
    {
        var r = await _e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(Guid.NewGuid(), null, "T", "R", false));

        Assert.Equal(CodigoError.VistaPreviaNoDisponible, r.Codigo);
        AfirmarNadaGuardado();
    }

    // ---------- RF-21: resultado ----------

    [Fact]
    public async Task El_resultado_trae_leidas_importadas_excluidas_advertencias_y_nombre_del_rodeo()
    {
        var lectura = PerfilFalso.Lectura([
            PerfilFalso.Fila(2, "1", "SECA"), PerfilFalso.Fila(3, "2", "RARO"), PerfilFalso.Fila(4, null, "SECA", "x"),
            PerfilFalso.Fila(5, "4", "PREÑADA")]);
        using var e = new EscenarioImportacion(new PerfilFalso("Otro", lectura));
        var vista = (await e.Importacion.PrepararAsync("x.xls", new MemoryStream(Bytes()))).Valor;

        var r = await e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(vista.Id, null, "Tambo", "  Rodeo Final ", false));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(4, r.Valor.FilasLeidas);
        Assert.Equal(3, r.Valor.AnimalesImportados);
        Assert.Equal(1, r.Valor.FilasExcluidas);
        Assert.Equal("Rodeo Final", r.Valor.NombreRodeo);
        var adv = Assert.Single(r.Valor.Advertencias);
        Assert.Equal("RARO", adv.Valor);
    }

    // ---------- RF-22: descartar ----------

    [Fact]
    public async Task Descartar_borra_la_vista_previa_sin_guardar_nada()
    {
        var vista = await PrepararAsync();

        _e.Importacion.Descartar(vista.Id);

        var r = await ConfirmarAsync(vista);
        Assert.Equal(CodigoError.VistaPreviaNoDisponible, r.Codigo);
        AfirmarNadaGuardado();
    }

    [Fact]
    public async Task Preparar_una_segunda_vista_descarta_la_primera()
    {
        var primera = await PrepararAsync(Bytes(1));
        var segunda = await PrepararAsync(Bytes(2));

        Assert.Equal(CodigoError.VistaPreviaNoDisponible, (await ConfirmarAsync(primera)).Codigo);
        Assert.True((await ConfirmarAsync(segunda)).EsCorrecto);
    }

    [Fact]
    public async Task Preparar_no_guarda_nada_en_la_base_ni_hace_copias()
    {
        await PrepararAsync();

        AfirmarNadaGuardado();
        Assert.Empty(_e.Backup.Pedidos);
    }

    // ---------- Punta a punta con el perfil real y la muestra ----------

    private static string RutaMuestra(string archivo)
    {
        // Sube desde la carpeta de los tests hasta encontrar tests/muestras (la raíz del repo).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "muestras"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "muestras", archivo);
    }

    [Fact]
    public async Task Punta_a_punta_la_muestra_de_226_vacas_se_importa_con_el_perfil_de_Dairy_Comp()
    {
        using var e = new EscenarioImportacion(new PerfilDairyComp());
        var original = await File.ReadAllBytesAsync(RutaMuestra("dairy-comp-226-vacas.xls"));

        var prep = await e.Importacion.PrepararAsync("dairy-comp-226-vacas.xls", new MemoryStream(original));
        Assert.True(prep.EsCorrecto, prep.Mensaje);
        var vista = prep.Valor;
        Assert.Equal(226, vista.FilasLeidas);               // sin contar la fila Total:226
        Assert.Equal(226, vista.Validas.Count);
        Assert.Empty(vista.Excluidas);
        Assert.Equal(226, vista.ConteoPorEstado.Sum(c => c.Cantidad));
        Assert.DoesNotContain(vista.Advertencias, a => a.Tipo == TipoAdvertencia.TotalNoCoincide);

        var r = await e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(vista.Id, null, "Tambo de prueba", "dairy-comp-226-vacas", false));

        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(226, r.Valor.AnimalesImportados);
        Assert.Equal(226, e.Cuenta("Animal", "Origen = 'Importado'"));
        Assert.Equal(0, e.Cuenta("Historial"));
        Assert.Equal(0, e.Cuenta("Tacto"));
        Assert.Equal(226, e.Cuenta("Animal", "IdElectronico IS NULL AND FechaUltimoTactoUtc IS NULL AND Observaciones IS NULL"));
        Assert.Equal(original, (byte[])e.Base.Escalar("SELECT ArchivoOrigenBlob FROM Rodeo")!);   // byte a byte
        Assert.Equal("Dairy Comp", e.Base.Escalar("SELECT PerfilExcel FROM Rodeo"));
        Assert.Equal(vista.Hash, e.Base.Escalar("SELECT ArchivoOrigenHash FROM Rodeo"));
        // El JSON de originales conserva las columnas del archivo.
        var json = (string)e.Base.Escalar("SELECT DatosOriginalesJson FROM Animal LIMIT 1")!;
        Assert.Contains("\"ID\"", json);
    }

    [Fact]
    public async Task Punta_a_punta_filas_con_problemas_importa_las_buenas_y_excluye_las_malas()
    {
        using var e = new EscenarioImportacion(new PerfilDairyComp());
        var bytes = await File.ReadAllBytesAsync(RutaMuestra("filas-con-problemas.xls"));

        var prep = await e.Importacion.PrepararAsync("filas-con-problemas.xls", new MemoryStream(bytes));

        Assert.True(prep.EsCorrecto, prep.Mensaje);
        Assert.NotEmpty(prep.Valor.Excluidas);
        Assert.True(prep.Valor.PuedeConfirmar);
        var r = await e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(prep.Valor.Id, null, "Tambo", "Rodeo", false));
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(prep.Valor.Validas.Count, e.Cuenta("Animal"));
    }

    // ---------- Con el migrador real (EF) ----------

    [Fact]
    public async Task Con_el_MigradorEf_real_la_migracion_Inicial_crea_la_base_la_reabre_sin_pendientes_y_permite_importar()
    {
        // Carpeta vacía: la base la crea el arranque de verdad (sin el migrador falso).
        using var e = new EscenarioImportacion(new PerfilDairyComp(), migrar: false);
        var migrador = new MigradorEf();

        var primero = await e.Arranque.IniciarAsync();
        Assert.Equal(TipoResultadoArranque.BaseCreada, primero.Tipo);
        Assert.Equal(migrador.Conocidas(), migrador.Aplicadas(e.Rutas.BaseDeDatos));   // nada pendiente

        var segundo = await e.Arranque.IniciarAsync();   // reabrir
        Assert.Equal(TipoResultadoArranque.Correcto, segundo.Tipo);
        Assert.Equal(migrador.Conocidas(), migrador.Aplicadas(e.Rutas.BaseDeDatos));

        var original = await File.ReadAllBytesAsync(RutaMuestra("dairy-comp-226-vacas.xls"));
        var prep = await e.Importacion.PrepararAsync("m.xls", new MemoryStream(original));
        Assert.True(prep.EsCorrecto, prep.Mensaje);
        Assert.Contains(prep.Valor.ConteoPorEstado, c => c.EstadoId is not null);   // el arranque sembró los estados
        var r = await e.Importacion.ConfirmarAsync(new ConfirmarImportacionCommand(prep.Valor.Id, null, "Tambo", "Rodeo", false));
        Assert.True(r.EsCorrecto, r.Mensaje);
        Assert.Equal(226, e.Cuenta("Animal"));
    }
}
