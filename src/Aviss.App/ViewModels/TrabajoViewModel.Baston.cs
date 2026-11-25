using Aviss.Application.Puertos;
using Aviss.Rfid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.ViewModels;

// Spec 008: el estado del bastón real en la pantalla de trabajo. Solo muestra y comanda: la conexión, los reintentos y el filtro de
// repetidas son del lector (Aviss.Rfid). Perder el bastón NUNCA toca la ficha, el evento ni la observación (RF-15): este archivo
// no usa nada de eso a propósito.
public sealed partial class TrabajoViewModel
{
    private readonly IPreferenciasBaston? _preferenciasBaston;   // nulo en tests viejos y si no hay bastón (el simulador)
    private readonly IBluetoothBaston? _bluetooth;
    private bool _suscriptoBaston;

    /// <summary>Lo que se muestra hoy del bastón; se rearma entero cada vez que el lector avisa un cambio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoBaston), nameof(TextoBaston), nameof(DetalleBaston), nameof(TextoCartelBaston), nameof(TextoAccionBaston),
        nameof(ColorFondoBaston), nameof(HayAvisoBaston), nameof(HayAccionBaston))]
    private PresentacionBaston _baston = PresentacionBaston.Crear(EstadoConexionRfid.Desconectado, MotivoSinBaston.Ninguno);

    public EstadoBaston EstadoBaston => Baston.Estado;
    /// <summary>El chip de la barra, siempre visible: "Conectado", "Buscando el bastón" o "Sin bastón".</summary>
    public string TextoBaston => Baston.Texto;
    /// <summary>El motivo del cartel: "Elegí el bastón", "Falta el permiso de Bluetooth", etc.</summary>
    public string DetalleBaston => Baston.Detalle;
    /// <summary>La línea grande del cartel: el motivo si lo hay ("Elegí el bastón") y si no el estado ("Buscando el bastón").</summary>
    public string TextoCartelBaston => string.IsNullOrEmpty(Baston.Detalle) ? Baston.Texto : Baston.Detalle;
    public string TextoAccionBaston => Baston.TextoAccion;
    /// <summary>Color de fondo (hex) del chip y del cartel; el texto va en blanco.</summary>
    public string ColorFondoBaston => Baston.ColorFondo;
    /// <summary>El cartel grande se ve mientras no esté conectado.</summary>
    public bool HayAvisoBaston => Baston.HayAviso;
    public bool HayAccionBaston => Baston.Accion != AccionBaston.Ninguna;

    /// <summary>Para la fila "Bastón: ..." de la configuración (RF-3).</summary>
    public string NombreBaston => _preferenciasBaston?.Obtener()?.Nombre ?? "Sin elegir";

    private void SuscribirBaston()
    {
        if (!_suscriptoBaston)
        {
            _lector.EstadoCambiado += AlCambiarElLector;
            _suscriptoBaston = true;
        }
        ActualizarBaston();   // al volver de elegir otro bastón también cambia el nombre
    }

    private void DesuscribirBaston()
    {
        if (!_suscriptoBaston) return;
        _lector.EstadoCambiado -= AlCambiarElLector;
        _suscriptoBaston = false;
    }

    // Puede llegar desde cualquier hilo: solo lee el estado y lo publica (los bindings de MAUI saltan al hilo principal).
    private void AlCambiarElLector() => ActualizarBaston();

    private void ActualizarBaston()
    {
        Baston = PresentacionBaston.Crear(_lector.Estado, _lector.Motivo);
        OnPropertyChanged(nameof(NombreBaston));
    }

    /// <summary>
    /// El botón grande del cartel (RF-12): hace lo que dice su texto según el motivo. Reconectar salta la espera; el resto lleva
    /// a elegir, pide el permiso o abre los ajustes.
    /// </summary>
    [RelayCommand]
    private async Task ReconectarBastonAsync()
    {
        try
        {
            switch (Baston.Accion)
            {
                case AccionBaston.Reconectar:
                    await _lector.ReconectarAhoraAsync();
                    break;
                case AccionBaston.ElegirBaston:
                    await _navegacion.IrAElegirBastonAsync();
                    break;
                case AccionBaston.PedirPermiso when _bluetooth is not null:
                    // Si Android ya no muestra el diálogo (denegado dos veces), la respuesta vuelve denegada: se abren los ajustes.
                    if (await _bluetooth.PedirPermisosAsync()) await _lector.ReconectarAhoraAsync();
                    else _bluetooth.AbrirAjustesDeLaApp();
                    break;
                case AccionBaston.AbrirAjustesBluetooth when _bluetooth is not null:
                    _bluetooth.AbrirAjustesBluetooth();
                    break;
            }
        }
        catch (Exception)
        {
            // Un toque que falla no debe tirar la pantalla; el cartel sigue ahí para volver a tocar.
        }
    }

    /// <summary>"Cambiar bastón" (cartel y engranaje, RF-3).</summary>
    [RelayCommand]
    private Task CambiarBastonAsync() => _navegacion.IrAElegirBastonAsync();

    /// <summary>
    /// La app pasó a segundo plano o se apagó la pantalla: se corta la conexión y no se lee en segundo plano (RF-16). El bucle de
    /// lecturas sigue vivo; lo recibido y no procesado se descarta (lo hace el lector).
    /// </summary>
    public async Task PausarLectorAsync()
    {
        try { await _lector.DesconectarAsync(); }
        catch (Exception) { /* cortar nunca debe fallar la pantalla */ }
    }

    /// <summary>
    /// La app volvió: conecta de inmediato. Si la pantalla de trabajo ya no está abierta (se fue a "Últimos movimientos"), no hace
    /// nada: volver a entrar arma todo de nuevo.
    /// </summary>
    public async Task ReanudarLectorAsync()
    {
        if (_ctsLector is null) return;
        try { await _lector.ConectarAsync(); }
        catch (Exception) { MensajeError = MensajeLectorNoConecta; }
    }
}
