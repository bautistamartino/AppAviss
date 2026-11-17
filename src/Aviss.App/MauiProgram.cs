using Aviss.App.Paginas;
using Aviss.App.Servicios;
using Aviss.App.ViewModels;
using Aviss.Application.Puertos;
using Aviss.Excel;
using Aviss.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aviss.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// El espacio libre se mide con StatFs en Android (DriveInfo mide "/" y no el almacenamiento de la app). Se registra
		// ANTES de AddAvissInfrastructure, que usa TryAdd y por eso no lo pisa.
		builder.Services.AddSingleton<Aviss.Infrastructure.Sistema.IEspacioLibreProvider, EspacioLibreAndroid>();

		// Todo lo de datos vive en el almacenamiento privado de la app (en Android, /data/data/com.aviss.app/files).
		// Infrastructure no conoce MAUI: acá se le pasa la carpeta.
		builder.Services.AddAvissInfrastructure(new RutasAviss(FileSystem.AppDataDirectory));

		// Importación de Dairy Comp (spec 002): el perfil de Excel (NPOI) y lo propio de la plataforma Android.
		builder.Services.AddAvissExcel();
		builder.Services.AddSingleton<ISelectorArchivo, SelectorArchivoMaui>();
		builder.Services.AddSingleton<INavegacion, NavegacionShell>();

		// ViewModels y páginas transitorios: cada vez que se abre la pantalla arranca limpia en el paso 1.
		// (La vista previa sobrevive a rotar y a pasar a segundo plano porque la pantalla sigue abierta.)
		builder.Services.AddTransient<InicioViewModel>();
		builder.Services.AddTransient<ImportarViewModel>();
		builder.Services.AddTransient<InicioPage>();
		builder.Services.AddTransient<ImportarPage>();

		var app = builder.Build();

		// Arranque de la base (crear, revisar, actualizar con backup). Se espera acá, antes de que exista cualquier pantalla,
		// para que nadie toque la base a medio preparar. Task.Run evita bloquear el hilo principal con contexto de UI
		// (sin deadlock). El resultado queda en IArranqueResultado, que una pantalla futura leerá (RF-28); el servicio
		// ya captura sus errores y los convierte en un resultado, así que acá no se lanza nada.
		var arranque = app.Services.GetRequiredService<IArranqueService>();
		Task.Run(() => arranque.IniciarAsync()).GetAwaiter().GetResult();

		return app;
	}
}
