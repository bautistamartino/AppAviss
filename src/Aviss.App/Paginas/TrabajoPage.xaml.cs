using Aviss.App.ViewModels;
using Aviss.Application.Campo;

namespace Aviss.App.Paginas;

/// <summary>
/// Pantalla de trabajo (spec 003). Acá no hay lógica de negocio: solo el ciclo de vida de la página (arrancar y cortar el
/// lector, recalcular al volver de segundo plano) y, solo en Debug, el panel del
/// simulador. Todo lo demás está enlazado al <see cref="TrabajoViewModel"/>.
/// </summary>
public partial class TrabajoPage : ContentPage
{
	private readonly TrabajoViewModel _vm;
	private Window? _ventana;

	public TrabajoPage(TrabajoViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _vm = viewModel;
		_vm.PropertyChanged += AlCambiarViewModel;
		ScrollDatos.PropertyChanged += AlCambiarVisibilidadDeLista;
		ScrollHistorial.PropertyChanged += AlCambiarVisibilidadDeLista;
	}

	// Al abrir o cerrar la capa de eventos, o al abrirse el aviso "Leyó otra vaca" (spec 009), se le quita el foco a "Caravana a mano":
	// si no, su teclado numérico queda abierto (por ejemplo tras una búsqueda a mano) y tapa los botones de la capa. Es solo de pantalla.
	private async void AlCambiarViewModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TrabajoViewModel.Ficha))
		{
			ReiniciarScrollSiCambioLaVaca();
			return;
		}
		if (e.PropertyName != nameof(TrabajoViewModel.HayEventos)
			&& e.PropertyName != nameof(TrabajoViewModel.HayConfirmarReemplazo)) return;
		try
		{
			CampoBusqueda.Unfocus();
			await CampoBusqueda.HideSoftInputAsync(CancellationToken.None);
		}
		catch (Exception)
		{
		}
	}

	// Las listas de Datos e Historial deben abrir desde arriba. El ScrollView conserva el desplazamiento de la ficha anterior,
	// así que al cambiar de vaca (o al cerrar y volver a abrir la ficha) se vuelve a 0 sin animación. Si la ficha se
	// refresca para la MISMA vaca (por ejemplo tras guardar un tacto) no se toca el scroll del usuario.
	// Solo una de las dos listas está visible; la otra queda "pendiente" y se reinicia cuando se muestra.
	private string? _caravanaMostrada;
	private bool _pendienteDatos, _pendienteHistorial;

	private void ReiniciarScrollSiCambioLaVaca()
	{
		var caravana = _vm.Ficha?.Caravana;
		if (caravana is null) { _caravanaMostrada = null; return; }   // ficha cerrada: la próxima apertura reinicia
		if (caravana == _caravanaMostrada) return;
		_caravanaMostrada = caravana;
		_pendienteDatos = _pendienteHistorial = true;
		AplicarScrollPendiente();
	}

	private void AplicarScrollPendiente()
	{
		// Dispatcher: el layout de la lista nueva tiene que estar armado antes de volver al tope.
		Dispatcher.Dispatch(async () =>
		{
			try
			{
				if (_pendienteDatos && ScrollDatos.IsVisible) { _pendienteDatos = false; await ScrollDatos.ScrollToAsync(0, 0, false); }
				if (_pendienteHistorial && ScrollHistorial.IsVisible) { _pendienteHistorial = false; await ScrollHistorial.ScrollToAsync(0, 0, false); }
			}
			catch (Exception)
			{
				// Es solo estética: si no se puede volver arriba, no pasa nada.
			}
		});
	}

	// Cuando la pestaña que estaba oculta pasa a verse, se aplica el reinicio que quedó pendiente.
	private void AlCambiarVisibilidadDeLista(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(IsVisible) && (_pendienteDatos || _pendienteHistorial)) AplicarScrollPendiente();
	}

	// Botón Atrás de Android (spec 009, RF-4): con el aviso "Leyó otra vaca" abierto equivale a "Cancelar lectura", que es la salida
	// que no descarta nada. Con cualquier otro cuadro o sin cuadro, Atrás se comporta como siempre (devolver false = lo maneja el sistema).
	protected override bool OnBackButtonPressed()
	{
		if (_vm.HayConfirmarReemplazo)
		{
			_vm.CancelarLecturaCommand.Execute(null);
			return true;
		}
		return base.OnBackButtonPressed();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		// Al volver de segundo plano se recalcula el contador y el avance (por si cambió el día o la zona horaria, RF-30).
		_ventana = Window;
		if (_ventana is not null)
		{
			_ventana.Resumed += AlVolverDeSegundoPlano;
			_ventana.Stopped += AlPasarASegundoPlano;   // spec 008, RF-16: sin lectura en segundo plano
		}

		try
		{
			// Arranca el lector, el refresco de cada 60 s y mantiene la pantalla encendida (RF-3).
			await _vm.IniciarAsync();
		}
		catch (Exception)
		{
			// Un async void que lanza tira abajo la app: en el campo preferimos que la pantalla siga viva.
		}
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();
		if (_ventana is not null)
		{
			_ventana.Resumed -= AlVolverDeSegundoPlano;
			_ventana.Stopped -= AlPasarASegundoPlano;
		}
		_ventana = null;

		try
		{
			// Corta el lector y el refresco y deja que la pantalla se apague.
			await _vm.DetenerAsync();
		}
		catch (Exception)
		{
		}
	}

	private async void AlVolverDeSegundoPlano(object? sender, EventArgs e)
	{
		try
		{
			await _vm.RecalcularAsync();
			await _vm.ReanudarLectorAsync();   // spec 008: reconecta con el bastón de inmediato
		}
		catch (Exception) { }
	}

	// La app pasó a segundo plano o se apagó la pantalla: el lector corta la conexión (spec 008, RF-16).
	private async void AlPasarASegundoPlano(object? sender, EventArgs e)
	{
		try { await _vm.PausarLectorAsync(); }
		catch (Exception) { }
	}

	// Al buscar (botón "Buscar" o Enter en el teclado) el teclado se baja solo: si no, queda tapando la ficha que acaba de aparecer.
	// Es solo de pantalla: la búsqueda en sí la hace el ViewModel con su comando.
	private async void OnBuscarTerminado(object? sender, EventArgs e)
	{
		try
		{
			CampoBusqueda.Unfocus();
			await CampoBusqueda.HideSoftInputAsync(CancellationToken.None);
		}
		catch (Exception)
		{
			// Si el teclado no se puede bajar, no pasa nada: la búsqueda ya se hizo.
		}
	}

}
