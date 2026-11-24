using System.Globalization;
using Aviss.Application.Campo;
using Aviss.Application.Comandos;
using Aviss.Application.Comun;
using Aviss.Application.Puertos;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Pantalla de trabajo: avisos de "Animal repetido" y "Ya pasó", caravana no encontrada, y el deshacer (aviso de 5 segundos).</summary>
public sealed partial class TrabajoViewModel
{
    // ---- Aviso "Animal repetido" / "Ya pasó" (RF-22)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoTitulo), nameof(AvisoCaravana), nameof(AvisoDetalle), nameof(PuedeCorregir), nameof(TextoCorregir))]
    private ResultadoLecturaDto? _aviso;

    public string AvisoTitulo => Aviso?.Aviso switch
    {
        TipoAviso.Repetido => "Animal repetido",
        TipoAviso.YaPasoSinManga => "Ya pasó, sin manga registrada",
        TipoAviso.YaPasoOtraManga => Aviso.UltimoRegistro?.NumeroManga is { } m ? $"Ya pasó en la manga {m}" : "Ya pasó",
        _ => ""
    };

    /// <summary>La caravana leída, para mostrarla bien grande (quien maneja el bastón nota si leyó la de la vecina).</summary>
    public string AvisoCaravana => Aviso?.Caravana ?? Aviso?.CodigoLeido ?? "";

    /// <summary>Último registro de la vaca: qué y a qué hora (`<evento> · <hora>`; sin quién, spec 006 RF-5).</summary>
    public string AvisoDetalle => Aviso?.UltimoRegistro is { } u
        ? $"{DescribirRegistro(u)} · {u.FechaHoraUtc.ToLocalTime():HH:mm}"
        : "";

    /// <summary>"Corregir" solo se ofrece cuando lo último no es la marca de pasada (RF-22).</summary>
    public bool PuedeCorregir => Aviso is { PuedeCorregir: true, UltimoRegistro: not null };

    public string TextoCorregir => Aviso?.UltimoRegistro is { } u ? $"¿Deshacer lo último de esta vaca: {DescribirRegistro(u)}?" : "";

    private static string DescribirRegistro(UltimoRegistroDto u) => u.Tipo switch
    {
        TipoRegistroCampo.Tacto => string.IsNullOrEmpty(u.EstadoNombre) ? "Tacto" : $"Tacto {u.EstadoNombre}",
        TipoRegistroCampo.Rechazo => (string.IsNullOrWhiteSpace(u.EstadoNombre) ? EventosCampo.NombreRechazo : u.EstadoNombre) + (string.IsNullOrWhiteSpace(u.Observaciones) ? "" : $" · {u.Observaciones}"),
        _ => "Marca de pasada"
    };


    // ================================================================== aviso "Animal repetido" / "Ya pasó" (RF-22)

    /// <summary>"Ver ficha": abre la ficha sin marcar de nuevo.</summary>
    [RelayCommand]
    private async Task VerFichaAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso || Aviso?.AnimalId is not { } animalId) return;
        try
        {
            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(animalId));
            if (!ficha.EsCorrecto) { MensajeError = ficha.Mensaje; return; }
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"Seguir": cierra el aviso y espera la siguiente lectura.</summary>
    [RelayCommand]
    private async Task SeguirAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso) return;
        CerrarFichaInterno();
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>"Corregir": pide confirmación antes de deshacer lo último de la vaca.</summary>
    [RelayCommand]
    private async Task CorregirAsync()
    {
        if (Cuadro != CuadroTrabajo.Aviso || !PuedeCorregir) return;
        await CambiarCuadroAsync(CuadroTrabajo.ConfirmarCorregir);
    }

    [RelayCommand]
    private async Task ConfirmarCorregirAsync()
    {
        if (Cuadro != CuadroTrabajo.ConfirmarCorregir || Aviso is not { UltimoRegistro: { } ultimo, AnimalId: { } animalId }) return;
        try
        {
            var tipo = ultimo.Tipo switch
            {
                TipoRegistroCampo.Tacto => TipoDeshacer.Tacto,
                TipoRegistroCampo.Rechazo => TipoDeshacer.Rechazo,
                _ => TipoDeshacer.Marca
            };
            var resultado = await DeshacerAsync(tipo, ultimo.RegistroId);
            if (!resultado.EsCorrecto)
            {
                // No se pudo: motivo y el aviso queda abierto (RF-22).
                await CambiarCuadroAsync(CuadroTrabajo.Aviso);
                MensajeError = resultado.Mensaje;
                return;
            }

            var ficha = await EnCampoAsync(() => _campo.ObtenerFichaAsync(animalId));
            await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
            if (ficha.EsCorrecto) MostrarFicha(ficha.Valor);
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    /// <summary>"No" en la confirmación de "Corregir": vuelve al aviso.</summary>
    [RelayCommand]
    private Task CancelarCorregirAsync() => Cuadro == CuadroTrabajo.ConfirmarCorregir
        ? CambiarCuadroAsync(CuadroTrabajo.Aviso)
        : Task.CompletedTask;

    // ================================================================== caravana no encontrada (RF-24)

    /// <summary>"Buscar a mano": cierra el cuadro y deja la búsqueda lista. No se guardó nada.</summary>
    [RelayCommand]
    private async Task BuscarAManoAsync()
    {
        if (Cuadro != CuadroTrabajo.NoEncontrada) return;
        TextoBusqueda = "";
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    /// <summary>Cierra "Caravana no encontrada" sin hacer nada.</summary>
    [RelayCommand]
    private async Task CerrarNoEncontradaAsync()
    {
        if (Cuadro != CuadroTrabajo.NoEncontrada) return;
        await CambiarCuadroAsync(CuadroTrabajo.Ninguno);
    }

    // ================================================================== deshacer (RF-26, RF-27)

    // Deshace y, si salió bien, actualiza lo que se ve: cierra la ficha de una vaca desmarcada o anulada, baja el
    // contador y recalcula. Devuelve el resultado para que quien llama decida qué mensaje mostrar si fue rechazado.
    private async Task<Resultado<ResultadoDeshacerDto>> DeshacerAsync(TipoDeshacer tipo, Guid id)
    {

        var resultado = await EnCampoAsync(() => _campo.DeshacerAsync(new DeshacerCommand(tipo, id)));
        if (!resultado.EsCorrecto) return resultado;

        var deshecho = resultado.Valor;
        // Desmarcar una vaca cierra su ficha (RF-26).
        if (tipo is TipoDeshacer.Marca && deshecho.AnimalId is { } animalId && Ficha?.AnimalId == animalId)
            CerrarFichaInterno();
        if (deshecho.ContadorManga is { } contador) Contador = contador;
        UltimoGuardado = tipo switch
        {
            TipoDeshacer.Marca => "Se desmarcó la vaca.",
            TipoDeshacer.Cierre => "Se reabrió la manga.",
            TipoDeshacer.Rechazo => "Se deshizo el rechazo.",
            _ => "Se deshizo el tacto."
        };
        await RecalcularAsync();
        return resultado;
    }

    /// <summary>El botón "Deshacer" del aviso de 5 segundos.</summary>
    [RelayCommand]
    private async Task DeshacerAvisoAsync()
    {
        if (AvisoDeshacer is not { } aviso) return;
        QuitarAvisoDeshacer();
        try
        {
            var resultado = await DeshacerAsync(aviso.Tipo, aviso.Id);
            // Si ya no se puede (por ejemplo ya no es lo último de la vaca): se rechaza con el motivo (RF-27).
            if (!resultado.EsCorrecto) MensajeError = resultado.Mensaje;
        }
        catch (Exception) { MensajeError = MensajeFalla; }
    }

    // Publica un aviso nuevo (reemplaza al anterior) y lo saca solo a los 5 segundos. Sobrevive a las lecturas y a los
    // avisos de repetido: solo lo cambia otro aviso de deshacer, o su propio tiempo.
    private void PublicarAvisoDeshacer(AvisoDeshacerDto aviso)
    {
        _ctsAviso?.Cancel();
        var cts = _ctsAviso = new CancellationTokenSource();
        AvisoDeshacer = aviso;
        _ = ExpirarAvisoAsync(aviso, cts);
    }

    private async Task ExpirarAvisoAsync(AvisoDeshacerDto aviso, CancellationTokenSource cts)
    {
        try
        {
            await _temporizador.EsperarAsync(DuracionAvisoDeshacer, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; }

        if (!cts.IsCancellationRequested && ReferenceEquals(AvisoDeshacer, aviso)) AvisoDeshacer = null;
    }

    private void QuitarAvisoDeshacer()
    {
        _ctsAviso?.Cancel();
        _ctsAviso = null;
        AvisoDeshacer = null;
    }

}
