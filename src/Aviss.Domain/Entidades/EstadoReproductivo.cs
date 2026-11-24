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

    /// <summary>
    /// Spec 005 (segunda vuelta): los pasos que pide este evento después de elegirlo, como nombres de
    /// <see cref="PasoEvento"/> separados por coma ("DiasAborto,ResultadoAborto"). Nulo = no pide nada. Los declara el
    /// catálogo, no la pantalla (RF-27): un evento nuevo con pasos no obliga a tocar un "if" por nombre.
    /// </summary>
    public string? Pasos { get; private set; }

    /// <summary>
    /// El estado en que queda la vaca al elegir este evento, si no es el propio evento (Preñada fecha previa → PREÑADA,
    /// RF-26). Nulo en el resto.
    /// </summary>
    public Guid? EstadoDestinoId { get; private set; }

    /// <summary>Los pasos como lista, en el orden en que se piden. Un nombre desconocido (de una versión futura) se ignora.</summary>
    public IReadOnlyList<PasoEvento> ListarPasos()
    {
        if (string.IsNullOrWhiteSpace(Pasos)) return [];
        var lista = new List<PasoEvento>();
        foreach (var texto in Pasos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<PasoEvento>(texto, out var paso)) lista.Add(paso);
        return lista;
    }

    /// <summary>
    /// Un evento estándar nuevo (siembra): con su clave, tipo, principal, orden, pasos y destino (RF-1 a RF-3, RF-26).
    /// <paramref name="estadoDestinoId"/> es el Id del estado que nombra <c>inicial.DestinoClave</c>, ya resuelto por quien siembra.
    /// </summary>
    public static EstadoReproductivo CrearEstandar(EstadoInicial inicial, DateTime ahoraUtc, Guid? estadoDestinoId = null)
    {
        var e = Crear(inicial.Codigo, inicial.Nombre, inicial.CodigoDairyComp, ahoraUtc);
        e.ClaveEstandar = inicial.Codigo;
        e.AplicarEstandar(inicial, estadoDestinoId);
        return e;
    }

    /// <summary>
    /// Un estado ya existente (por ejemplo un "Aborto" creado a mano) pasa a ser el estándar (RF-20): recibe clave, tipo,
    /// principal, orden, pasos y destino. NO cambia nombre, código interno, código de Dairy Comp ni si está activo.
    /// </summary>
    public void AdoptarComoEstandar(EstadoInicial inicial, DateTime ahoraUtc, Guid? estadoDestinoId = null)
    {
        ClaveEstandar = inicial.Codigo;
        AplicarEstandar(inicial, estadoDestinoId);
        Tocar(ahoraUtc);
    }

    /// <summary>
    /// Iguala tipo, principal, orden, pasos y destino a la tabla de estándar (los que reconcilia el sembrador en cada
    /// arranque). No toca nombre, códigos ni si está activo. Devuelve si cambió algo: así un arranque que no tiene nada
    /// para corregir no modifica la fila ni su fecha de actualización (idempotente).
    /// </summary>
    public bool Reconciliar(EstadoInicial inicial, Guid? estadoDestinoId, DateTime ahoraUtc)
    {
        if (Tipo == inicial.Tipo && EsPrincipal == inicial.EsPrincipal && Orden == inicial.Orden
            && Pasos == inicial.Pasos && EstadoDestinoId == estadoDestinoId)
            return false;
        AplicarEstandar(inicial, estadoDestinoId);
        Tocar(ahoraUtc);
        return true;
    }

    private void AplicarEstandar(EstadoInicial inicial, Guid? estadoDestinoId)
    {
        Tipo = inicial.Tipo;
        EsPrincipal = inicial.EsPrincipal;
        Orden = inicial.Orden;
        Pasos = inicial.Pasos;
        EstadoDestinoId = estadoDestinoId;
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
