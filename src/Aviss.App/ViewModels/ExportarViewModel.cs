using System.Collections.ObjectModel;
using Aviss.Application.Comun;
using Aviss.Application.Exportacion;
using Aviss.Application.Puertos;
using Aviss.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Los pasos de la pantalla "Exportar" (spec 007). Es una sola página: cambia lo que se muestra, no la pantalla.</summary>
public enum PasoExportacion { Lista, Resumen, Generando, Resultado }

/// <summary>Los rodeos de un establecimiento, para mostrar la lista agrupada (RF-1).</summary>
public sealed class GrupoRodeosExportacion : List<RodeoParaExportarDto>
{
    public GrupoRodeosExportacion(string establecimientoNombre, IEnumerable<RodeoParaExportarDto> rodeos) : base(rodeos)
        => EstablecimientoNombre = establecimientoNombre;

    public string EstablecimientoNombre { get; }
}

/// <summary>
/// Lógica de la pantalla "Exportar" (spec 007, RF-1 a RF-3, RF-19, RF-20, RF-24, RF-28): lista de rodeos, resumen, elegir el
/// formato al final, generar, ver el resultado y compartir. No usa tipos de MAUI: habla con el servicio de exportación, con la
/// entrega de archivos y con la navegación por interfaces. Ningún texto lleva caravanas ni nombres de animales (RF-27); los
/// nombres de establecimiento y rodeo solo aparecen en pantalla, nunca en mensajes de error.
/// </summary>
public sealed partial class ExportarViewModel : ObservableObject
{
    // Mensajes fijos para las fallas reales (excepciones): nunca llevan datos del tambo (RF-27).
    public const string MensajeFallaLista = "No se pudieron leer los rodeos. Probá de nuevo.";
    public const string MensajeFallaResumen = "No se pudo leer el resumen del rodeo. Probá de nuevo.";
    public const string MensajeFallaCompartir = "No se pudo abrir el menú de compartir. El archivo quedó guardado igual.";

    private readonly IExportacionService _servicio;
    private readonly IEntregaArchivo _entrega;
    private readonly INavegacion _navegacion;
    private readonly IZonaHoraria _zona;

    // Se cancela al volver atrás mientras se genera (RF-28): el servicio no guarda nada si se cancela.
    private CancellationTokenSource? _cancelacion;
    private FormatoExportacion? _ultimoFormato;

    public ExportarViewModel(IExportacionService servicio, IEntregaArchivo entrega, INavegacion navegacion, IZonaHoraria zona)
        => (_servicio, _entrega, _navegacion, _zona) = (servicio, entrega, navegacion, zona);

    // ------------------------------------------------------------------ estado

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsPasoLista), nameof(EsPasoResumen), nameof(EsPasoGenerando), nameof(EsPasoResultado), nameof(PuedeGenerar), nameof(HayReintento))]
    [NotifyCanExecuteChangedFor(nameof(GenerarCsvCommand), nameof(GenerarXlsCommand), nameof(ReintentarCommand), nameof(CancelarGeneracionCommand))]
    private PasoExportacion _paso = PasoExportacion.Lista;

    /// <summary>Los rodeos activos agrupados por establecimiento (RF-1).</summary>
    public ObservableCollection<GrupoRodeosExportacion> Grupos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayRodeos), nameof(NoHayRodeos))]
    private int _cantidadRodeos;

    /// <summary>Leyendo la lista o el resumen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeGenerar), nameof(NoHayRodeos))]
    [NotifyCanExecuteChangedFor(nameof(GenerarCsvCommand), nameof(GenerarXlsCommand))]
    private bool _estaCargando;

    [ObservableProperty]
    private RodeoParaExportarDto? _rodeoElegido;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoConEvento), nameof(TextoSinEvento), nameof(HayFueraDelArchivo), nameof(TextoFueraDelArchivo),
        nameof(HayJornada), nameof(TextoJornada), nameof(HayUltimaExportacion), nameof(TextoUltimaExportacion), nameof(PuedeGenerar))]
    [NotifyCanExecuteChangedFor(nameof(GenerarCsvCommand), nameof(GenerarXlsCommand))]
    private ResumenExportacionDto? _resumen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NombreArchivo), nameof(TextoLugar), nameof(TextoCantidades), nameof(AvisoConstanciaNoGuardada),
        nameof(HayAvisoConstancia))]
    private ExportacionEntregadaDto? _resultado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    /// <summary>La última generación falló de una forma que vale la pena repetir (RF-24): se ofrece "Reintentar".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReintentarCommand))]
    [NotifyPropertyChangedFor(nameof(HayReintento))]
    private bool _puedeReintentar;

    // ------------------------------------------------------------------ derivados (para la pantalla)

    public bool EsPasoLista => Paso == PasoExportacion.Lista;
    public bool EsPasoResumen => Paso == PasoExportacion.Resumen;
    public bool EsPasoGenerando => Paso == PasoExportacion.Generando;
    public bool EsPasoResultado => Paso == PasoExportacion.Resultado;
    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);
    public bool HayRodeos => CantidadRodeos > 0;
    public bool NoHayRodeos => CantidadRodeos == 0 && !EstaCargando;
    public bool HayReintento => PuedeReintentar && EsPasoResumen;

    /// <summary>Se puede generar solo desde el resumen y con el resumen ya cargado: nunca antes de elegir el formato (RF-3).</summary>
    public bool PuedeGenerar => Paso == PasoExportacion.Resumen && Resumen is not null && !EstaCargando;

    /// <summary>"N de M vacas con evento" (RF-2): las vacas del archivo original, el mismo criterio que la constancia.</summary>
    public string TextoConEvento => Resumen is null ? "" : $"{Resumen.VacasConEvento} de {Resumen.TotalVacas} vacas con evento";

    public string TextoSinEvento => Resumen is null ? "" : $"Sin evento: {Resumen.VacasSinEvento}";

    public bool HayFueraDelArchivo => Resumen is { VacasFueraDelArchivoConEvento: > 0 };

    /// <summary>Línea fija: son vacas con evento que no tienen fila en el archivo original, así que no salen en él (RF-2).</summary>
    public string TextoFueraDelArchivo => Resumen is null ? "" : $"Con evento y fuera del archivo (altas en campo o ID repetido): {Resumen.VacasFueraDelArchivoConEvento}";

    public bool HayJornada => Resumen?.Jornada is not null;

    public string TextoJornada => Resumen?.Jornada is { } dia ? $"Tacto del día {dia:dd/MM/yyyy}" : "";

    public bool HayUltimaExportacion => Resumen?.UltimaExportacion is not null;

    /// <summary>"Última exportación: dd/MM/yyyy HH:mm, N vacas", en la hora de la tablet (RF-2).</summary>
    public string TextoUltimaExportacion => Resumen?.UltimaExportacion is { } u
        ? $"Última exportación: {TimeZoneInfo.ConvertTimeFromUtc(u.FechaUtc, _zona.Zona):dd/MM/yyyy HH:mm}, {u.CantidadVacasConEvento} vacas"
        : "";

    public string NombreArchivo => Resultado?.Archivo.Nombre ?? "";

    /// <summary>"Guardado en Descargas" (RF-19).</summary>
    public string TextoLugar => Resultado is null ? "" : $"Guardado en {Resultado.Archivo.Lugar}";

    public string TextoCantidades => Resultado is null ? "" : $"{Resultado.Filas} filas, {Resultado.VacasConEvento} vacas con evento";

    public bool HayAvisoConstancia => Resultado is { ConstanciaGuardada: false };

    public string AvisoConstanciaNoGuardada => HayAvisoConstancia ? MensajesExportacion.ConstanciaNoGuardada : "";

    // ------------------------------------------------------------------ paso 1: lista de rodeos

    /// <summary>Lee los rodeos activos (RF-1). Se llama cada vez que se muestra la pantalla; no hace nada si ya se está exportando.</summary>
    public async Task CargarAsync()
    {
        if (Paso != PasoExportacion.Lista) return;
        EstaCargando = true;
        try
        {
            var rodeos = await _servicio.ListarRodeosAsync();
            Grupos.Clear();
            // El servicio ya los devuelve ordenados por establecimiento y rodeo: se agrupan sin reordenar.
            foreach (var grupo in rodeos.GroupBy(r => r.EstablecimientoId))
                Grupos.Add(new GrupoRodeosExportacion(grupo.First().EstablecimientoNombre, grupo));
            CantidadRodeos = rodeos.Count;
        }
        catch (Exception)
        {
            MensajeError = MensajeFallaLista;
        }
        finally
        {
            EstaCargando = false;
        }
    }

    /// <summary>Tocar un rodeo: muestra el resumen antes de generar (RF-2). No genera nada.</summary>
    [RelayCommand]
    private async Task ElegirRodeoAsync(RodeoParaExportarDto? rodeo)
    {
        if (rodeo is null || Paso != PasoExportacion.Lista || EstaCargando) return;
        EstaCargando = true;
        MensajeError = null;
        try
        {
            var respuesta = await _servicio.ObtenerResumenAsync(rodeo.RodeoId);
            if (!respuesta.EsCorrecto)
            {
                // El rodeo ya no existe o está de baja (RF-23): se avisa y se refresca la lista.
                MensajeError = respuesta.Mensaje;
                EstaCargando = false;
                await CargarAsync();
                return;
            }

            RodeoElegido = rodeo;
            Resumen = respuesta.Valor;
            PuedeReintentar = false;
            Paso = PasoExportacion.Resumen;
        }
        catch (Exception)
        {
            MensajeError = MensajeFallaResumen;
        }
        finally
        {
            EstaCargando = false;
        }
    }

    // ------------------------------------------------------------------ paso 2: resumen y formato

    // Un botón por formato: el formato se elige acá, al final, y no hay ninguno de antemano (RF-3).
    private bool PuedeGenerarComando() => PuedeGenerar;

    [RelayCommand(CanExecute = nameof(PuedeGenerarComando))]
    private Task GenerarCsvAsync() => GenerarAsync(FormatoExportacion.Csv);

    [RelayCommand(CanExecute = nameof(PuedeGenerarComando))]
    private Task GenerarXlsAsync() => GenerarAsync(FormatoExportacion.Xls);

    private bool PuedeReintentarComando() => HayReintento && _ultimoFormato is not null;

    [RelayCommand(CanExecute = nameof(PuedeReintentarComando))]
    private Task ReintentarAsync() => _ultimoFormato is { } formato ? GenerarAsync(formato) : Task.CompletedTask;

    // ------------------------------------------------------------------ paso 3: generando

    private async Task GenerarAsync(FormatoExportacion formato)
    {
        // Doble toque: aunque el botón esté habilitado un instante, acá se corta.
        if (!PuedeGenerar || RodeoElegido is null) return;

        _ultimoFormato = formato;
        MensajeError = null;
        PuedeReintentar = false;
        Paso = PasoExportacion.Generando;
        var cancelacion = _cancelacion = new CancellationTokenSource();
        try
        {
            var respuesta = await _servicio.ExportarAsync(RodeoElegido.RodeoId, formato, cancelacion.Token);
            if (respuesta.EsCorrecto)
            {
                Resultado = respuesta.Valor;
                Paso = PasoExportacion.Resultado;
                return;
            }

            // Rechazo con mensaje fijo: se vuelve al resumen y se puede reintentar (los que tienen sentido, RF-24).
            MensajeError = respuesta.Mensaje;
            PuedeReintentar = respuesta.Codigo is CodigoError.ExportacionNoValida or CodigoError.ArchivoNoGuardado;
            Paso = PasoExportacion.Resumen;
        }
        catch (OperationCanceledException)
        {
            // El veterinario canceló: el servicio no guardó ni registró nada. Vuelve al resumen sin ningún aviso de error.
            Paso = PasoExportacion.Resumen;
        }
        catch (Exception)
        {
            MensajeError = MensajesExportacion.NoSePudoGenerar;
            PuedeReintentar = true;
            Paso = PasoExportacion.Resumen;
        }
        finally
        {
            if (ReferenceEquals(_cancelacion, cancelacion)) _cancelacion = null;
            cancelacion.Dispose();
        }
    }

    private bool PuedeCancelarGeneracion() => Paso == PasoExportacion.Generando;

    /// <summary>Cancela la generación en curso (RF-28): no se guarda nada.</summary>
    [RelayCommand(CanExecute = nameof(PuedeCancelarGeneracion))]
    private void CancelarGeneracion()
    {
        try { _cancelacion?.Cancel(); }
        catch (ObjectDisposedException) { /* ya terminó: no hay nada que cancelar */ }
    }

    /// <summary>La pantalla se cierra (botón Atrás de Android): si estaba generando, se cancela sin guardar nada.</summary>
    public void AlSalir() => CancelarGeneracion();

    // ------------------------------------------------------------------ paso 4: resultado

    /// <summary>"Compartir": abre las opciones de Android para el archivo ya guardado (RF-20).</summary>
    [RelayCommand]
    private async Task CompartirAsync()
    {
        if (Resultado is null) return;
        try
        {
            await _entrega.CompartirAsync(Resultado.Archivo);
        }
        catch (Exception)
        {
            MensajeError = MensajeFallaCompartir;   // el archivo ya está guardado: no se pierde nada
        }
    }

    // ------------------------------------------------------------------ volver

    /// <summary>
    /// "Volver": del resumen a la lista y de la lista al inicio (RF-28, sin generar nada). Mientras se genera no hace nada: para
    /// salir está "Cancelar".
    /// </summary>
    [RelayCommand]
    private async Task VolverAsync()
    {
        switch (Paso)
        {
            case PasoExportacion.Generando:
                return;
            case PasoExportacion.Resumen:
                Reiniciar();
                await CargarAsync();   // la lista puede haber cambiado
                return;
            default:
                Reiniciar();
                await _navegacion.IrAInicioAsync();
                return;
        }
    }

    /// <summary>"Listo": termina y vuelve al inicio.</summary>
    [RelayCommand]
    private async Task ListoAsync()
    {
        Reiniciar();
        await _navegacion.IrAInicioAsync();
    }

    private void Reiniciar()
    {
        Resumen = null;
        Resultado = null;
        RodeoElegido = null;
        MensajeError = null;
        PuedeReintentar = false;
        _ultimoFormato = null;
        Paso = PasoExportacion.Lista;
    }
}
