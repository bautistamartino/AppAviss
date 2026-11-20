using Aviss.Application.Campo;
using Aviss.Domain.Entidades;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Puertos;

// Puertos del flujo de campo (spec 003). Application define las interfaces; Infrastructure, Aviss.Rfid y la app
// (plataforma) las implementan. Nada de esto referencia MAUI.

// ---------------------------------------------------------------------------------------------------------------
// Lector RFID (RF-31)
// ---------------------------------------------------------------------------------------------------------------

/// <summary>Un código entregado por el lector, con la hora (UTC) en que llegó.</summary>
public sealed record TagRead(string Codigo, DateTime FechaHoraUtc);

public enum EstadoConexionRfid { Desconectado, Conectando, Conectado }

/// <summary>
/// El lector de caravanas. El simulador de esta spec y el bastón real de la spec posterior implementan lo mismo, así
/// que cambiar uno por otro no toca el flujo de trabajo (RF-31).
/// </summary>
public interface IRfidReader
{
    EstadoConexionRfid Estado { get; }

    /// <summary>Las lecturas a medida que llegan. Termina cuando se cancela el token.</summary>
    IAsyncEnumerable<TagRead> Lecturas(CancellationToken ct);

    Task ConectarAsync(CancellationToken ct = default);
    Task DesconectarAsync(CancellationToken ct = default);
}

/// <summary>Solo existe en la compilación de desarrollo: permite "leer" una caravana desde la pantalla (RF-31).</summary>
public interface ISimuladorLecturas
{
    void Emitir(string codigo);
}

// ---------------------------------------------------------------------------------------------------------------
// Servicios de plataforma que la lógica necesita pero no implementa
// ---------------------------------------------------------------------------------------------------------------

/// <summary>La zona horaria del dispositivo, consultada en cada cálculo: un cambio de zona se nota sin reiniciar (RF-30).</summary>
public interface IZonaHoraria
{
    TimeZoneInfo Zona { get; }
}

/// <summary>Preferencias de la tablet (fuera de la base, como el identificador del dispositivo): el veterinario recordado (RF-1).</summary>
public interface IPreferenciasDispositivo
{
    Guid? ObtenerVeterinarioRecordado();
    void GuardarVeterinarioRecordado(Guid veterinarioId);

    /// <summary>
    /// Si el veterinario eligió llevar el control por manga (contador "7/10" y cierre de mangas), o nulo si nunca eligió
    /// (en ese caso se usa <see cref="ControlPorMangaPorDefecto"/>). Es una preferencia de cada veterinario.
    /// </summary>
    bool? ObtenerControlPorManga(Guid veterinarioId);
    void GuardarControlPorManga(Guid veterinarioId, bool valor);

    /// <summary>Lo que se usa mientras el veterinario no elija: apagado (cada uno lo prende si lo quiere).</summary>
    const bool ControlPorMangaPorDefecto = false;
}

/// <summary>Vibración y sonido simples del sistema, sin voz (RF-16, RF-22).</summary>
public interface IAvisosCampo
{
    /// <summary>Vibración corta: algo se guardó.</summary>
    void Confirmacion();

    /// <summary>Sonido o vibración de "animal repetido / ya pasó".</summary>
    void Repetido();

    /// <summary>La chicharra de "no entra en tacto" (CVET 0): fuerte y corta, para que se oiga sin mirar la pantalla.</summary>
    void SinTacto();

    /// <summary>La manga llegó a su cantidad y se cerró sola: un aviso corto y positivo, distinto de la chicharra.</summary>
    void MangaCompleta();
}

/// <summary>Mantener la pantalla encendida mientras se trabaja (RF-3).</summary>
public interface IPantalla
{
    void MantenerEncendida(bool encendida);
}

/// <summary>Espera un tiempo. Existe como puerto para que los tests no esperen de verdad (aviso de 5 segundos, refrescos).</summary>
public interface ITemporizador
{
    Task EsperarAsync(TimeSpan tiempo, CancellationToken ct);
}

// ---------------------------------------------------------------------------------------------------------------
// Repositorios nuevos (spec 003). Como los demás, trabajan dentro de la transacción de IUnitOfWork.
// ---------------------------------------------------------------------------------------------------------------

public interface IMangaRepository
{
    Task<Manga?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>La manga del rodeo sin cierre vigente (la de mayor número si hubiera más de una), o null si no hay.</summary>
    Task<Manga?> ObtenerAbiertaAsync(Guid rodeoId, CancellationToken ct = default);

    /// <summary>Número de la manga que sigue en el rodeo: el mayor número usado más uno (1 si no hay ninguna).</summary>
    Task<int> SiguienteNumeroAsync(Guid rodeoId, CancellationToken ct = default);

    Task<CierreManga?> ObtenerCierreAsync(Guid cierreId, CancellationToken ct = default);

    /// <summary>El último cierre vigente (no anulado) del rodeo, por fecha y hora.</summary>
    Task<CierreManga?> ObtenerUltimoCierreVigenteDelRodeoAsync(Guid rodeoId, CancellationToken ct = default);

    /// <summary>Cantidad de vacas distintas con marca vigente en la manga: el contador (RF-5).</summary>
    Task<int> ContarVacasAsync(Guid mangaId, CancellationToken ct = default);

    /// <summary>La hora del último "Terminar el tacto" vigente (no anulado) del rodeo dentro del rango, o nulo si no hubo.</summary>
    Task<DateTime?> ObtenerFechaTerminoElTactoAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default);

    /// <summary>Caravanas con marca vigente en la manga, en el orden en que se marcaron.</summary>
    Task<IReadOnlyList<string>> ListarCaravanasMarcadasAsync(Guid mangaId, CancellationToken ct = default);

    void Agregar(Manga manga);
    void AgregarCierre(CierreManga cierre);
}

public interface IMarcaPasadaRepository
{
    Task<MarcaPasada?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>La marca vigente más reciente de la vaca dentro del rango (el día local de hoy), o null.</summary>
    Task<MarcaPasada?> ObtenerVigenteEnRangoAsync(Guid animalId, RangoUtc rango, CancellationToken ct = default);

    /// <summary>La marca vigente de la vaca en esa manga, sin importar el día, o null. La base no admite dos.</summary>
    Task<MarcaPasada?> ObtenerVigenteEnMangaAsync(Guid animalId, Guid mangaId, CancellationToken ct = default);

    /// <summary>Marcas vigentes de la vaca (en cualquier manga).</summary>
    Task<IReadOnlyList<MarcaPasada>> ListarVigentesDeAnimalAsync(Guid animalId, CancellationToken ct = default);

    void Agregar(MarcaPasada marca);
}

/// <summary>
/// Consultas que miran a la vez todos los tipos de registro de campo (hoy tacto y marca de pasada). La spec 004 suma
/// sus tipos acá: así el "último registro vigente" y el orden de guardado se resuelven en un solo lugar.
/// </summary>
public interface IRegistroCampoRepository
{
    /// <summary>
    /// El registro de campo vigente más reciente de la vaca (fecha y hora; con empate, el guardado después), o null.
    /// Los tactos de anulación y los anulados no cuentan.
    /// </summary>
    Task<RegistroCampoVigente?> ObtenerUltimoVigenteAsync(Guid animalId, CancellationToken ct = default);

    /// <summary>Máximo orden de guardado entre todos los tipos de registro más uno; se llama dentro de la transacción (RF-21).</summary>
    Task<long> SiguienteOrdenAsync(CancellationToken ct = default);

    /// <summary>¿Tiene la vaca un tacto normal vigente dentro del rango?</summary>
    Task<bool> TieneTactoVigenteEnRangoAsync(Guid animalId, RangoUtc rango, CancellationToken ct = default);

    /// <summary>Vacas no anuladas del rodeo con una marca o un tacto vigente dentro del rango (procesadas hoy).</summary>
    Task<int> ContarProcesadasAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default);

    /// <summary>La hora del primer y del último movimiento de hoy en el rodeo (marca o tacto normal vigente de vacas no anuladas).</summary>
    Task<HorarioDeRegistros> ObtenerHorarioAsync(Guid rodeoId, RangoUtc rango, CancellationToken ct = default);
}

/// <summary>La hora del primer y del último movimiento de un día (nulos si no hubo ninguno).</summary>
public readonly record struct HorarioDeRegistros(DateTime? PrimeroUtc, DateTime? UltimoUtc);

/// <summary>El último registro de campo vigente de una vaca, sin importar de qué tipo sea.</summary>
public sealed record RegistroCampoVigente(
    TipoRegistroCampo Tipo, Guid Id, DateTime FechaHoraUtc, long OrdenInsercion, Guid UsuarioId, Guid? EstadoId, Guid? MangaId);
