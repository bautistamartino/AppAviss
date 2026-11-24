using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>Un tambo. No se borra: solo se da de baja (RF-24).</summary>
public sealed class Establecimiento : EntidadEditable
{
    public string Nombre { get; private set; } = "";
    public string? Notas { get; private set; }
    public bool Activo { get; private set; }

    private Establecimiento() { }

    public static Establecimiento Crear(string? nombre, string? notas, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            Nombre = Validar.TextoObligatorio(nombre, "El nombre del establecimiento"),
            Notas = Validar.TextoOpcional(notas),
            Activo = true
        };

    private Establecimiento(DateTime ahoraUtc) : base(ahoraUtc) { }

    /// <summary>Cambia el nombre (mismas reglas que al crear: obligatorio, sin espacios de más). No toca nada más.</summary>
    public void Renombrar(string? nombre, DateTime ahoraUtc)
    {
        // Primero se valida: si el nombre no sirve, la entidad queda como estaba.
        var limpio = Validar.TextoObligatorio(nombre, "El nombre del establecimiento");
        Nombre = limpio;
        Tocar(ahoraUtc);
    }

    public void DarDeBaja(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Reactivar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }
}
