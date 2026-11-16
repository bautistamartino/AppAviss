using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>Un veterinario. Sin contraseña (RF-7). No se borra: solo se da de baja.</summary>
public sealed class Usuario : EntidadEditable
{
    /// <summary>Id fijo del usuario "Sin asignar" (RF-7b): se usa hasta que exista la selección de veterinario.</summary>
    public static readonly Guid SinAsignarId = new("00000000-0000-0000-0000-000000000001");
    public const string NombreSinAsignar = "Sin asignar";
    public const int MatriculaMaxima = 30;

    public string Nombre { get; private set; } = "";
    public string? Matricula { get; private set; }
    public bool Activo { get; private set; }

    private Usuario() { }
    private Usuario(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static Usuario Crear(string? nombre, string? matricula, DateTime ahoraUtc)
    {
        var mat = Validar.TextoOpcional(matricula);
        if (mat is not null && mat.Length > MatriculaMaxima)
            throw new DominioException($"La matrícula no puede superar los {MatriculaMaxima} caracteres.");
        return new Usuario(ahoraUtc)
        {
            Nombre = Validar.TextoObligatorio(nombre, "El nombre del usuario"),
            Matricula = mat,
            Activo = true
        };
    }

    /// <summary>El usuario por defecto, con Id fijo, que se siembra al crear la base.</summary>
    public static Usuario CrearSinAsignar(DateTime ahoraUtc)
    {
        var u = Crear(NombreSinAsignar, null, ahoraUtc);
        u.Id = SinAsignarId;
        return u;
    }

    public void DarDeBaja(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Reactivar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }
}
