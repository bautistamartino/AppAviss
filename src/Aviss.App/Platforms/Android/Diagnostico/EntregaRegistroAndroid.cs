using System.Runtime.Versioning;
using Android.Content;
using Android.Provider;
using Aviss.Rfid.Diagnostico;
using AndroidUri = Android.Net.Uri;

namespace Aviss.App.Diagnostico;

/// <summary>
/// Saca el registro de diagnóstico de la tablet (como <c>EntregaArchivoAndroid</c> con el Excel): lo copia a Descargas con
/// <c>MediaStore</c> (sin permisos) y abre "Compartir" con ese archivo. El original queda en el almacenamiento privado de la app.
/// </summary>
public sealed class EntregaRegistroAndroid : IEntregaRegistro
{
    public Task<string?> GuardarEnDescargasAsync(string ruta) => Task.Run(() => Guardar(ruta)?.Nombre);

    public async Task CompartirAsync(string ruta)
    {
        // El archivo de la app es privado: se copia a Descargas y se comparte esa copia (content://), igual que la exportación.
        var guardado = await Task.Run(() => Guardar(ruta)) ?? throw new IOException("No se pudo copiar el registro a Descargas.");

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var envio = new Intent(Intent.ActionSend);
            envio.SetType("text/plain");
            envio.PutExtra(Intent.ExtraStream, guardado.Uri);
            envio.ClipData = ClipData.NewRawUri(guardado.Nombre, guardado.Uri);
            envio.AddFlags(ActivityFlags.GrantReadUriPermission);

            var selector = Intent.CreateChooser(envio, "Compartir registro")!;
            var actividad = Platform.CurrentActivity;
            if (actividad is not null) actividad.StartActivity(selector);
            else
            {
                selector.AddFlags(ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(selector);
            }
        });
    }

    private static (string Nombre, AndroidUri Uri)? Guardar(string ruta)
    {
        // MediaStore.Downloads existe desde Android 10 (la tablet de prueba tiene Android 16).
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
        return GuardarConMediaStore(ruta);
    }

    [SupportedOSPlatform("android29.0")]
    private static (string Nombre, AndroidUri Uri)? GuardarConMediaStore(string ruta)
    {
        var resolver = Android.App.Application.Context.ContentResolver;
        if (resolver is null || !File.Exists(ruta)) return null;

        var nombre = Path.GetFileName(ruta);
        var valores = new ContentValues();
        valores.Put(MediaStore.IMediaColumns.DisplayName, nombre);
        valores.Put(MediaStore.IMediaColumns.MimeType, "text/plain");
        valores.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);
        valores.Put(MediaStore.IMediaColumns.IsPending, 1);

        AndroidUri? uri = null;
        try
        {
            uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri!, valores);
            if (uri is null) return null;

            // El archivo puede seguir abierto para escribir (la sesión sigue): se lo lee compartido.
            using (var origen = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var salida = resolver.OpenOutputStream(uri, "w") ?? throw new IOException("No se pudo abrir el destino."))
            {
                var bytes = new byte[origen.Length];
                origen.ReadExactly(bytes);
                salida.Write(bytes, 0, bytes.Length);
                salida.Flush();
            }

            var publicar = new ContentValues();
            publicar.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, publicar, null, null);

            // Android puede renombrar si ya existía uno igual: se informa el nombre final.
            using var cursor = resolver.Query(uri, [MediaStore.IMediaColumns.DisplayName], null, null, null);
            var final = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) ?? nombre : nombre;
            return (final, uri);
        }
        catch (Exception)
        {
            if (uri is not null)
            {
                try { resolver.Delete(uri, null, null); } catch (Exception) { }
            }
            return null;
        }
    }
}
