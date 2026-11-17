using Aviss.App.ViewModels;
#if DEBUG
using Aviss.Application.Servicios;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace Aviss.App.Paginas;

public partial class InicioPage : ContentPage
{
	public InicioPage(InicioViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;

#if DEBUG
		// ACCESO TEMPORAL (spec 003, T4): hasta que T5 agregue la pantalla de elección y el botón "Trabajar", este botón,
		// que solo existe en Debug, deja elegidos un veterinario y el primer rodeo importado y abre la pantalla de trabajo.
		// T5 lo borra.
		Contenido.Children.Add(new Button
		{
			Text = "Probar pantalla de trabajo (temporal, Debug)",
			FontSize = 24,
			FontAttributes = FontAttributes.Bold,
			HeightRequest = 72,
			Command = new Command(async () => await AbrirTrabajoDePruebaAsync())
		});
#endif
	}

#if DEBUG
	private async Task AbrirTrabajoDePruebaAsync()
	{
		var servicios = Handler!.MauiContext!.Services;
		var campo = servicios.GetRequiredService<ICampoService>();
		var sesion = servicios.GetRequiredService<SesionTrabajo>();
		var navegacion = servicios.GetRequiredService<INavegacion>();

		var rodeos = await campo.ListarRodeosParaTrabajarAsync();
		if (rodeos.Count == 0)
		{
			await DisplayAlert("Sin rodeos", "Primero importá un rodeo con \"Importar rodeo\".", "Aceptar");
			return;
		}

		var veterinarios = await campo.ListarVeterinariosActivosAsync();
		if (veterinarios.Count == 0)
		{
			// Sin veterinarios no se puede trabajar: se crea uno de prueba.
			await campo.CrearVeterinarioAsync("Veterinario de prueba", null);
			veterinarios = await campo.ListarVeterinariosActivosAsync();
		}

		sesion.Empezar(veterinarios[0], rodeos[0]);
		await navegacion.IrATrabajoAsync();
	}
#endif
}
