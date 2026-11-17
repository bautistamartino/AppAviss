using System.Collections.ObjectModel;
using Aviss.Application.Campo;
using Aviss.Application.Servicios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

/// <summary>Una fila de "Últimos movimientos" lista para mostrar (RF-28).</summary>
public sealed class MovimientoItem
{
    public MovimientoItem(MovimientoDto dto) => Dto = dto;

    public MovimientoDto Dto { get; }

    public bool EsCierre => Dto.Tipo == TipoMovimiento.Cierre;
    public bool Anulado => Dto.Anulado;
    public bool PuedeDeshacer => Dto.PuedeDeshacer;

    /// <summary>Qué se hizo: "Marca", "Tacto", "Alta en campo" o "Cierre de manga".</summary>
    public string Tipo => Dto.Tipo switch
    {
        TipoMovimiento.Tacto => "Tacto",
        TipoMovimiento.Marca => "Marca",
        TipoMovimiento.Alta => "Alta en campo",
        _ => "Cierre de manga"
    };

    /// <summary>La caravana (en un cierre, la manga). Son datos de pantalla: nunca se escriben en logs.</summary>
    public string Titulo => EsCierre ? $"Manga {Dto.NumeroManga}" : Dto.Caravana ?? "";

    /// <summary>Hora local del movimiento (RF-28).</summary>
    public string Hora => Dto.FechaHoraUtc.ToLocalTime().ToString("HH:mm");

    public string Veterinario => Dto.Veterinario;

    /// <summary>Estado del tacto o tipo de cierre, y la manga.</summary>
    public string Detalle
    {
        get
        {
            var partes = new List<string>();
            if (!string.IsNullOrEmpty(Dto.Detalle)) partes.Add(Dto.Detalle);
            if (!EsCierre && Dto.NumeroManga is { } manga) partes.Add($"Manga {manga}");
            return string.Join(" · ", partes);
        }
    }

    /// <summary>
    /// En un cierre, las dos cifras cuando difieren: lo que se guardó al cerrar y el contador de hoy
    /// ("Al cerrar: 14 · Hoy: 13", RF-8). Vacío si no hace falta.
    /// </summary>
    public string TextoCierre =>
        EsCierre && Dto.CantidadAviss is { } alCerrar && Dto.ContadorActual is { } hoy && alCerrar != hoy
            ? $"Al cerrar: {alCerrar} · Hoy: {hoy}"
            : "";

    public bool HayTextoCierre => TextoCierre.Length > 0;
}

/// <summary>Lógica de "Últimos movimientos" del rodeo en trabajo, con deshacer (spec 003, RF-28, RF-29).</summary>
public sealed partial class MovimientosViewModel : ObservableObject
{
    public const string MensajeFalla = "Algo falló y no se cambió nada. Probá de nuevo.";

    private readonly ICampoService _campo;
    private readonly SesionTrabajo _sesion;

    public MovimientosViewModel(ICampoService campo, SesionTrabajo sesion) => (_campo, _sesion) = (campo, sesion);

    public ObservableCollection<MovimientoItem> Movimientos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeError))]
    private string? _mensajeError;

    /// <summary>"Se deshizo" y similares (también el aviso de que una caravana quedó libre).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeInfo))]
    private string? _mensajeInfo;

    [ObservableProperty]
    private bool _estaOcupado;

    public bool HayMensajeError => !string.IsNullOrEmpty(MensajeError);
    public bool HayMensajeInfo => !string.IsNullOrEmpty(MensajeInfo);
    public bool HayMovimientos => Movimientos.Count > 0;

    /// <summary>Los 50 movimientos más recientes del rodeo en trabajo.</summary>
    [RelayCommand]
    public async Task CargarAsync()
    {
        if (_sesion.Rodeo is not { } rodeo) return;
        try
        {
            var lista = await _campo.ListarMovimientosAsync(rodeo.RodeoId);
            Movimientos.Clear();
            foreach (var m in lista) Movimientos.Add(new MovimientoItem(m));
            OnPropertyChanged(nameof(HayMovimientos));
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
    }

    /// <summary>
    /// Deshace un movimiento. Si el servicio lo rechaza (ya no es el último, otro día...) muestra el motivo y no cambia
    /// nada; si lo acepta, vuelve a leer la lista.
    /// </summary>
    [RelayCommand]
    private async Task DeshacerAsync(MovimientoItem? item)
    {
        if (item is null || !item.PuedeDeshacer || EstaOcupado || _sesion.Veterinario is not { } vet) return;

        EstaOcupado = true;
        MensajeError = null;
        MensajeInfo = null;
        try
        {
            var tipo = item.Dto.Tipo switch
            {
                TipoMovimiento.Tacto => TipoDeshacer.Tacto,
                TipoMovimiento.Marca => TipoDeshacer.Marca,
                TipoMovimiento.Alta => TipoDeshacer.Alta,
                _ => TipoDeshacer.Cierre
            };
            // En un alta, RegistroId ya es el id del animal (lo que pide DeshacerAsync).
            var resultado = await _campo.DeshacerAsync(new DeshacerCommand(tipo, item.Dto.RegistroId, vet.Id));
            if (!resultado.EsCorrecto)
            {
                MensajeError = resultado.Mensaje;
                return;
            }

            MensajeInfo = tipo == TipoDeshacer.Alta ? "Se anuló el alta: la caravana quedó libre." : "Se deshizo.";
            await CargarAsync();
        }
        catch (Exception)
        {
            MensajeError = MensajeFalla;
        }
        finally
        {
            EstaOcupado = false;
        }
    }
}
