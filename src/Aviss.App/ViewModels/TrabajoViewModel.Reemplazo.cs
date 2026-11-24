using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>
/// Pantalla de trabajo, spec 009: con una ficha abierta, el bastón (o la búsqueda a mano) lee OTRA vaca. En vez de reemplazar la ficha
/// en silencio, se abre el cuadro "Leyó otra vaca" con tres opciones: poner en espera la actual y abrir la nueva, cancelar la lectura
/// o saltar la actual y abrir la nueva. La vaca nueva no se marca hasta que el veterinario elige abrirla (opciones 1 y 3).
/// </summary>
public sealed partial class TrabajoViewModel
{
    /// <summary>Lo que quedó pendiente de decidir: el resultado de la lectura (sin marcar) y el código que se leyó.</summary>
    private sealed record ReemplazoPendiente(ResultadoLecturaDto Resultado, string Codigo, FichaAnimalDto? FichaNueva);

    private ReemplazoPendiente? _reemplazoPendiente;

    public bool HayConfirmarReemplazo => Cuadro == CuadroTrabajo.ConfirmarReemplazo;

    // ---- Lo que muestra el aviso (RF-2)

    /// <summary>La vaca de la ficha abierta, con su CVET si lo tiene: "1001 · CVET PRE".</summary>
    public string ReemplazoCaravanaActual => Ficha is { } f ? ConCvet(f.Caravana, f.Cvet) : "";

    /// <summary>La vaca que acaba de leer el bastón, con su CVET si lo tiene.</summary>
    public string ReemplazoCaravanaNueva => _reemplazoPendiente is { } p
        ? ConCvet(p.Resultado.Caravana ?? p.Codigo, p.FichaNueva?.Cvet ?? p.Resultado.Ficha?.Cvet ?? "")
        : "";

    /// <summary>La ficha actual tiene un evento o una observación elegidos sin guardar: las opciones 1 y 3 los descartan.</summary>
    public bool HayReemplazoConEventoSinGuardar => HayConfirmarReemplazo && (EventoElegido is not null || HayObservacionPendiente);

    public string TextoReemplazoAdvertencia => HayReemplazoConEventoSinGuardar
        ? "El evento o la observación que cargaste en la vaca actual se perderían (no se guardó ningún tacto)."
        : "";

    private static string ConCvet(string caravana, string? cvet) => string.IsNullOrWhiteSpace(cvet) ? caravana : $"{caravana} · CVET {cvet}";

    // ---- Abrir el cuadro

    private async Task AbrirConfirmarReemplazoAsync(ResultadoLecturaDto resultado, Lectura lectura, FichaAnimalDto? fichaNueva)
    {
        _reemplazoPendiente = new ReemplazoPendiente(resultado, lectura.Codigo, fichaNueva);
        MensajeCuadro = null;
        _avisos.Repetido();   // suena o vibra una vez para que quien mira a la vaca note que la pantalla pide algo (RF-2)
        OnPropertyChanged(nameof(ReemplazoCaravanaActual));
        OnPropertyChanged(nameof(ReemplazoCaravanaNueva));
        OnPropertyChanged(nameof(HayReemplazoConEventoSinGuardar));
        OnPropertyChanged(nameof(TextoReemplazoAdvertencia));
        await CambiarCuadroAsync(CuadroTrabajo.ConfirmarReemplazo);
        // El cuadro recién abierto ya existe: las propiedades que dependen de él se vuelven a leer.
        OnPropertyChanged(nameof(HayReemplazoConEventoSinGuardar));
        OnPropertyChanged(nameof(TextoReemplazoAdvertencia));
    }

    // ---- Las tres opciones

    /// <summary>Opción 1: pone en espera la ficha actual (queda en la botonera, sin tacto) y abre la nueva, que recién ahí se marca (RF-3).</summary>
    [RelayCommand]
    private Task PonerEnEsperaYAbrirNuevaAsync() => ResolverReemplazoAsync(ponerEnEspera: true);

    /// <summary>Opción 3: cierra la ficha actual como "Saltar" (sin guardar ningún tacto) y abre la nueva, que recién ahí se marca (RF-5).</summary>
    [RelayCommand]
    private Task SaltarYAbrirNuevaAsync() => ResolverReemplazoAsync(ponerEnEspera: false);

    /// <summary>
    /// Opción 2 (y el botón Atrás de Android): descarta la lectura como si no se hubiera leído. La vaca nueva no se marca, no suma y no deja
    /// ningún registro; la ficha actual sigue abierta con su evento y su observación (RF-4). Al cerrar el cuadro se procesa la última lectura
    /// que llegó mientras estaba abierto (RF-9).
    /// </summary>
    [RelayCommand]
    private async Task CancelarLecturaAsync()
    {
        if (Cuadro != CuadroTrabajo.ConfirmarReemplazo) return;
        _reemplazoPendiente = null;
        MensajeCuadro = null;
        MensajeError = null;
        MensajeInfo = "Lectura cancelada: no se marcó";
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // Las opciones 1 y 3 hacen lo mismo en tres pasos: (1) cerrar la actual, (2) marcar y abrir la nueva, (3) cerrar el cuadro al final.
    private async Task ResolverReemplazoAsync(bool ponerEnEspera)
    {
        if (Cuadro != CuadroTrabajo.ConfirmarReemplazo || _reemplazoPendiente is not { } pendiente || _sesion.Rodeo is not { } rodeo) return;

        try
        {
            MensajeCuadro = null;

            // 1) Cerrar la actual. Si falla, el cuadro queda abierto con el motivo y no se cambia nada más (RF-10).
            string? textoActual = null;
            var cambioLaEspera = false;
            if (Ficha is { } actual)
            {
                if (ponerEnEspera && !actual.EnEspera)
                {
                    var puesta = await EnCampoAsync(() => _campo.PonerEnEsperaAsync(new PonerEnEsperaCommand(actual.AnimalId)));
                    // Una ficha vieja de una vaca que ya estaba en espera se toma como éxito: lo que se quería ya está hecho.
                    if (!puesta.EsCorrecto && puesta.Codigo != CodigoError.VacaYaEnEspera)
                    {
                        MensajeCuadro = puesta.Mensaje;
                        return;
                    }
                    cambioLaEspera = true;
                    textoActual = $"{actual.Caravana}: en espera";
                }
                else if (!ponerEnEspera && actual.EnEspera)
                {
                    // La actual viene de la espera: "Saltar" la deja salteada (spec 004, RF-3). Con la opción 1 queda pendiente (RF-7).
                    var salteada = await EnCampoAsync(() => _campo.SaltearEsperaAsync(new SaltearEsperaCommand(actual.AnimalId)));
                    if (!salteada.EsCorrecto)
                    {
                        MensajeCuadro = salteada.Mensaje;
                        return;
                    }
                    cambioLaEspera = true;
                    textoActual = $"{actual.Caravana}: espera salteada";
                }
            }

            // La actual ya salió de la pantalla (descarta también el evento y la observación elegidos).
            CerrarFichaInterno();
            _reemplazoPendiente = null;
            if (textoActual is not null) UltimoGuardado = textoActual;
            if (cambioLaEspera) await CargarEsperasAsync();

            // 2) Abrir la nueva. Si la lectura era "RequiereDecision", recién ahora se pide la marca de verdad (sin la bandera).
            //    Si era una vaca en espera, ya está marcada: solo se abre su ficha.
            if (pendiente.Resultado.Tipo == TipoResultadoLectura.RequiereDecision)
            {
                var marca = await EnCampoAsync(() => _campo.ProcesarLecturaAsync(new ProcesarLecturaCommand(rodeo.RodeoId, pendiente.Codigo)));
                if (!marca.EsCorrecto)
                {
                    // Se rechazó la marca (por ejemplo, reloj mal puesto): la actual conserva lo hecho y la nueva queda sin marcar (RF-10).
                    MensajeError = marca.Mensaje;
                }
                else
                {
                    await AplicarLecturaAsync(marca.Valor, new Lectura(pendiente.Codigo, false));
                }
            }
            else
            {
                await AplicarLecturaAsync(pendiente.Resultado with { Ficha = pendiente.FichaNueva ?? pendiente.Resultado.Ficha },
                    new Lectura(pendiente.Codigo, false));
            }
        }
        catch (Exception)
        {
            MensajeCuadro = MensajeFalla;
            // Si ya se cerró la actual, no queda nada que decidir: se cierra el cuadro y el error se ve en la pantalla.
            if (_reemplazoPendiente is null && Cuadro == CuadroTrabajo.ConfirmarReemplazo)
            {
                MensajeCuadro = null;
                MensajeError = MensajeFalla;
            }
        }

        // 3) Recién ahora se cierra el cuadro: destraba la cola y procesa la lectura guardada con el estado nuevo (RF-9). Si el resultado
        //    de la marca abrió su propio cuadro (un aviso de repetido, por ejemplo), no se lo pisa.
        if (Cuadro == CuadroTrabajo.ConfirmarReemplazo && _reemplazoPendiente is null)
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }
}
