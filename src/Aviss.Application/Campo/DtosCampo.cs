using Aviss.Domain.Enums;

namespace Aviss.Application.Campo;

// Lo que los servicios de campo le devuelven a las pantallas (spec 003). Son datos de solo lectura, sin lógica.
// Las fechas van en UTC (las pantallas las pasan a hora local), salvo las del historial de la ficha, que ya vienen en
// hora local porque algunas son fechas sin hora (las del Excel).

/// <summary>Los tipos de registro de campo (la spec 004 agrega tratamiento y rechazo).</summary>
public enum TipoRegistroCampo { Tacto, Marca }

/// <summary>Qué se hace con una lectura (RF-11, RF-12, RF-22, RF-24).</summary>
public enum TipoResultadoLectura
{
    /// <summary>La vaca se encontró y quedó marcada (se muestra su ficha).</summary>
    Marcada,
    /// <summary>La vaca ya estaba procesada hoy en la manga actual: "Animal repetido".</summary>
    Repetido,
    /// <summary>La vaca ya estaba procesada hoy en otra manga, o con un tacto sin marca: "Ya pasó".</summary>
    YaPaso,
    /// <summary>La vaca quedó marcada y suma al conteo, pero su CVET es 0: no entra en tacto. No trae ficha y suena la chicharra.</summary>
    SinTacto,
    /// <summary>No existe en el rodeo: se ofrece buscar a mano; no se guardó nada.</summary>
    NoEncontrada
}

/// <summary>Cómo se presenta el aviso de una vaca ya procesada (lo decide <see cref="ReglasAviso"/>).</summary>
public enum TipoAviso { Repetido, YaPasoOtraManga, YaPasoSinManga }

/// <summary>Qué se deshace (RF-26 a RF-28). El <c>Id</c> del comando es el del registro.</summary>
public enum TipoDeshacer { Tacto, Marca, Cierre }

public enum TipoMovimiento { Tacto, Marca, Cierre }

/// <summary>Para el panel del simulador (solo desarrollo): qué caravana de muestra pedir.</summary>
public enum MuestraCaravana { Existente, YaLeida }

public sealed record CampoFichaDto(string Nombre, string Valor);

public enum OrigenEvento { Excel, Aviss }

/// <summary>
/// Una línea del historial de la ficha (RF-14). <see cref="FechaLocal"/> es nula cuando la fecha del Excel no se pudo
/// interpretar: entonces <see cref="TextoFecha"/> trae el texto tal cual y el evento va al final, sin ordenar.
/// <see cref="DiasTranscurridos"/> son los días desde ese día hasta hoy (negativo si es una fecha futura, como la
/// fecha probable de parto).
/// </summary>
public sealed record EventoFichaDto(
    OrigenEvento Origen, string Titulo, DateTime? FechaLocal, string? TextoFecha, int? DiasTranscurridos, bool Anulado);

/// <param name="Inseminaciones"><c>VINS</c>: cantidad de inseminaciones (no es una fecha). Vacío si no hay dato.</param>
/// <param name="UltimoToro"><c>UTORO</c>: el último toro, sin fecha. Vacío si no hay dato.</param>
public sealed record FichaAnimalDto(
    Guid AnimalId,
    string Caravana,
    string? IdElectronico,
    Guid? EstadoId,
    string? EstadoNombre,
    DateTime? FechaUltimoTactoUtc,
    IReadOnlyList<CampoFichaDto> Campos,
    string Inseminaciones,
    string UltimoToro,
    IReadOnlyList<EventoFichaDto> Historial,
    string Cvet = "",
    bool EntraEnTacto = true,
    IReadOnlyList<CampoFichaDto>? Extras = null);

/// <summary>El último registro vigente de una vaca, para el aviso "Ya pasó / Animal repetido" (RF-22).</summary>
public sealed record UltimoRegistroDto(
    TipoRegistroCampo Tipo, Guid RegistroId, DateTime FechaHoraUtc, string? EstadoNombre, string Veterinario, int? NumeroManga);

/// <summary>Lo que devuelve el procesamiento de una lectura (RF-11, RF-12, RF-22, RF-24).</summary>
/// <param name="Ficha">La ficha, solo cuando la vaca quedó marcada. En un aviso se pide con <c>ObtenerFichaAsync</c> ("Ver ficha").</param>
/// <param name="MarcaId">Id de la marca recién guardada (para el "Deshacer" de 5 segundos). Solo si quedó marcada.</param>
/// <param name="Aviso">Qué aviso mostrar. Solo en Repetido y YaPaso.</param>
/// <param name="PuedeCorregir">"Corregir" se ofrece solo cuando el último registro vigente no es la marca de pasada (RF-22).</param>
public sealed record ResultadoLecturaDto(
    TipoResultadoLectura Tipo,
    string CodigoLeido,
    Guid? AnimalId,
    string? Caravana,
    FichaAnimalDto? Ficha,
    Guid? MarcaId,
    int? NumeroManga,
    int? ContadorManga,
    TipoAviso? Aviso,
    UltimoRegistroDto? UltimoRegistro,
    bool PuedeCorregir);

/// <param name="PrimeraVacaUtc">Hora de la primera vaca de hoy (marca o tacto vigente), o nulo si todavía no hubo ninguna.</param>
/// <param name="FinUtc">Hora del último movimiento de hoy o, si es posterior, de "Terminar el tacto"; nulo si no hubo nada.</param>
/// <param name="Terminado">El fin es la hora en que se apretó "Terminar el tacto" (y no la de la última vaca).</param>
public sealed record AvanceRodeoDto(
    DateOnly Fecha, int TotalVacas, int Procesadas, int Pendientes, int Porcentaje,
    DateTime? PrimeraVacaUtc = null, DateTime? FinUtc = null, bool Terminado = false);

/// <summary>El estado del trabajo en un rodeo: la manga (abierta o por empezar), su contador y el avance del día (RF-3, RF-4, RF-30).</summary>
/// <param name="MangaAbierta">False = manga por empezar ("Manga N · 0"): la primera marca la abre.</param>
public sealed record EstadoTrabajoDto(
    Guid RodeoId, int NumeroManga, bool MangaAbierta, Guid? MangaId, int Contador, AvanceRodeoDto Avance);

public sealed record ResumenMangaDto(Guid MangaId, int NumeroManga, int Contador, IReadOnlyList<string> Caravanas);

/// <summary>Un cierre de manga con las dos cifras (RF-6, RF-8): la que se guardó al cerrar y el contador de hoy.</summary>
public sealed record CierreMangaDto(
    Guid CierreId,
    Guid MangaId,
    int NumeroManga,
    TipoCierreManga Tipo,
    DateTime FechaHoraUtc,
    int CantidadAviss,
    int? CantidadConfirmada,
    int? Diferencia,
    int ContadorActual);

/// <summary>Resultado de "Finalizar manga" o "Terminar el tacto". <see cref="Cierre"/> es nulo si no había manga abierta (solo en Terminar).</summary>
public sealed record ResultadoCierreDto(CierreMangaDto? Cierre, AvanceRodeoDto Avance);


/// <param name="ContadorManga">Contador de la manga después de deshacer una marca.</param>
public sealed record ResultadoDeshacerDto(
    TipoDeshacer Tipo, Guid? AnimalId, Guid? MangaId, int? NumeroManga, int? ContadorManga);

/// <param name="RegistroId">Id del tacto, de la marca o del cierre: es lo que se manda a deshacer.</param>
/// <param name="Detalle">Estado del tacto (nombre) o tipo de cierre.</param>
/// <param name="CantidadAviss">Solo en cierres: lo que contaba Aviss al cerrar, lo confirmado y el contador de hoy (RF-8).</param>
public sealed record MovimientoDto(
    TipoMovimiento Tipo,
    Guid RegistroId,
    Guid? AnimalId,
    string? Caravana,
    int? NumeroManga,
    string? Detalle,
    DateTime FechaHoraUtc,
    string Veterinario,
    bool Anulado,
    bool PuedeDeshacer,
    int? CantidadAviss,
    int? CantidadConfirmada,
    int? ContadorActual);

public sealed record RodeoParaTrabajarDto(
    Guid EstablecimientoId,
    string EstablecimientoNombre,
    Guid RodeoId,
    string RodeoNombre,
    DateTime FechaImportacionUtc,
    EstadoRodeo Estado,
    AvanceRodeoDto Avance);

public sealed record VeterinarioDto(Guid Id, string Nombre, string? Matricula);

public sealed record EstadoActivoDto(Guid Id, string Nombre);
