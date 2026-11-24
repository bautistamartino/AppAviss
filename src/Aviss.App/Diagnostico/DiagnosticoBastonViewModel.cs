using System.Collections.ObjectModel;
using Aviss.Rfid.Diagnostico;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aviss.App.Diagnostico;

/// <summary>
/// Pantalla "Diagnóstico del bastón" (spike previo a la spec 008, solo compilación DEBUG). Es una herramienta para averiguar en la
/// tablet qué transporte y qué trama usa el XRS2; no es el lector final. Toda la lógica de bytes, líneas, candidatos y registro vive
/// en <see cref="SesionDiagnostico"/> (probada sin Android); acá solo se conecta con la pantalla y con los transportes.
/// No se muestran ni se registran datos de animales ni de tambos: solo lo que manda el bastón y el nombre y dirección del dispositivo.
/// </summary>
public sealed partial class DiagnosticoBastonViewModel : ObservableObject
{
    private const int MaximoFilasVisor = 300;   // el archivo guarda todo; el visor solo las últimas

    private readonly IBluetoothDiagnostico _bluetooth;
    private readonly IEntregaRegistro _entrega;

    private RegistroArchivo? _registro;
    private SesionDiagnostico? _sesion;
    private ITransporteDiagnostico? _transporte;
    private string _fuenteActual = "";

    public DiagnosticoBastonViewModel(IBluetoothDiagnostico bluetooth, IEntregaRegistro entrega)
    {
        _bluetooth = bluetooth;
        _entrega = entrega;
    }

    public ObservableCollection<DispositivoBluetooth> Dispositivos { get; } = [];
    public ObservableCollection<EntradaVisor> Entradas { get; } = [];

    [ObservableProperty] private DispositivoBluetooth? _seleccionado;
    [ObservableProperty] private string _estado = "Desconectado";
    [ObservableProperty] private string _detalleEstado = "";
    [ObservableProperty] private bool _conectado;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HayMensaje))] private string _mensaje = "";
    [ObservableProperty] private string _contadores = "Paquetes: 0   Líneas: 0";
    [ObservableProperty] private string _rutaRegistro = "";
    [ObservableProperty] private string _caravanaImpresa = "";
    [ObservableProperty] private string _textoTeclado = "";
    [ObservableProperty] private bool _numerosCortos;

    public bool HaySeleccion => Seleccionado is not null;
    public bool HayMensaje => Mensaje.Length > 0;

    partial void OnSeleccionadoChanged(DispositivoBluetooth? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        ConectarSppCommand.NotifyCanExecuteChanged();
        ConectarBleCommand.NotifyCanExecuteChanged();
    }

    partial void OnNumerosCortosChanged(bool value)
    {
        // Opción del parser tentativo: con "números cortos" también se marcan secuencias de 4 a 14 dígitos.
        if (_sesion is not null) _sesion.Opciones = new OpcionesParserCaravana { MinimoDigitosCorto = value ? 4 : 0 };
    }

    /// <summary>Tercera ruta: lo que entra como teclado. Cada línea completa (terminada en Enter) se registra.</summary>
    partial void OnTextoTecladoChanged(string value)
    {
        if (_sesion is null || string.IsNullOrEmpty(value)) return;
        var (lineas, resto) = AcumuladorTeclado.Procesar(value);
        if (lineas.Count == 0) return;
        foreach (var l in lineas) _sesion.RecibirLineaTeclado(l);
        TextoTeclado = resto;
    }

    /// <summary>Abre la sesión (un archivo nuevo). Se llama al mostrar la pantalla; si ya hay una, no hace nada.</summary>
    public void Iniciar()
    {
        if (_sesion is not null) return;
        // Carpeta privada de la app: nadie más la ve. Para sacar el archivo están "Compartir" y "Guardar en Descargas".
        _registro = new RegistroArchivo(Path.Combine(FileSystem.AppDataDirectory, "diagnostico"));
        RutaRegistro = _registro.Ruta;
        _sesion = new SesionDiagnostico(_registro, () => DateTime.UtcNow, new OpcionesParserCaravana());
        _sesion.EntradaNueva += AlLlegarEntrada;
        _sesion.Nota($"inicio de sesion | tablet={DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model} | android={DeviceInfo.Current.VersionString}");
    }

    /// <summary>Al salir de la pantalla: corta la conexión y cierra el archivo.</summary>
    public async Task CerrarAsync()
    {
        await DesconectarAsync();
        _registro?.Dispose();
        _registro = null;
        _sesion = null;
    }

    // ---------- Permisos y dispositivos ----------

    [RelayCommand]
    private async Task RefrescarAsync()
    {
        Mensaje = "";
        Ocupado = true;
        try
        {
            var permisos = await _bluetooth.PedirPermisosAsync();
            switch (permisos)
            {
                case EstadoPermisosBluetooth.BluetoothNoDisponible:
                    Mensaje = "Esta tablet no tiene Bluetooth.";
                    return;
                case EstadoPermisosBluetooth.Denegados:
                    Mensaje = "Sin el permiso de Bluetooth no se pueden ver los dispositivos. Tocá \"Refrescar\" y aceptá, o activalo en Ajustes de Android > Aplicaciones > Aviss > Permisos.";
                    return;
            }

            var lista = _bluetooth.ListarEmparejados();
            Dispositivos.Clear();
            foreach (var d in lista) Dispositivos.Add(d);
            Mensaje = lista.Count == 0
                ? "No hay dispositivos emparejados (o el Bluetooth está apagado). Tocá \"Ajustes de Bluetooth\", prendé el bastón y emparejalo."
                : $"{lista.Count} dispositivo(s) emparejado(s). Elegí el bastón.";
            _sesion?.Nota($"dispositivos emparejados: {lista.Count}");
        }
        catch (Exception ex)
        {
            Mensaje = "No se pudo leer la lista de Bluetooth: " + ex.GetType().Name;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void AbrirAjustesBluetooth()
    {
        try { _bluetooth.AbrirAjustesBluetooth(); }
        catch (Exception) { Mensaje = "No se pudieron abrir los ajustes de Bluetooth. Abrilos a mano desde Ajustes de Android."; }
    }

    // ---------- Conexión ----------

    private bool HayDispositivo() => Seleccionado is not null;

    [RelayCommand(CanExecute = nameof(HayDispositivo))]
    private Task ConectarSppAsync() => ConectarAsync(_bluetooth.CrearTransporteSpp());

    [RelayCommand(CanExecute = nameof(HayDispositivo))]
    private Task ConectarBleAsync() => ConectarAsync(_bluetooth.CrearTransporteBle());

    private async Task ConectarAsync(ITransporteDiagnostico transporte)
    {
        if (Seleccionado is null || _sesion is null) return;
        await DesconectarAsync();

        _transporte = transporte;
        _fuenteActual = transporte.Nombre;
        transporte.EstadoCambiado += AlCambiarEstado;
        transporte.Informacion += AlLlegarInformacion;
        transporte.DatosRecibidos += AlLlegarDatos;

        // En el archivo queda el nombre y la dirección del dispositivo (no son datos del tambo).
        _sesion.Nota($"conectar {transporte.Nombre} | dispositivo={Seleccionado.Titulo} | direccion={Seleccionado.Direccion} | tipo={Seleccionado.TipoTexto} | uuids={Seleccionado.UuidsTexto}");
        await transporte.ConectarAsync(Seleccionado);
    }

    [RelayCommand]
    private async Task DesconectarAsync()
    {
        var t = _transporte;
        _transporte = null;
        if (t is null) return;
        t.EstadoCambiado -= AlCambiarEstado;
        t.Informacion -= AlLlegarInformacion;
        t.DatosRecibidos -= AlLlegarDatos;
        await t.DesconectarAsync();
        Estado = "Desconectado";
        DetalleEstado = "";
        Conectado = false;
        _sesion?.Nota($"estado {t.Nombre}: desconectado");
    }

    // Los eventos de los transportes llegan desde hilos de fondo: se anotan ahí y se pasa al hilo principal para tocar la pantalla.
    private void AlCambiarEstado(CambioEstadoTransporte cambio)
    {
        _sesion?.Nota($"estado {_fuenteActual}: {cambio.Estado}{(cambio.Detalle is null ? "" : " | " + cambio.Detalle)}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Estado = cambio.Estado switch
            {
                EstadoTransporte.Conectando => "Conectando...",
                EstadoTransporte.Conectado => "Conectado",
                EstadoTransporte.Error => "Error",
                _ => "Desconectado",
            };
            DetalleEstado = cambio.Detalle ?? "";
            Conectado = cambio.Estado == EstadoTransporte.Conectado;
        });
    }

    private void AlLlegarInformacion(string texto)
    {
        _sesion?.Nota(texto);
        MainThread.BeginInvokeOnMainThread(() => DetalleEstado = texto);
    }

    private void AlLlegarDatos(string origen, byte[] datos)
        => _sesion?.RecibirBytes(origen == _fuenteActual ? origen : $"{_fuenteActual}:{origen}", datos);

    private void AlLlegarEntrada(EntradaVisor entrada)
    {
        var sesion = _sesion;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Entradas.Insert(0, entrada);   // la más nueva arriba
            while (Entradas.Count > MaximoFilasVisor) Entradas.RemoveAt(Entradas.Count - 1);
            if (sesion is not null) Contadores = $"Paquetes: {sesion.Paquetes}   Líneas: {sesion.Lineas}";
        });
    }

    // ---------- Visor ----------

    /// <summary>Si el bastón no manda CR ni LF, lo recibido queda "pendiente": este botón lo cierra como una línea.</summary>
    [RelayCommand]
    private void CerrarLineaPendiente()
    {
        if (_sesion is null) return;
        var e = _sesion.CerrarPendientes(_fuenteActual);
        Mensaje = e is null ? "No hay nada pendiente." : "Se cerró la línea pendiente.";
    }

    [RelayCommand]
    private void Limpiar()
    {
        _sesion?.Limpiar();
        Entradas.Clear();
        Contadores = "Paquetes: 0   Líneas: 0";
    }

    [RelayCommand]
    private void Anotar()
    {
        if (_sesion is null) return;
        var texto = CaravanaImpresa.Trim();
        if (texto.Length == 0) { Mensaje = "Escribí el número impreso en la caravana."; return; }
        _sesion.Anotar(texto);
        Mensaje = $"Anotado: {texto} (queda en el archivo, junto a la última lectura).";
        CaravanaImpresa = "";
    }

    // ---------- Sacar el archivo ----------

    [RelayCommand]
    private async Task CompartirAsync()
    {
        if (_registro is null) return;
        try
        {
            await _entrega.CompartirAsync(_registro.Ruta);
            Mensaje = "";
        }
        catch (Exception)
        {
            Mensaje = "No se pudo abrir el menú de compartir. Probá \"Guardar en Descargas\".";
        }
    }

    [RelayCommand]
    private async Task GuardarEnDescargasAsync()
    {
        if (_registro is null) return;
        var nombre = await _entrega.GuardarEnDescargasAsync(_registro.Ruta);
        Mensaje = nombre is null
            ? "No se pudo guardar en Descargas."
            : $"Guardado en Descargas como {nombre}.";
    }
}
