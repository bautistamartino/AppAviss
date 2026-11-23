using System.Text.Encodings.Web;
using System.Text.Json;
using Aviss.Domain.Entidades;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Importacion;

/// <summary>
/// Lo que sale de validar una lectura: o un rechazo del archivo, o las filas válidas, las excluidas,
/// las advertencias y el conteo por estado. Todavía no sabe nada del archivo (hash, bytes): eso lo arma el servicio.
/// </summary>
public sealed class ResultadoValidacion
{
    public MotivoRechazoArchivo? Rechazo { get; init; }
    public int FilasLeidas { get; init; }
    public IReadOnlyList<FilaAnimalImportada> Validas { get; init; } = [];
    public IReadOnlyList<FilaExcluida> Excluidas { get; init; } = [];
    public IReadOnlyList<AdvertenciaImportacion> Advertencias { get; init; } = [];
    public IReadOnlyList<ConteoEstado> ConteoPorEstado { get; init; } = [];

    public bool PuedeConfirmar => Rechazo is null && Validas.Count > 0;
}

/// <summary>
/// RF-8 a RF-12: validación fila por fila de una lectura cruda. Función pura (sin archivos, sin base): se prueba
/// armando LecturaExcel a mano.
/// </summary>
public static class ValidadorImportacion
{
    // Relajado: deja pasar ñ, tildes, etc. tal cual en vez de escaparlos como ñ (el JSON se lee mejor en la base).
    private static readonly JsonWriterOptions OpcionesJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ResultadoValidacion Validar(LecturaExcel lectura, IReadOnlyList<EstadoReproductivo> estados)
    {
        // 1. Límites de filas (el perfil ya rechaza el exceso, pero la regla es de negocio: se verifica igual).
        if (lectura.Filas.Count > LimitesImportacion.MaxFilas)
            return new ResultadoValidacion { Rechazo = MotivoRechazoArchivo.ExcedeFilas, FilasLeidas = lectura.Filas.Count };
        if (lectura.Filas.Count == 0)
            return new ResultadoValidacion { Rechazo = MotivoRechazoArchivo.SinFilasDeDatos };

        // 2. ID de cada fila (vacío / inválido / largo). Solo las que pasan participan de la detección de repetidos.
        var excluidas = new Dictionary<int, MotivoFila>();
        var conId = new List<(FilaLeida Fila, Caravana Caravana)>(lectura.Filas.Count);
        foreach (var fila in lectura.Filas)
        {
            fila.Celdas.TryGetValue(lectura.NombreColumnaId, out var celdaId);
            var r = ReglasIdImportacion.Evaluar(celdaId);
            if (r.EsValido) conId.Add((fila, r.Caravana!));
            else excluidas[fila.NumeroFila] = r.Motivo!.Value;
        }

        // 3. Repetidos por Caravana.Clave (misma regla que la base): TODAS las filas del ID repetido quedan afuera.
        var repetidos = conId.GroupBy(x => x.Caravana.Clave).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
        foreach (var (fila, _) in repetidos)
            excluidas[fila.NumeroFila] = MotivoFila.IdRepetido;

        // 4. Las que quedan: estado (RPRO) y JSON de originales.
        var estadosPorCodigo = IndexarEstadosActivos(estados);
        var validas = new List<FilaAnimalImportada>(conId.Count);
        var desconocidos = new Dictionary<string, (string Valor, List<int> Filas)>();   // por clave, en orden de aparición

        foreach (var (fila, caravana) in conId)
        {
            if (excluidas.ContainsKey(fila.NumeroFila)) continue;

            Guid? estadoId = null;
            if (fila.Celdas.TryGetValue(lectura.NombreColumnaEstado, out var celdaRpro) && !celdaRpro.EsVacia)
            {
                var valor = celdaRpro.Texto.Trim();
                var clave = Caravana.CalcularClave(valor);
                if (estadosPorCodigo.TryGetValue(clave, out var id))
                    estadoId = id;
                else
                {
                    if (!desconocidos.TryGetValue(clave, out var grupo))
                        desconocidos[clave] = grupo = (valor, new List<int>());
                    grupo.Filas.Add(fila.NumeroFila);
                }
            }

            validas.Add(new FilaAnimalImportada(fila.NumeroFila, caravana, estadoId, ArmarJson(lectura.Encabezados, fila)));
        }

        return new ResultadoValidacion
        {
            FilasLeidas = lectura.Filas.Count,
            Validas = validas,
            Excluidas = excluidas.OrderBy(kv => kv.Key).Select(kv => new FilaExcluida(kv.Key, kv.Value)).ToList(),
            Advertencias = ArmarAdvertencias(lectura, desconocidos.Values.ToList()),
            ConteoPorEstado = ContarPorEstado(validas, estados)
        };
    }

    /// <summary>
    /// Código de Dairy Comp (sin distinguir mayúsculas, sin normalizar acentos) → estado ACTIVO. Los dados de baja
    /// no entran, así que sus códigos cuentan como desconocidos (RF-11). Ante dos activos iguales gana el primero.
    /// Spec 005: los eventos de tipo SoloEvento (OK, Aborto, Revisar Nuevamente) tampoco entran: no son un estado
    /// reproductivo, así que un RPRO igual a su código entra sin estado y con advertencia, como un código desconocido.
    /// </summary>
    private static Dictionary<string, Guid> IndexarEstadosActivos(IReadOnlyList<EstadoReproductivo> estados)
    {
        var mapa = new Dictionary<string, Guid>();
        foreach (var e in estados.Where(e => e.Activo && e.Tipo == TipoEvento.Estado))
            mapa.TryAdd(Caravana.CalcularClave(e.CodigoDairyComp.Trim()), e.Id);
        return mapa;
    }

    /// <summary>
    /// Objeto JSON { "encabezado original": "texto de la celda" } en el orden de columnas del archivo.
    /// Las celdas vacías se omiten; las columnas desconocidas se incluyen; ningún valor se interpreta (RF-12).
    /// </summary>
    private static string ArmarJson(IReadOnlyList<string> encabezados, FilaLeida fila)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, OpcionesJson))
        {
            w.WriteStartObject();
            foreach (var encabezado in encabezados)
            {
                if (fila.Celdas.TryGetValue(encabezado, out var celda) && !celda.EsVacia)
                    w.WriteString(encabezado, celda.Texto);
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static List<AdvertenciaImportacion> ArmarAdvertencias(
        LecturaExcel lectura, List<(string Valor, List<int> Filas)> desconocidos)
    {
        var lista = new List<AdvertenciaImportacion>();

        foreach (var columna in lectura.ColumnasOpcionalesFaltantes)
            lista.Add(new AdvertenciaImportacion(
                TipoAdvertencia.ColumnaOpcionalFaltante,
                $"Falta la columna opcional {columna}; se importa igual.", columna, 0, []));

        if (lectura.TotalDeclarado is { } total && total != lectura.Filas.Count)
            lista.Add(new AdvertenciaImportacion(
                TipoAdvertencia.TotalNoCoincide,
                $"La fila Total indica {total} filas pero se leyeron {lectura.Filas.Count}.", null, 0, []));

        foreach (var (valor, filas) in desconocidos)
            lista.Add(new AdvertenciaImportacion(
                TipoAdvertencia.EstadoDesconocido,
                $"El estado \"{valor}\" no está en el catálogo: {filas.Count} fila(s) se importan sin estado.",
                valor, filas.Count, filas.Take(LimitesImportacion.MaxEjemplosAdvertencia).ToList()));

        return lista;
    }

    /// <summary>Estados en el orden del catálogo (solo los que tienen vacas) y al final "sin estado".</summary>
    private static List<ConteoEstado> ContarPorEstado(List<FilaAnimalImportada> validas, IReadOnlyList<EstadoReproductivo> estados)
    {
        var conteo = new List<ConteoEstado>();
        foreach (var estado in estados)
        {
            var n = validas.Count(v => v.EstadoId == estado.Id);
            if (n > 0) conteo.Add(new ConteoEstado(estado.Id, estado.Nombre, n));
        }
        var sinEstado = validas.Count(v => v.EstadoId is null);
        if (sinEstado > 0) conteo.Add(new ConteoEstado(null, "Sin estado", sinEstado));
        return conteo;
    }
}
