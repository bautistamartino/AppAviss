using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Aviss.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 15 en adelante dibuja la app por debajo de las barras del sistema (de estado y de navegación o de tareas).
        // MAUI aplica esos márgenes al arrancar, pero al abrir y cerrar el teclado dejaba de aplicarlos y la barra de búsqueda
        // quedaba detrás de los botones de inicio y volver. Se aplican acá, siempre: el contenido se acomoda a la barra de
        // estado, a la de navegación y al teclado, y esos márgenes ya no le llegan a MAUI.
        var contenido = FindViewById(Android.Resource.Id.Content);
        if (contenido is not null)
            ViewCompat.SetOnApplyWindowInsetsListener(contenido, new AjustarAMargenesDelSistema());
    }

    private sealed class AjustarAMargenesDelSistema : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View view, WindowInsetsCompat insets)
        {
            var margenes = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.Ime());
            view.SetPadding(margenes.Left, margenes.Top, margenes.Right, margenes.Bottom);
            return WindowInsetsCompat.Consumed;
        }
    }
}
