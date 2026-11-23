using Aviss.Domain.Catalogo;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Domain.Entidades;

/// <summary>
/// Un estado del catálogo (PREÑADA, SECA, ...). Los tactos lo referencian por Id, así que renombrarlo no rompe nada (RF-5).
/// El <see cref="Codigo"/> interno es estable; <see cref="CodigoDairyComp"/> es el texto exacto que se usará en el Excel.
/// </summary>
public sealed class EstadoReproductivo : EntidadEditable
{
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public string CodigoDairyComp { get; private set; } = "";
    public bool Activo { get; private set; }

    /// <summary>Spec 005: <c>Estado</c> cambia el estado de la vaca; <c>SoloEvento</c> solo queda registrado (RF-1).</summary>
    public TipoEvento Tipo { get; private set; } = TipoEvento.Estado;

    /// <summary>Si se ve siempre en la lista de la ficha (principal) o detrás de "Otro evento" (RF-2).</summary>
    public bool EsPrincipal { get; private set; }

    /// <summary>Lugar entre los principales (de arriba abajo). Los de "Otro evento" no lo usan: van por nombre (RF-7).</summary>
    public int Orden { get; private set; }

    /// <summary>
    /// Identidad estable de los 9 eventos estándar (RF-5): nula en los creados a mano. No cambia al renombrar ni al
    /// cambiar el código de Dairy Comp; es lo que reconoce a "OK" aunque se llame distinto.
    /// </summary>
    public string? ClaveEstandar { get; private set; }

    /// <summary>
    /// Nombre normalizado para detectar repetidos sin distinguir mayúsculas (incluida la Ñ).
    /// Solo lectura: en la base es una columna generada con la misma regla.
    /// </summary>
    public string NombreClave => Caravana.CalcularClave(Nombre);

    private EstadoReproductivo() { }
    private EstadoReproductivo(DateTime ahoraUtc) : base(ahoraUtc) { }

    public static EstadoReproductivo Crear(string? codigo, string? nombre, string? codigoDairyComp, DateTime ahoraUtc)
        => new(ahoraUtc)
        {
            Codigo = ValidarCodigo(codigo),
            Nombre = Validar.TextoObligatorio(nombre, "El nombre del estado"),
            CodigoDairyComp = Validar.TextoObligatorio(codigoDairyComp, "El código de Dairy Comp"),
            Activo = true
        };

    /// <summary>Un evento estándar nuevo (siembra): con su clave, tipo, principal y orden (RF-1 a RF-3).</summary>
    public static EstadoReproductivo CrearEstandar(EstadoInicial inicial, DateTime ahoraUtc)
    {
        var e = Crear(inicial.Codigo, inicial.Nombre, inicial.CodigoDairyComp, ahoraUtc);
        e.ClaveEstandar = inicial.Codigo;
        e.Tipo = inicial.Tipo;
        e.EsPrincipal = inicial.EsPrincipal;
        e.Orden = inicial.Orden;
        return e;
    }

    /// <summary>
    /// Un estado ya existente (por ejemplo un "Aborto" creado a mano) pasa a ser el estándar (RF-20): recibe clave, tipo,
    /// principal y orden. NO cambia nombre, código interno, código de Dairy Comp ni si está activo.
    /// </summary>
    public void AdoptarComoEstandar(EstadoInicial inicial, DateTime ahoraUtc)
    {
        ClaveEstandar = inicial.Codigo;
        Tipo = inicial.Tipo;
        EsPrincipal = inicial.EsPrincipal;
        Orden = inicial.Orden;
        Tocar(ahoraUtc);
    }

    /// <summary>Cambia nombre y código de Dairy Comp. El código interno y la identidad estándar no cambian nunca.</summary>
    public void Modificar(string? nombre, string? codigoDairyComp, DateTime ahoraUtc)
    {
        Nombre = Validar.TextoObligatorio(nombre, "El nombre del estado");
        CodigoDairyComp = Validar.TextoObligatorio(codigoDairyComp, "El código de Dairy Comp");
        Tocar(ahoraUtc);
    }

    public void Desactivar(DateTime ahoraUtc) { Activo = false; Tocar(ahoraUtc); }
    public void Activar(DateTime ahoraUtc) { Activo = true; Tocar(ahoraUtc); }

    /// <summary>Solo A-Z, 0-9 y guion bajo (mayúsculas, sin acentos ni espacios). Mismo criterio que el CHECK de la base.</summary>
    private static string ValidarCodigo(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
            throw new DominioException("El código interno del estado es obligatorio.");
        foreach (var c in codigo)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
            if (!ok)
                throw new DominioException("El código interno solo admite mayúsculas sin acentos, números y guion bajo.");
        }
        return codigo;
    }
}
