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

    /// <summary>
    /// "No entra en tacto" (CVET 0): la chicharra. Vibración larga y un zumbido grave por el canal de alarma, bien distinto
    /// del pitido de "repetido", para saber de oído que esa vaca no se tacta.
    /// </summary>
    public void SinTacto()
    {
        Vibrar(milisegundos: 500);
        Zumbar();
    }


    /// <summary>La manga llegó a su cantidad y se cerró sola: una vibración corta y un tono positivo de confirmación.</summary>
    public void MangaCompleta()
    {
        Vibrar(milisegundos: 150);
        Tono(Tone.PropAck, 300);
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

    // Un tono corto por el canal de notificaciones; se libera después para no dejar un recurso de audio abierto.
    private static void Tono(Tone tipo, int milisegundos)
    {
        try
        {
            var tono = new ToneGenerator(Android.Media.Stream.Notification, 100);
            tono.StartTone(tipo, milisegundos);
            _ = Task.Delay(milisegundos + 300).ContinueWith(_ => tono.Release());
        }
        catch (Exception)
        {
            // Sin sonido alcanzan la vibración y el aviso visible.
        }
    }

    private static void Zumbar()
    {
        try
        {
            // Canal de alarma y volumen máximo del generador: se oye sobre el ruido de la manga. PropNack es un zumbido doble
            // grave ("negativo"), parecido a una chicharra.
            var tono = new ToneGenerator(Android.Media.Stream.Alarm, 100);
            tono.StartTone(Tone.PropNack, 600);
            _ = Task.Delay(900).ContinueWith(_ => tono.Release());
        }
        catch (Exception)
        {
            // Sin sonido alcanzan la vibración y el aviso visible.
        }
    }
}
