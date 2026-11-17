using Android.Content;
using Android.Media;
using Android.OS;
using Aviss.Application.Puertos;

namespace Aviss.App;

/// <summary>
/// Vibración y sonido simples del sistema para el flujo de campo (spec 003, RF-16 y RF-22). Sin voz y sin paquetes
/// nuevos: usa <c>Vibrator</c> y <c>ToneGenerator</c> de Android. La vibración necesita el permiso normal VIBRATE
/// (declarado en el manifiesto). Si algo falla (emulador sin vibrador, sin sonido), no pasa nada: el aviso visible
/// siempre se muestra igual.
/// </summary>
public sealed class AvisosAndroid : IAvisosCampo
{
    /// <summary>Algo se guardó: una vibración corta.</summary>
    public void Confirmacion() => Vibrar(milisegundos: 60);

    /// <summary>Animal repetido o "Ya pasó": vibración más larga y un pitido, para que se note aunque no se mire la pantalla.</summary>
    public void Repetido()
    {
        Vibrar(milisegundos: 300);
        Pitar();
    }

    private static void Vibrar(long milisegundos)
    {
        try
        {
            var vibrador = ObtenerVibrador();
            if (vibrador is null || !vibrador.HasVibrator) return;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                vibrador.Vibrate(VibrationEffect.CreateOneShot(milisegundos, VibrationEffect.DefaultAmplitude));
            else
#pragma warning disable CS0618 // Vibrate(long) está obsoleto desde Android 8, pero es lo único que hay en versiones viejas.
                vibrador.Vibrate(milisegundos);
#pragma warning restore CS0618
        }
        catch (Exception)
        {
            // Sin vibración no se pierde nada importante.
        }
    }

    private static Vibrator? ObtenerVibrador()
    {
        var contexto = Android.App.Application.Context;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            var manager = (VibratorManager?)contexto.GetSystemService(Context.VibratorManagerService);
            return manager?.DefaultVibrator;
        }
#pragma warning disable CS0618 // VibratorService está obsoleto desde Android 12; se usa solo en versiones anteriores.
        return (Vibrator?)contexto.GetSystemService(Context.VibratorService);
#pragma warning restore CS0618
    }

    private static void Pitar()
    {
        try
        {
            // Se crea y se libera en cada pitido: son pocos y evita dejar un recurso de audio abierto todo el día.
            var tono = new ToneGenerator(Android.Media.Stream.Notification, 100);
            tono.StartTone(Tone.PropBeep2, 250);
            // El ToneGenerator reproduce en un hilo propio: se libera un rato después para no cortar el sonido.
            _ = Task.Delay(500).ContinueWith(_ => tono.Release());
        }
        catch (Exception)
        {
            // Sin sonido el aviso visible alcanza.
        }
    }
}
