using Aviss.Application.Puertos;
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

		// Todo lo de datos vive en el almacenamiento privado de la app (en Android, /data/data/com.aviss.app/files).
		// Infrastructure no conoce MAUI: acá se le pasa la carpeta.
		builder.Services.AddAvissInfrastructure(new RutasAviss(FileSystem.AppDataDirectory));

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
