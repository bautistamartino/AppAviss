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

	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		// Al volver de segundo plano se recalcula el contador y el avance (por si cambió el día o la zona horaria, RF-30).
		_ventana = Window;
		if (_ventana is not null) _ventana.Resumed += AlVolverDeSegundoPlano;

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
		if (_ventana is not null) _ventana.Resumed -= AlVolverDeSegundoPlano;
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
		try { await _vm.RecalcularAsync(); }
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
