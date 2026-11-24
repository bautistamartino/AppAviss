using System.Runtime.Versioning;
using Android.Content;
using Android.OS;
using Android.Provider;
using Aviss.Application.Exportacion;
using Aviss.Domain.Enums;
using AndroidUri = Android.Net.Uri;

namespace Aviss.App;

/// <summary>
/// Entrega del archivo de vuelta en Android (spec 007, RF-19, RF-20, RF-24): lo guarda en la carpeta Descargas con
/// <c>MediaStore</c> (sin pedir permisos: desde Android 10 una app escribe sus propios archivos en Descargas sin ninguno) y abre
/// las opciones de compartir del sistema. No usa la red.
/// <para>
/// Mientras se escribe, el archivo figura como "pendiente" (<c>IS_PENDING = 1</c>): ninguna otra app lo ve a medias. Recién
/// cuando está completo se lo publica. Si algo falla o se cancela, se borra lo parcial.
/// </para>
/// </summary>
public sealed class EntregaArchivoAndroid : IEntregaArchivo
{
    private const string MimeCsv = "text/csv";
    private const string MimeXls = "application/vnd.ms-excel";

    public Task<ResultadoGuardadoArchivo> GuardarAsync(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct = default)
        // Escribir ~25 MB no debe trabar la pantalla: todo va en un hilo de fondo.
        => Task.Run(() => Guardar(nombreSugerido, contenido, formato, ct), ct);

    private static ResultadoGuardadoArchivo Guardar(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // MediaStore.Downloads existe desde Android 10 (API 29). La tablet de prueba tiene Android 16; para versiones más viejas
        // la app dice con claridad que no puede guardar (no se escribe nada).
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return ResultadoGuardadoArchivo.Fallido(FalloGuardado.NoDisponible);
        return GuardarConMediaStore(nombreSugerido, contenido, formato, ct);
    }

    [SupportedOSPlatform("android29.0")]
    private static ResultadoGuardadoArchivo GuardarConMediaStore(string nombreSugerido, byte[] contenido, FormatoExportacion formato, CancellationToken ct)
    {
        var resolver = Android.App.Application.Context.ContentResolver;
        if (resolver is null) return ResultadoGuardadoArchivo.Fallido(FalloGuardado.Otro);

        // Sin lugar suficiente se avisa antes de empezar (con margen chico para los metadatos).
        if (HayPocoEspacio(contenido.LongLength)) return ResultadoGuardadoArchivo.Fallido(FalloGuardado.SinEspacio);

        var nombre = NombreLibre(resolver, nombreSugerido);
        var valores = new ContentValues();
        valores.Put(MediaStore.IMediaColumns.DisplayName, nombre);
        valores.Put(MediaStore.IMediaColumns.MimeType, MimePara(formato));
        valores.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);
        valores.Put(MediaStore.IMediaColumns.IsPending, 1);   // invisible para las demás apps hasta terminar de escribir

        AndroidUri? uri = null;
        try
        {
            uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri!, valores);
            if (uri is null) return ResultadoGuardadoArchivo.Fallido(FalloGuardado.Otro);

            using (var salida = resolver.OpenOutputStream(uri, "w"))
            {
                if (salida is null) throw new IOException("No se pudo abrir el archivo para escribir.");
                salida.Write(contenido, 0, contenido.Length);
                salida.Flush();
            }

            // Última oportunidad de cancelar: todavía es "pendiente", así que se borra sin que nadie lo haya visto.
            ct.ThrowIfCancellationRequested();

            // Publicar: recién ahora el archivo es visible.
            var publicar = new ContentValues();
            publicar.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, publicar, null, null);

            // Android puede haberle agregado un número si ya había uno igual: se informa el nombre FINAL.
            var nombreFinal = NombreDe(resolver, uri) ?? nombre;
            return ResultadoGuardadoArchivo.Correcto(new ArchivoGuardado(nombreFinal, "Descargas", uri.ToString()!));
        }
        catch (System.OperationCanceledException)
        {
            Borrar(resolver, uri);
            throw;
        }
        catch (Exception ex)
        {
            // Nada a medias: se borra lo parcial y se informa solo la categoría (nunca el nombre ni la ruta).
            Borrar(resolver, uri);
            return ResultadoGuardadoArchivo.Fallido(Clasificar(ex));
        }
    }

    public async Task CompartirAsync(ArchivoGuardado archivo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // El Share de MAUI solo recibe rutas de archivo; el archivo de Descargas es un content:// de MediaStore, así que se arma
        // el Intent de "enviar" a mano. El permiso de lectura se le da al destino que el veterinario elija, y solo a este archivo.
        var uri = AndroidUri.Parse(archivo.Referencia);
        var mime = archivo.Nombre.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? MimeCsv : MimeXls;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var envio = new Intent(Intent.ActionSend);
            envio.SetType(mime);
            envio.PutExtra(Intent.ExtraStream, uri);
            envio.ClipData = ClipData.NewRawUri(archivo.Nombre, uri);
            envio.AddFlags(ActivityFlags.GrantReadUriPermission);

            var selector = Intent.CreateChooser(envio, "Compartir archivo")!;
            var actividad = Platform.CurrentActivity;
            if (actividad is not null)
            {
                actividad.StartActivity(selector);
            }
            else
            {
                selector.AddFlags(ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(selector);
            }
        });
    }

    private static string MimePara(FormatoExportacion formato) => formato == FormatoExportacion.Csv ? MimeCsv : MimeXls;

    /// <summary>Si ya hay un archivo propio con ese nombre en Descargas, agrega " (1)", " (2)"... antes de la extensión (RF-19).</summary>
    [SupportedOSPlatform("android29.0")]
    private static string NombreLibre(ContentResolver resolver, string sugerido)
    {
        var punto = sugerido.LastIndexOf('.');
        var baseNombre = punto > 0 ? sugerido[..punto] : sugerido;
        var extension = punto > 0 ? sugerido[punto..] : "";

        var candidato = sugerido;
        for (var n = 1; ExisteEnDescargas(resolver, candidato) && n < 1000; n++)
            candidato = $"{baseNombre} ({n}){extension}";
        return candidato;
    }

    [SupportedOSPlatform("android29.0")]
    private static bool ExisteEnDescargas(ContentResolver resolver, string nombre)
    {
        try
        {
            using var cursor = resolver.Query(MediaStore.Downloads.ExternalContentUri!, [MediaStore.IMediaColumns.DisplayName],
                $"{MediaStore.IMediaColumns.DisplayName} = ?", [nombre], null);
            return cursor is not null && cursor.MoveToFirst();
        }
        catch (Exception)
        {
            return false;   // si no se puede mirar, MediaStore igual evita pisar (renombra al insertar)
        }
    }

    [SupportedOSPlatform("android29.0")]
    private static string? NombreDe(ContentResolver resolver, AndroidUri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [MediaStore.IMediaColumns.DisplayName], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Borrar(ContentResolver resolver, AndroidUri? uri)
    {
        if (uri is null) return;
        try { resolver.Delete(uri, null, null); }
        catch (Exception) { /* no hay más que hacer: sigue pendiente (invisible) y el sistema lo limpia */ }
    }

    private static bool HayPocoEspacio(long bytes)
    {
        try
        {
            using var stat = new StatFs(Android.OS.Environment.ExternalStorageDirectory!.Path);
            return stat.AvailableBytes < bytes + 1024 * 1024;
        }
        catch (Exception)
        {
            return false;   // no se pudo medir: se intenta y, si falta lugar, falla la escritura
        }
    }

    private static FalloGuardado Clasificar(Exception ex)
    {
        if (ex is Java.Lang.SecurityException or UnauthorizedAccessException) return FalloGuardado.SinPermiso;
        // "ENOSPC (No space left on device)" llega como IOException con ese texto.
        var texto = ex.Message ?? "";
        if (texto.Contains("ENOSPC", StringComparison.OrdinalIgnoreCase) || texto.Contains("No space", StringComparison.OrdinalIgnoreCase))
            return FalloGuardado.SinEspacio;
        return FalloGuardado.Otro;
    }
}
