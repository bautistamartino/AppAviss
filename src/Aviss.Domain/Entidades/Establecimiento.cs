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

    public void DarDeBaja(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Reactivar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }
}
