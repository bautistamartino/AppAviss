using Aviss.App.ViewModels;
using Aviss.Application.Campo;

namespace Aviss.App.Paginas;

/// <summary>
/// Pantalla de trabajo (spec 003). Acá no hay lógica de negocio: solo el ciclo de vida de la página (arrancar y cortar el
/// lector, recalcular al volver de segundo plano), la lista para cambiar de veterinario y, solo en Debug, el panel del
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

#if DEBUG
		ConstruirPanelSimulador();
#endif
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

	// ---- Cambiar de veterinario sin salir de la manga (RF-1) ----

	private async void OnCambiarVeterinarioClicked(object? sender, EventArgs e)
	{
		IReadOnlyList<VeterinarioDto> veterinarios;
		try { veterinarios = await _vm.ListarVeterinariosAsync(); }
		catch (Exception) { return; }

		ListaVeterinarios.Children.Clear();
		// El estilo de botón de campo vive en Styles.xaml (recursos de la aplicación).
		// (Se escribe completo: "Application" solo, dentro de Aviss.*, apunta al espacio de nombres Aviss.Application.)
		Microsoft.Maui.Controls.Application.Current!.Resources.TryGetValue("BotonCampo", out var estilo);
		foreach (var v in veterinarios)
		{
			var boton = new Button { Text = v.Nombre, BindingContext = v };
			if (estilo is Style s) boton.Style = s;
			boton.Clicked += OnVeterinarioElegido;
			ListaVeterinarios.Children.Add(boton);
		}
		CapaVeterinarios.IsVisible = true;
	}

	private void OnVeterinarioElegido(object? sender, EventArgs e)
	{
		if (sender is Button { BindingContext: VeterinarioDto elegido })
			_vm.CambiarVeterinarioCommand.Execute(elegido);
		CapaVeterinarios.IsVisible = false;
	}

	private void OnCerrarVeterinariosClicked(object? sender, EventArgs e) => CapaVeterinarios.IsVisible = false;

#if DEBUG
	// ---- Panel del simulador: SOLO en la compilación de desarrollo (RF-31) ----
	// Está armado desde código, dentro de #if DEBUG, para que en la versión que se distribuye ni siquiera exista.
	private void ConstruirPanelSimulador()
	{
		if (!_vm.HaySimulador) return;

		PanelSimulador.Children.Add(new Label
		{
			Text = "Simulador (solo Debug):",
			TextColor = Colors.Black,
			FontAttributes = FontAttributes.Bold,
			FontSize = 16,
			Margin = new Thickness(0, 0, 8, 0),
			VerticalOptions = LayoutOptions.Center
		});
		PanelSimulador.Children.Add(BotonSimulador("Leer vaca existente", _vm.SimularExistenteCommand));
		PanelSimulador.Children.Add(BotonSimulador("Leer vaca ya leída", _vm.SimularYaLeidaCommand));
		PanelSimulador.Children.Add(BotonSimulador("Leer caravana inexistente", _vm.SimularInexistenteCommand));
		PanelSimulador.IsVisible = true;
	}

	private static Button BotonSimulador(string texto, System.Windows.Input.ICommand comando) => new()
	{
		Text = texto,
		Command = comando,
		FontSize = 16,
		HeightRequest = 44,
		Padding = new Thickness(10, 0),
		Margin = new Thickness(0, 2, 6, 2),
		BackgroundColor = Color.FromArgb("#455A64"),
		TextColor = Colors.White
	};
#endif
}
