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

		// Preferencias de la tablet (SharedPreferences): una sola instancia con dos puertos, la de mangas y la limpieza de la
		// clave vieja del veterinario (spec 006, RF-21). La limpieza se registra ANTES de AddAvissInfrastructure, que usa
		// TryAdd con una versión que no hace nada y por eso no pisa esta.
		builder.Services.AddSingleton<PreferenciasMaui>();
		builder.Services.AddSingleton<ILimpiezaPreferencias>(sp => sp.GetRequiredService<PreferenciasMaui>());

		// Exportación (spec 007, RF-19, RF-20): guardar en Descargas con MediaStore y compartir. Va ANTES de AddAvissInfrastructure,
		// que registra con TryAdd un respaldo "no disponible" y por eso no lo pisa.
		builder.Services.AddSingleton<Aviss.Application.Exportacion.IEntregaArchivo, EntregaArchivoAndroid>();

		// Todo lo de datos vive en el almacenamiento privado de la app (en Android, /data/data/com.aviss.app/files).
		// Infrastructure no conoce MAUI: acá se le pasa la carpeta.
		builder.Services.AddAvissInfrastructure(new RutasAviss(FileSystem.AppDataDirectory));

		// Importación de Dairy Comp (spec 002): el perfil de Excel (NPOI) y lo propio de la plataforma Android.
		builder.Services.AddAvissExcel();
		builder.Services.AddSingleton<ISelectorArchivo, SelectorArchivoMaui>();
		builder.Services.AddSingleton<INavegacion, NavegacionShell>();

		// Flujo de campo (spec 003): lo propio de la tablet detrás de los puertos de Application.
		builder.Services.AddSingleton<IPreferenciasMangas>(sp => sp.GetRequiredService<PreferenciasMaui>());
		builder.Services.AddSingleton<IPantalla, PantallaMaui>();
		builder.Services.AddSingleton<ITemporizador, TemporizadorReal>();
		builder.Services.AddSingleton<IAvisosCampo, AvisosAndroid>();

#if DEBUG
		// Solo en la compilación de desarrollo: lector simulado y simulador de lecturas (RF-31). Es UNA instancia que
		// cumple las dos interfaces, para que lo que emite el panel lo reciba el flujo. Nada de esto existe en Release.
		builder.Services.AddSingleton<Aviss.Rfid.FakeRfidReader>();
		builder.Services.AddSingleton<IRfidReader>(sp => sp.GetRequiredService<Aviss.Rfid.FakeRfidReader>());
		builder.Services.AddSingleton<ISimuladorLecturas>(sp => sp.GetRequiredService<Aviss.Rfid.FakeRfidReader>());
#else
		// En la versión que se distribuye no hay simulador: el lector no entrega nada hasta la spec del bastón real.
		builder.Services.AddSingleton<IRfidReader, Aviss.Rfid.LectorNoDisponible>();
#endif

		// ViewModels y páginas transitorios: cada vez que se abre la pantalla arranca limpia en el paso 1.
		// (La vista previa sobrevive a rotar y a pasar a segundo plano porque la pantalla sigue abierta.)
		builder.Services.AddTransient<InicioViewModel>();
		builder.Services.AddTransient<ImportarViewModel>();
		// La sesión (el rodeo elegido) es única y vive mientras la app esté viva (RF-36).
		builder.Services.AddSingleton<SesionTrabajo>();
		builder.Services.AddTransient<EleccionTrabajoViewModel>();
		builder.Services.AddTransient<TrabajoViewModel>();
		builder.Services.AddTransient<MovimientosViewModel>();
		builder.Services.AddTransient<ExportarViewModel>();
		builder.Services.AddTransient<InicioPage>();
		builder.Services.AddTransient<ImportarPage>();
		builder.Services.AddTransient<TrabajoPage>();
		builder.Services.AddTransient<EleccionTrabajoPage>();
		builder.Services.AddTransient<MovimientosPage>();
		builder.Services.AddTransient<ExportarPage>();

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
