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
/// Por qué no hay bastón conectado (spec 008). <c>Ninguno</c> cuando está conectado, buscando o es un lector que no
/// tiene este concepto (el simulador).
/// </summary>
public enum MotivoSinBaston { Ninguno, NoElegido, SinPermiso, BluetoothApagado, SinBluetooth, NoEmparejado, NoResponde }

/// <summary>
/// El lector de caravanas. El simulador de esta spec y el bastón real de la spec 008 implementan lo mismo, así
/// que cambiar uno por otro no toca el flujo de trabajo (RF-31).
/// </summary>
public interface IRfidReader
{
    EstadoConexionRfid Estado { get; }

    /// <summary>Por qué está sin bastón (solo tiene sentido con <see cref="Estado"/> en Desconectado).</summary>
    MotivoSinBaston Motivo { get; }

    /// <summary>Se dispara cuando cambia <see cref="Estado"/> o <see cref="Motivo"/>. Puede venir de cualquier hilo.</summary>
    event Action? EstadoCambiado;

    /// <summary>Las lecturas a medida que llegan. Termina cuando se cancela el token.</summary>
    IAsyncEnumerable<TagRead> Lecturas(CancellationToken ct);

    /// <summary>Empieza a conectar y vuelve enseguida (la conexión sigue en segundo plano). Idempotente.</summary>
    Task ConectarAsync(CancellationToken ct = default);
    Task DesconectarAsync(CancellationToken ct = default);

    /// <summary>Reintenta ya, saltando la espera entre ciclos (botón "Tocá para reconectar").</summary>
    Task ReconectarAhoraAsync(CancellationToken ct = default);
}

/// <summary>El bastón que la tablet recuerda (nombre y dirección Bluetooth). No identifica a ningún veterinario (spec 006).</summary>
public sealed record BastonRecordado(string Nombre, string Direccion);

/// <summary>Preferencia de la tablet: qué bastón emparejado se usa. Fuera de la base, como la de mangas.</summary>
public interface IPreferenciasBaston
{
    BastonRecordado? Obtener();
    void Guardar(BastonRecordado baston);
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

/// <summary>Preferencias de la tablet (fuera de la base): solo la de mangas por establecimiento (spec 003, RF-43).</summary>
public interface IPreferenciasMangas
{
    /// <summary>
    /// Si en ese establecimiento se eligió llevar el control por manga (contador "7/10" y cierre de mangas), o nulo si nunca se eligió
    /// (en ese caso se usa <see cref="ControlPorMangaPorDefecto"/>). Es una preferencia de cada establecimiento (cada tambo trabaja a su manera).
    /// </summary>
    bool? ObtenerControlPorManga(Guid establecimientoId);
    void GuardarControlPorManga(Guid establecimientoId, bool valor);

    /// <summary>Lo que se usa mientras no se elija: apagado (cada establecimiento lo prende si lo quiere).</summary>
    const bool ControlPorMangaPorDefecto = false;
}

/// <summary>
/// Limpieza de lo que una versión anterior dejó en las preferencias de la tablet (spec 006, RF-21). Es un puerto aparte porque
/// <c>Preferences</c> es de MAUI e Infrastructure no depende de MAUI. Nunca toca la preferencia de mangas.
/// </summary>
public interface ILimpiezaPreferencias
{
    /// <summary>Borra las claves que ya no se usan (el veterinario recordado). Si no se puede, no debe fallar.</summary>
    void EliminarClavesObsoletas();
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

    /// <summary>Se perdió la conexión con el bastón (spec 008, RF-14): una vibración corta, sin sonido.</summary>
    void BastonPerdido();
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

/// <summary>Esperas de vacas (spec 004). No son registros de campo: no entran en el orden compartido ni en el "último vigente".</summary>
public interface IEsperaRepository
{
    /// <summary>La espera pendiente de la vaca (con seguimiento: quien la pide la resuelve o saltea), o null.</summary>
    Task<Espera?> ObtenerPendienteAsync(Guid animalId, CancellationToken ct = default);

    /// <summary>Las esperas pendientes del rodeo, de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<EsperaDto>> ListarPendientesAsync(Guid rodeoId, CancellationToken ct = default);

    void Agregar(Espera espera);
}

public interface IRechazoRepository
{
    Task<Rechazo?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Todos los rechazos de la vaca (también los anulados), del más viejo al más nuevo, sin seguimiento (para la ficha).</summary>
    Task<IReadOnlyList<Rechazo>> ListarPorAnimalAsync(Guid animalId, CancellationToken ct = default);

    void Agregar(Rechazo rechazo);
}

/// <summary>La hora del primer y del último movimiento de un día (nulos si no hubo ninguno).</summary>
public readonly record struct HorarioDeRegistros(DateTime? PrimeroUtc, DateTime? UltimoUtc);

/// <summary>El último registro de campo vigente de una vaca, sin importar de qué tipo sea.</summary>
public sealed record RegistroCampoVigente(
    TipoRegistroCampo Tipo, Guid Id, DateTime FechaHoraUtc, long OrdenInsercion, Guid? EstadoId, Guid? MangaId,
    string? Observaciones = null, Guid? EventoId = null, int? DiasPreniez = null, int? DiasAborto = null);
