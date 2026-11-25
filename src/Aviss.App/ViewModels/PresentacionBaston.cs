using Aviss.Application.Puertos;

namespace Aviss.App.ViewModels;

/// <summary>Cómo se ve la conexión con el bastón en la pantalla de trabajo (spec 008, RF-13).</summary>
public enum EstadoBaston { Conectado, Buscando, SinBaston }

/// <summary>Qué hace el botón grande del cartel según por qué no hay bastón (RF-12).</summary>
public enum AccionBaston
{
    Ninguna,
    /// <summary>Reintentar ya, saltando la espera.</summary>
    Reconectar,
    /// <summary>Abrir la lista de bastones emparejados.</summary>
    ElegirBaston,
    /// <summary>Volver a pedir el permiso de Bluetooth y, si sigue denegado, abrir los ajustes de la app.</summary>
    PedirPermiso,
    AbrirAjustesBluetooth,
}

/// <summary>
/// El mapa puro de "estado y motivo del lector" a lo que se muestra: texto, qué hace el toque y color (spec 008, RF-1, RF-4 a RF-6,
/// RF-11 a RF-13). Sin MAUI, para probarlo con xUnit. El estado nunca depende solo del color: siempre lleva texto (constitución §9).
/// </summary>
public sealed record PresentacionBaston(
    EstadoBaston Estado, string Texto, string Detalle, string TextoAccion, AccionBaston Accion, string ColorFondo, bool HayAviso)
{
    // Alto contraste con texto blanco, para distinguirlos al sol.
    public const string ColorVerde = "#1B5E20";
    public const string ColorAmbar = "#E65100";
    public const string ColorRojo = "#B71C1C";

    public static PresentacionBaston Crear(EstadoConexionRfid estado, MotivoSinBaston motivo) => estado switch
    {
        EstadoConexionRfid.Conectado => new(EstadoBaston.Conectado, "Conectado", "", "", AccionBaston.Ninguna, ColorVerde, false),
        EstadoConexionRfid.Conectando => new(EstadoBaston.Buscando, "Buscando el bastón", "", "", AccionBaston.Ninguna, ColorAmbar, true),
        _ => SinBaston(motivo),
    };

    private static PresentacionBaston SinBaston(MotivoSinBaston motivo)
    {
        const string sin = "Sin bastón";
        return motivo switch
        {
            MotivoSinBaston.NoElegido =>
                new(EstadoBaston.SinBaston, sin, "Elegí el bastón", "Tocá para elegir", AccionBaston.ElegirBaston, ColorRojo, true),
            MotivoSinBaston.NoEmparejado =>
                new(EstadoBaston.SinBaston, sin, "El bastón ya no está emparejado", "Tocá para elegir otro", AccionBaston.ElegirBaston, ColorRojo, true),
            MotivoSinBaston.SinPermiso =>
                new(EstadoBaston.SinBaston, sin, "Falta el permiso de Bluetooth",
                    "Tocá para darlo (Permisos > Dispositivos cercanos)", AccionBaston.PedirPermiso, ColorRojo, true),
            MotivoSinBaston.BluetoothApagado =>
                new(EstadoBaston.SinBaston, sin, "Prendé el Bluetooth de la tablet", "Tocá para abrir los ajustes", AccionBaston.AbrirAjustesBluetooth, ColorRojo, true),
            MotivoSinBaston.SinBluetooth =>
                new(EstadoBaston.SinBaston, sin, "Esta tablet no tiene Bluetooth", "", AccionBaston.Ninguna, ColorRojo, true),
            MotivoSinBaston.NoResponde =>
                new(EstadoBaston.SinBaston, sin, "El bastón no responde: prendelo o acercalo", "Tocá para reconectar", AccionBaston.Reconectar, ColorRojo, true),
            // Sin motivo: el lector recién se cortó (pausa de la pantalla) o no tiene este concepto. Reconectar es lo único que tiene sentido.
            _ => new(EstadoBaston.SinBaston, sin, "Sin conexión con el bastón", "Tocá para reconectar", AccionBaston.Reconectar, ColorRojo, true),
        };
    }
}
