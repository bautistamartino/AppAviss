using Aviss.App.ViewModels;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Importacion;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Tests.ViewModels;

// Prueban la lógica de la pantalla de importación sin MAUI: el selector y el servicio son falsos.
public class ImportarViewModelTests
{
    // ------------------------------------------------------------------ falsos

    private sealed class SelectorFalso : ISelectorArchivo
    {
        public ArchivoElegido? Siguiente { get; set; }
        public Exception? Falla { get; set; }
        public int Llamadas { get; private set; }

        public Task<ArchivoElegido?> ElegirAsync(CancellationToken ct = default)
        {
            Llamadas++;
            if (Falla is not null) throw Falla;
            return Task.FromResult(Siguiente);
        }
    }

    private sealed class ServicioFalso : IImportacionService
    {
        public Resultado<VistaPreviaImportacion>? RespuestaPreparar { get; set; }
        public IReadOnlyList<EstablecimientoResumen> Establecimientos { get; set; } = [];
        public Func<ConfirmarImportacionCommand, Task<Resultado<ResultadoImportacion>>>? Confirmar { get; set; }
        public List<string> Preparados { get; } = [];
        public List<ConfirmarImportacionCommand> Confirmados { get; } = [];
        public List<Guid> Descartados { get; } = [];
        public Exception? FallaPreparar { get; set; }

        public Task<Resultado<VistaPreviaImportacion>> PrepararAsync(string nombreArchivo, Stream contenido, CancellationToken ct = default)
        {
            Preparados.Add(nombreArchivo);
            if (FallaPreparar is not null) throw FallaPreparar;
            return Task.FromResult(RespuestaPreparar!);
        }

        public Task<IReadOnlyList<EstablecimientoResumen>> ListarEstablecimientosActivosAsync(CancellationToken ct = default)
            => Task.FromResult(Establecimientos);

        public Task<Resultado<ResultadoImportacion>> ConfirmarAsync(ConfirmarImportacionCommand comando, CancellationToken ct = default)
        {
            Confirmados.Add(comando);
            return Confirmar!(comando);
        }

        public void Descartar(Guid vistaPreviaId) => Descartados.Add(vistaPreviaId);

        public Task<Resultado<Guid>> ResetearRodeoAsync(Guid rodeoId, CancellationToken ct = default)
            => throw new NotSupportedException("No se usa en estas pruebas.");
    }

    private sealed class NavegacionFalsa : INavegacion
    {
        public int AInicio { get; private set; }
        public int AImportar { get; private set; }
        public Task IrAInicioAsync() { AInicio++; return Task.CompletedTask; }
        public Task IrAImportarAsync() { AImportar++; return Task.CompletedTask; }
        public Task IrAExportarAsync() => Task.CompletedTask;
        public Task IrAEleccionTrabajoAsync() => Task.CompletedTask;
        public Task IrATrabajoAsync() => Task.CompletedTask;
        public Task IrAMovimientosAsync() => Task.CompletedTask;
        public Task IrAElegirBastonAsync() => Task.CompletedTask;
        public Task VolverAsync() => Task.CompletedTask;
    }

    // ------------------------------------------------------------------ armado

    private static ArchivoElegido Archivo(string nombre = "Tambo Norte 2026.xls")
        => new(nombre, new MemoryStream([1, 2, 3]));

    private static FilaAnimalImportada Fila(int n) => new(n, Caravana.TryCrear($"A{n}", out var c, out _) ? c! : throw new InvalidOperationException(), null, "{}");

    private static VistaPreviaImportacion Vista(
        string nombreArchivo = "Tambo Norte 2026.xls", int validas = 2, int excluidas = 1, bool repetido = false)
        => new(Guid.NewGuid(), nombreArchivo, "hash", [1], "Dairy Comp", validas + excluidas,
            Enumerable.Range(1, validas).Select(Fila).ToList(),
            Enumerable.Range(100, excluidas).Select(n => new FilaExcluida(n, MotivoFila.IdRepetido)).ToList(),
            [new AdvertenciaImportacion(TipoAdvertencia.EstadoDesconocido, "aviso", "XX", 1, [5])],
            [new ConteoEstado(Guid.NewGuid(), "PREÑADA", validas)],
            repetido ? [new RodeoImportadoResumen("Rodeo viejo", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))] : []);

    private static ResultadoImportacion ResultadoDe(VistaPreviaImportacion v, string nombre)
        => new(Guid.NewGuid(), nombre, v.FilasLeidas, v.Validas.Count, v.Excluidas.Count, v.Advertencias);

    private sealed record Entorno(ImportarViewModel Vm, SelectorFalso Selector, ServicioFalso Servicio, NavegacionFalsa Nav);

    private static Entorno Crear()
    {
        var selector = new SelectorFalso { Siguiente = Archivo() };
        var servicio = new ServicioFalso();
        var nav = new NavegacionFalsa();
        return new Entorno(new ImportarViewModel(servicio, selector, nav), selector, servicio, nav);
    }

    /// <summary>Deja el ViewModel en el paso de vista previa con esa vista.</summary>
    private static async Task<VistaPreviaImportacion> ConVistaPrevia(Entorno e, VistaPreviaImportacion? vista = null)
    {
        vista ??= Vista();
        e.Servicio.RespuestaPreparar = Resultado.Ok(vista);
        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);
        return vista;
    }

    // ------------------------------------------------------------------ paso 1: elegir

    [Fact]
    public void Arranca_en_el_paso_de_elegir_y_sin_mensajes()
    {
        var e = Crear();
        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.True(e.Vm.EsPasoElegir);
        Assert.Null(e.Vm.MensajeError);
        Assert.True(e.Vm.ElegirArchivoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selector_cancelado_no_cambia_nada()
    {
        var e = Crear();
        e.Selector.Siguiente = null;

        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.Null(e.Vm.MensajeError);
        Assert.Empty(e.Servicio.Preparados);
    }

    [Fact]
    public async Task Archivo_rechazado_muestra_el_mensaje_y_permite_elegir_otro()
    {
        var e = Crear();
        e.Servicio.RespuestaPreparar = Resultado.Error<VistaPreviaImportacion>(
            CodigoError.ArchivoRechazado, MensajesImportacion.Para(MotivoRechazoArchivo.FormatoNoSoportado));

        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.Equal(MensajesImportacion.Para(MotivoRechazoArchivo.FormatoNoSoportado), e.Vm.MensajeError);
        Assert.True(e.Vm.ElegirArchivoCommand.CanExecute(null));

        // Se elige otro y el mensaje viejo desaparece.
        e.Selector.Siguiente = Archivo("otro.xls");
        e.Servicio.RespuestaPreparar = Resultado.Ok(Vista());
        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);
        Assert.Null(e.Vm.MensajeError);
        Assert.Equal(PasoImportacion.VistaPrevia, e.Vm.Paso);
    }

    [Fact]
    public async Task Falla_inesperada_al_leer_muestra_un_mensaje_fijo_sin_datos()
    {
        var e = Crear();
        e.Servicio.FallaPreparar = new IOException("C:\\datos\\Tambo Norte\\secreto.xls");

        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.False(string.IsNullOrWhiteSpace(e.Vm.MensajeError));
        Assert.DoesNotContain("Tambo Norte", e.Vm.MensajeError);
        Assert.DoesNotContain("secreto", e.Vm.MensajeError);
        Assert.False(e.Vm.EstaOcupado);
    }

    [Fact]
    public async Task Falla_del_selector_muestra_un_mensaje_fijo()
    {
        var e = Crear();
        e.Selector.Falla = new UnauthorizedAccessException("permiso de /sdcard/Tambo");

        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.False(string.IsNullOrWhiteSpace(e.Vm.MensajeError));
        Assert.DoesNotContain("Tambo", e.Vm.MensajeError);
    }

    [Fact]
    public async Task El_flujo_del_archivo_se_cierra_despues_de_leerlo()
    {
        var e = Crear();
        var flujo = new MemoryStream([1]);
        e.Selector.Siguiente = new ArchivoElegido("a.xls", flujo);
        e.Servicio.RespuestaPreparar = Resultado.Ok(Vista());

        await e.Vm.ElegirArchivoCommand.ExecuteAsync(null);

        Assert.False(flujo.CanRead);
    }

    // ------------------------------------------------------------------ paso 2: vista previa

    [Fact]
    public async Task Vista_previa_muestra_resumen_estados_advertencias_y_excluidas()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e, Vista(validas: 3, excluidas: 2));

        Assert.Equal(PasoImportacion.VistaPrevia, e.Vm.Paso);
        Assert.Same(vista, e.Vm.Vista);
        Assert.Equal(5, e.Vm.FilasLeidas);
        Assert.Equal(3, e.Vm.FilasAImportar);
        Assert.Equal(2, e.Vm.FilasExcluidas);
        Assert.Single(e.Vm.Vista!.ConteoPorEstado);
        Assert.Single(e.Vm.Vista.Advertencias);
        Assert.Equal(2, e.Vm.Vista.Excluidas.Count);
        Assert.True(e.Vm.HayExcluidas);
        Assert.True(e.Vm.HayAdvertencias);
        Assert.False(e.Vm.EsArchivoRepetido);
    }

    [Fact]
    public async Task Las_advertencias_se_muestran_con_sus_filas_de_ejemplo()
    {
        var e = Crear();
        await ConVistaPrevia(e);

        var texto = Assert.Single(e.Vm.AdvertenciasTexto);
        Assert.Contains("aviso", texto);
        Assert.Contains("5", texto);
    }

    [Fact]
    public async Task Nombre_del_rodeo_propuesto_es_el_del_archivo_sin_extension()
    {
        var e = Crear();
        await ConVistaPrevia(e, Vista(nombreArchivo: "Tambo Norte 2026.xls"));
        Assert.Equal("Tambo Norte 2026", e.Vm.NombreRodeo);
    }

    [Fact]
    public async Task Nombre_propuesto_largo_se_recorta_al_maximo_permitido()
    {
        var e = Crear();
        await ConVistaPrevia(e, Vista(nombreArchivo: new string('a', 150) + ".xls"));
        Assert.Equal(LimitesImportacion.MaxLargoNombre, e.Vm.NombreRodeo.Length);
    }

    [Fact]
    public async Task Sin_establecimientos_hay_que_escribir_uno_nuevo()
    {
        var e = Crear();
        await ConVistaPrevia(e);

        Assert.False(e.Vm.HayEstablecimientos);
        Assert.True(e.Vm.UsarEstablecimientoNuevo);
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));   // falta el nombre

        e.Vm.NuevoEstablecimientoNombre = "Tambo Nuevo";
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Con_establecimientos_se_elige_uno_de_la_lista_y_no_hay_que_escribir_nada()
    {
        var e = Crear();
        var est = new EstablecimientoResumen(Guid.NewGuid(), "La Vaca");
        e.Servicio.Establecimientos = [est, new EstablecimientoResumen(Guid.NewGuid(), "Otro")];
        await ConVistaPrevia(e);

        Assert.True(e.Vm.HayEstablecimientos);
        Assert.False(e.Vm.UsarEstablecimientoNuevo);
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));   // todavía no eligió

        e.Vm.EstablecimientoSeleccionado = est;
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task No_se_puede_confirmar_sin_nombre_de_rodeo()
    {
        var e = Crear();
        await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));

        e.Vm.NombreRodeo = "   ";
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task No_se_puede_confirmar_sin_filas_validas()
    {
        var e = Crear();
        await ConVistaPrevia(e, Vista(validas: 0, excluidas: 3));
        e.Vm.NuevoEstablecimientoNombre = "Tambo";

        Assert.False(e.Vm.PuedeConfirmar);
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));
        Assert.True(e.Vm.HayExcluidas);   // igual se ve por qué quedaron afuera
    }

    [Fact]
    public async Task Archivo_repetido_avisa_y_exige_importar_igual()
    {
        var e = Crear();
        await ConVistaPrevia(e, Vista(repetido: true));
        e.Vm.NuevoEstablecimientoNombre = "Tambo";

        Assert.True(e.Vm.EsArchivoRepetido);
        Assert.Equal("Rodeo viejo", e.Vm.Vista!.ArchivoRepetido[0].Nombre);
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));   // falta "Importar igual"

        e.Vm.ConfirmaImportarIgual = true;
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));

        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(e.Vm.Vista!, c.NombreRodeo)));
        await e.Vm.ConfirmarCommand.ExecuteAsync(null);
        Assert.True(e.Servicio.Confirmados[0].ConfirmaImportarArchivoRepetido);
    }

    [Fact]
    public async Task Archivo_nuevo_no_manda_la_confirmacion_de_repetido()
    {
        var e = Crear();
        await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(e.Vm.Vista!, c.NombreRodeo)));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.False(e.Servicio.Confirmados[0].ConfirmaImportarArchivoRepetido);
    }

    // ------------------------------------------------------------------ confirmar

    [Fact]
    public async Task Confirmar_con_establecimiento_existente_manda_el_id_y_no_el_nombre_nuevo()
    {
        var e = Crear();
        var est = new EstablecimientoResumen(Guid.NewGuid(), "La Vaca");
        e.Servicio.Establecimientos = [est];
        var vista = await ConVistaPrevia(e);
        e.Vm.EstablecimientoSeleccionado = est;
        e.Vm.NuevoEstablecimientoNombre = "Texto que sobra";
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(vista, c.NombreRodeo)));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(e.Servicio.Confirmados);
        Assert.Equal(vista.Id, cmd.VistaPreviaId);
        Assert.Equal(est.Id, cmd.EstablecimientoId);
        Assert.Null(cmd.NuevoEstablecimientoNombre);
        Assert.Equal("Tambo Norte 2026", cmd.NombreRodeo);
    }

    [Fact]
    public async Task Confirmar_con_establecimiento_nuevo_manda_el_nombre_recortado()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "  Tambo Nuevo  ";
        e.Vm.NombreRodeo = "  Rodeo 1 ";
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(vista, c.NombreRodeo)));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        var cmd = Assert.Single(e.Servicio.Confirmados);
        Assert.Null(cmd.EstablecimientoId);
        Assert.Equal("Tambo Nuevo", cmd.NuevoEstablecimientoNombre);
        Assert.Equal("Rodeo 1", cmd.NombreRodeo);
    }

    [Fact]
    public async Task Confirmar_bien_lleva_al_resultado_con_los_numeros()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e, Vista(validas: 4, excluidas: 1));
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(vista, c.NombreRodeo)));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Resultado, e.Vm.Paso);
        Assert.NotNull(e.Vm.Resultado);
        Assert.Equal("Tambo Norte 2026", e.Vm.Resultado!.NombreRodeo);
        Assert.Equal(5, e.Vm.Resultado.FilasLeidas);
        Assert.Equal(4, e.Vm.Resultado.AnimalesImportados);
        Assert.Equal(1, e.Vm.Resultado.FilasExcluidas);
        Assert.Null(e.Vm.MensajeError);
        Assert.False(e.Vm.EstaConfirmando);
    }

    [Fact]
    public async Task Confirmar_deshabilitado_y_elegir_deshabilitado_mientras_corre()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        var compuerta = new TaskCompletionSource<Resultado<ResultadoImportacion>>();
        e.Servicio.Confirmar = _ => compuerta.Task;

        var enCurso = e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.True(e.Vm.EstaConfirmando);
        Assert.False(e.Vm.ConfirmarCommand.CanExecute(null));
        Assert.False(e.Vm.ElegirArchivoCommand.CanExecute(null));
        Assert.False(e.Vm.CancelarCommand.CanExecute(null));

        // Un segundo toque no llega al servicio.
        await e.Vm.ConfirmarCommand.ExecuteAsync(null);
        Assert.Single(e.Servicio.Confirmados);

        compuerta.SetResult(Resultado.Ok(ResultadoDe(vista, "x")));
        await enCurso;

        Assert.False(e.Vm.EstaConfirmando);
        Assert.Equal(PasoImportacion.Resultado, e.Vm.Paso);
    }

    [Fact]
    public async Task Rechazo_del_servicio_muestra_el_mensaje_y_queda_en_la_vista_previa_para_reintentar()
    {
        var e = Crear();
        await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        e.Servicio.Confirmar = _ => Task.FromResult(
            Resultado.Error<ResultadoImportacion>(CodigoError.Duplicado, MensajesImportacion.EstablecimientoRepetido));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.VistaPrevia, e.Vm.Paso);
        Assert.Equal(MensajesImportacion.EstablecimientoRepetido, e.Vm.MensajeError);
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));   // se corrige y se reintenta
    }

    [Fact]
    public async Task Excepcion_real_al_confirmar_muestra_un_mensaje_fijo_sin_datos_y_permite_reintentar()
    {
        var e = Crear();
        await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo Secreto";
        e.Servicio.Confirmar = _ => throw new IOException("Disco lleno en /data/Tambo Secreto/rodeo");

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.VistaPrevia, e.Vm.Paso);
        Assert.False(string.IsNullOrWhiteSpace(e.Vm.MensajeError));
        Assert.DoesNotContain("Secreto", e.Vm.MensajeError);
        Assert.DoesNotContain("Disco", e.Vm.MensajeError);
        Assert.False(e.Vm.EstaConfirmando);
        Assert.True(e.Vm.ConfirmarCommand.CanExecute(null));

        // El reintento anda.
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(e.Vm.Vista!, c.NombreRodeo)));
        await e.Vm.ConfirmarCommand.ExecuteAsync(null);
        Assert.Equal(PasoImportacion.Resultado, e.Vm.Paso);
    }

    [Theory]
    [InlineData(CodigoError.VistaPreviaNoDisponible)]
    [InlineData(CodigoError.ImportacionYaConfirmada)]
    public async Task Si_la_vista_previa_ya_no_existe_vuelve_a_elegir_con_el_mensaje(CodigoError codigo)
    {
        var e = Crear();
        await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        e.Servicio.Confirmar = _ => Task.FromResult(Resultado.Error<ResultadoImportacion>(codigo, MensajesImportacion.Para(codigo)));

        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.Equal(MensajesImportacion.Para(codigo), e.Vm.MensajeError);
        Assert.Null(e.Vm.Vista);
    }

    // ------------------------------------------------------------------ cancelar y resultado

    [Fact]
    public async Task Cancelar_descarta_la_vista_previa_y_vuelve_a_elegir()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";

        await e.Vm.CancelarCommand.ExecuteAsync(null);

        Assert.Equal(vista.Id, Assert.Single(e.Servicio.Descartados));
        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.Null(e.Vm.Vista);
        Assert.Equal("", e.Vm.NuevoEstablecimientoNombre);
        Assert.Equal("", e.Vm.NombreRodeo);
        Assert.Empty(e.Servicio.Confirmados);
    }

    [Fact]
    public async Task Importar_otro_vuelve_al_primer_paso_limpio()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e);
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        e.Servicio.Confirmar = c => Task.FromResult(Resultado.Ok(ResultadoDe(vista, c.NombreRodeo)));
        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        await e.Vm.ImportarOtroCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Elegir, e.Vm.Paso);
        Assert.Null(e.Vm.Resultado);
        Assert.Null(e.Vm.Vista);
        Assert.Empty(e.Servicio.Descartados);   // ya estaba consumida: no hay nada que descartar
    }

    [Fact]
    public async Task Volver_al_inicio_navega()
    {
        var e = Crear();
        await e.Vm.VolverAlInicioCommand.ExecuteAsync(null);
        Assert.Equal(1, e.Nav.AInicio);
    }

    // ------------------------------------------------------------------ InicioViewModel

    [Fact]
    public async Task Inicio_importar_rodeo_navega_a_la_pantalla_de_importacion()
    {
        var nav = new NavegacionFalsa();
        var vm = new InicioViewModel(nav);

        await vm.ImportarRodeoCommand.ExecuteAsync(null);

        Assert.Equal(1, nav.AImportar);
    }

    // ------------------------------------------------------------------ independencia de MAUI

    [Fact]
    public void Los_ViewModels_no_referencian_Microsoft_Maui()
    {
        var ensamblado = typeof(ImportarViewModel).Assembly;
        var tiposDeVm = new[] { typeof(ImportarViewModel), typeof(InicioViewModel), typeof(INavegacion) };
        foreach (var tipo in tiposDeVm)
        {
            var usados = tipo.GetProperties().Select(p => p.PropertyType)
                .Concat(tipo.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .Concat(tipo.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
            Assert.DoesNotContain(usados, t => t.Namespace?.StartsWith("Microsoft.Maui") == true);
        }
        Assert.DoesNotContain(ensamblado.GetReferencedAssemblies(), a => a.Name?.StartsWith("Microsoft.Maui") == true);
    }

    // ------------------------------------------------------------------ spec 010: resumen de EID

    private static VistaPreviaImportacion VistaConEid(bool hayColumna, int conEid, int sinEid, IReadOnlyList<AdvertenciaImportacion>? advertencias = null)
        => Vista() with { HayColumnaEid = hayColumna, ConEid = conEid, SinEid = sinEid, Advertencias = advertencias ?? [] };

    [Fact]
    public async Task Con_columna_EID_la_vista_previa_dice_cuantos_traen_EID_y_cuantos_no()
    {
        var e = Crear();
        await ConVistaPrevia(e, VistaConEid(true, 272, 10));

        Assert.True(e.Vm.HayResumenEid);
        Assert.Equal("Con EID: 272 · Sin EID: 10", e.Vm.ResumenEidTexto);
    }

    [Fact]
    public async Task Sin_columna_EID_no_hay_linea_de_resumen()
    {
        var e = Crear();
        await ConVistaPrevia(e, VistaConEid(false, 0, 0));

        Assert.False(e.Vm.HayResumenEid);
        Assert.Equal("", e.Vm.ResumenEidTexto);
    }

    [Fact]
    public async Task Las_advertencias_de_EID_invalido_y_repetido_salen_en_la_lista_sin_el_valor()
    {
        var e = Crear();
        var adv = new List<AdvertenciaImportacion>
        {
            new(TipoAdvertencia.EidInvalido, MensajesImportacion.EidInvalido(2), null, 2, [5, 9]),
            new(TipoAdvertencia.EidRepetido, MensajesImportacion.EidRepetido(3), null, 3, [7]),
        };
        await ConVistaPrevia(e, VistaConEid(true, 269, 13, adv));

        Assert.Equal(2, e.Vm.AdvertenciasTexto.Count);
        Assert.Contains("Filas de ejemplo: 5, 9.", e.Vm.AdvertenciasTexto[0]);
        Assert.Contains("EID", e.Vm.AdvertenciasTexto[1]);
        Assert.Equal("Con EID: 269 · Sin EID: 13", e.Vm.ResumenEidTexto);
    }

    [Fact]
    public async Task El_resultado_repite_el_resumen_de_EID_y_al_reiniciar_desaparece()
    {
        var e = Crear();
        var vista = await ConVistaPrevia(e, VistaConEid(true, 272, 10));
        e.Servicio.Confirmar = _ => Task.FromResult(Resultado.Ok(
            ResultadoDe(vista, "R") with { HayColumnaEid = true, ConEid = 272, SinEid = 10 }));
        e.Vm.NuevoEstablecimientoNombre = "Tambo";
        await e.Vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoImportacion.Resultado, e.Vm.Paso);
        Assert.True(e.Vm.HayResumenEid);
        Assert.Equal("Con EID: 272 · Sin EID: 10", e.Vm.ResumenEidTexto);

        await e.Vm.ImportarOtroCommand.ExecuteAsync(null);
        Assert.False(e.Vm.HayResumenEid);
    }
}
