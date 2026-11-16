using System.Globalization;
using System.Text.RegularExpressions;
using Aviss.Application.Importacion;
using NPOI;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;

namespace Aviss.Excel;

/// <summary>
/// Perfil del Excel que exporta Dairy Comp (.xls, formato binario viejo de Excel). Solo entrega una LECTURA CRUDA:
/// encabezados, filas con celdas tipadas y el total declarado. Las reglas de negocio (ID válido, repetidos, RPRO) viven
/// en Application. Nunca lanza: toda excepción de NPOI se convierte en un rechazo controlado, sin mensajes técnicos
/// ni datos del archivo (RF-24).
/// </summary>
public sealed class PerfilDairyComp : IExcelProfile
{
    public const string NombrePerfil = "Dairy Comp";

    // Columnas opcionales que Dairy Comp suele exportar (RF-4); si faltan se avisa pero se importa igual.
    private static readonly string[] ColumnasOpcionales = ["CVET", "FPART", "LACT", "DEL", "FCELO", "VINS", "UTORO", "COM", "FAPAR"];

    // Todo libro .xls (y también un .xlsx con contraseña) es un contenedor OLE2 que empieza con estos 8 bytes.
    private static readonly byte[] FirmaOle2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const ushort RegistroFilePass = 0x002F; // marca de "libro .xls cifrado con contraseña"

    // "Total:N" con variantes ("TOTAL : 5", "total:5"). Solo dígitos ASCII.
    private static readonly Regex PatronTotal = new(@"^\s*total\s*:\s*([0-9]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string Nombre => NombrePerfil;

    public ResultadoLectura Leer(byte[] contenido, int maxFilas)
    {
        try
        {
            return LeerInterno(contenido, maxFilas);
        }
        catch (Exception ex) when (EsDeContrasena(ex))
        {
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Protegido);
        }
        catch (Exception)
        {
            // Cualquier otra falla de NPOI (archivo dañado, estructura inesperada...) es "corrupto". No se propaga el
            // mensaje de la excepción: podría traer datos del archivo.
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Corrupto);
        }
    }

    private static ResultadoLectura LeerInterno(byte[] contenido, int maxFilas)
    {
        if (contenido is null || contenido.Length == 0)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Vacio);

        // El formato se detecta por el CONTENIDO, no por la extensión: un .xlsx renombrado a .xls empieza con "PK" (zip).
        if (!EmpiezaConFirmaOle2(contenido))
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.FormatoNoSoportado);

        contenido = CompletarUltimoSector(contenido, out bool cabeceraInvalida);
        if (cabeceraInvalida)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Corrupto);

        using var memoria = new MemoryStream(contenido, writable: false); // solo lectura: el original no se toca
        var sistemaArchivos = new POIFSFileSystem(memoria);

        // Un .xlsx guardado con contraseña también es un contenedor OLE2, pero con estas dos entradas.
        var raiz = sistemaArchivos.Root;
        if (raiz.HasEntry("EncryptedPackage") || raiz.HasEntry("EncryptionInfo"))
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Protegido);

        // Un .xls de verdad guarda su contenido en "Workbook" (Excel 97+) o "Book" (Excel 5). Otro OLE2 (.doc, .ppt) no.
        string? nombreFlujo = raiz.HasEntry("Workbook") ? "Workbook" : raiz.HasEntry("Book") ? "Book" : null;
        if (nombreFlujo is null)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.FormatoNoSoportado);

        if (TieneRegistroFilePass(sistemaArchivos, nombreFlujo))
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.Protegido);

        var libro = new HSSFWorkbook(sistemaArchivos);
        if (libro.NumberOfSheets == 0)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.SinHojas);

        return LeerHoja(libro.GetSheetAt(0), maxFilas);
    }

    private static ResultadoLectura LeerHoja(ISheet hoja, int maxFilas)
    {
        // 1) Encabezados: la primera fila con contenido (RF-3).
        int filaEncabezados = -1;
        for (int i = hoja.FirstRowNum; i <= hoja.LastRowNum && filaEncabezados < 0; i++)
        {
            var fila = hoja.GetRow(i);
            if (fila is not null && TieneContenido(fila)) filaEncabezados = i;
        }
        if (filaEncabezados < 0)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.FaltaColumna, "ID");

        var encabezados = new List<(string Nombre, int Columna)>();
        var filaEnc = hoja.GetRow(filaEncabezados);
        for (int c = Math.Max(0, (int)filaEnc.FirstCellNum); c < filaEnc.LastCellNum; c++)
        {
            var celda = Convertir(filaEnc.GetCell(c));
            if (celda is null || celda.EsVacia) continue; // encabezado vacío: esa columna se ignora
            encabezados.Add((celda.Texto.Trim(), c));
        }

        string? nombreId = encabezados.FirstOrDefault(e => Igual(e.Nombre, "ID")).Nombre;
        if (nombreId is null) return ResultadoLectura.Rechazado(MotivoRechazoArchivo.FaltaColumna, "ID");
        string? nombreEstado = encabezados.FirstOrDefault(e => Igual(e.Nombre, "RPRO")).Nombre;
        if (nombreEstado is null) return ResultadoLectura.Rechazado(MotivoRechazoArchivo.FaltaColumna, "RPRO");

        var repetido = encabezados.GroupBy(e => e.Nombre, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
            return ResultadoLectura.Rechazado(MotivoRechazoArchivo.EncabezadoDuplicado, repetido.First().Nombre);

        var opcionalesFaltantes = ColumnasOpcionales
            .Where(opcional => !encabezados.Any(e => Igual(e.Nombre, opcional)))
            .ToList();

        var columnaPorIndice = encabezados.ToDictionary(e => e.Columna, e => e.Nombre);

        // 2) Filas de datos: se saltean las totalmente vacías y se separa la fila "Total:N" (RF-5, RF-6).
        var filas = new List<FilaLeida>();
        int? totalDeclarado = null;

        for (int i = filaEncabezados + 1; i <= hoja.LastRowNum; i++)
        {
            var fila = hoja.GetRow(i);
            if (fila is null) continue;

            var celdas = new Dictionary<string, CeldaLeida>(StringComparer.Ordinal);
            CeldaLeida? primeraConContenido = null;
            bool hayContenido = false;

            for (int c = Math.Max(0, (int)fila.FirstCellNum); c < fila.LastCellNum; c++)
            {
                var celda = Convertir(fila.GetCell(c));
                if (celda is null) continue; // celda en blanco: no se guarda (sin entrada = vacía)

                if (!celda.EsVacia)
                {
                    hayContenido = true;
                    primeraConContenido ??= celda;
                }
                if (columnaPorIndice.TryGetValue(c, out var nombre))
                    celdas[nombre] = celda;
            }

            if (!hayContenido) continue; // fila totalmente vacía

            if (primeraConContenido is { Tipo: TipoCelda.Texto } primera && EsFilaTotal(primera.Texto, out int total))
            {
                totalDeclarado = total; // si hay varias, queda la última
                continue;
            }

            if (filas.Count >= maxFilas)
                return ResultadoLectura.Rechazado(MotivoRechazoArchivo.ExcedeFilas);

            filas.Add(new FilaLeida(i + 1, celdas)); // número de fila de Excel, 1 = primera
        }

        return ResultadoLectura.Correcto(new LecturaExcel(
            encabezados.Select(e => e.Nombre).ToList(),
            nombreId,
            nombreEstado,
            opcionalesFaltantes,
            filas,
            totalDeclarado));
    }

    // ---- Detección de formato y contraseña ----

    /// <summary>
    /// Un contenedor OLE2 siempre ocupa sectores completos (512 bytes, o 4096 en la versión 4). Si el archivo quedó cortado
    /// a mitad de un sector (una copia interrumpida), NPOI puede quedarse dando vueltas para siempre en vez de fallar
    /// (comprobado con la muestra). Se completa con ceros hasta el sector entero, sobre una copia: así NPOI falla rápido
    /// con "corrupto", y un archivo legítimo cuyo último sector venga sin rellenar se sigue leyendo. El original no se toca.
    /// </summary>
    private static byte[] CompletarUltimoSector(byte[] contenido, out bool cabeceraInvalida)
    {
        cabeceraInvalida = false;
        if (contenido.Length < 512) { cabeceraInvalida = true; return contenido; } // ni siquiera entra la cabecera

        int desplazamiento = BitConverter.ToUInt16(contenido, 30); // el tamaño del sector es 2^desplazamiento
        if (desplazamiento is not (9 or 12)) { cabeceraInvalida = true; return contenido; }

        int sector = 1 << desplazamiento;
        int resto = contenido.Length % sector;
        if (resto == 0) return contenido;

        var completo = new byte[contenido.Length + (sector - resto)];
        contenido.CopyTo(completo, 0);
        return completo;
    }

    private static bool EmpiezaConFirmaOle2(byte[] contenido)
        => contenido.Length >= FirmaOle2.Length && contenido.AsSpan(0, FirmaOle2.Length).SequenceEqual(FirmaOle2);

    /// <summary>
    /// Un .xls cifrado lleva un registro FILEPASS justo después del primer registro (BOF). Se mira a mano en los
    /// primeros bytes en vez de depender de lo que haga NPOI al abrir un archivo cifrado.
    /// </summary>
    private static bool TieneRegistroFilePass(POIFSFileSystem sistemaArchivos, string nombreFlujo)
    {
        using var flujo = sistemaArchivos.CreateDocumentInputStream(nombreFlujo);
        var cabecera = new byte[64];
        int leidos = flujo.Read(cabecera, 0, cabecera.Length);
        if (leidos < 8) return false;

        int largoBof = BitConverter.ToUInt16(cabecera, 2) + 4; // 2 bytes de tipo + 2 de largo + datos
        if (largoBof + 2 > leidos) return false;
        return BitConverter.ToUInt16(cabecera, largoBof) == RegistroFilePass;
    }

    private static bool EsDeContrasena(Exception ex) => ex is EncryptedDocumentException;

    // ---- Celdas ----

    private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool EsFilaTotal(string texto, out int total)
    {
        total = 0;
        var m = PatronTotal.Match(texto);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out total);
    }

    private static bool TieneContenido(IRow fila)
    {
        for (int c = Math.Max(0, (int)fila.FirstCellNum); c < fila.LastCellNum; c++)
        {
            var celda = Convertir(fila.GetCell(c));
            if (celda is not null && !celda.EsVacia) return true;
        }
        return false;
    }

    /// <summary>Clasifica una celda por su tipo y arma su texto. Devuelve null si la celda no existe o está en blanco.</summary>
    private static CeldaLeida? Convertir(ICell? celda)
    {
        if (celda is null) return null;

        switch (celda.CellType)
        {
            case CellType.Blank:
                return null;

            case CellType.String:
                return CeldaLeida.DeTexto(celda.StringCellValue ?? "");

            case CellType.Numeric:
                // Una fecha en Excel es un número con formato de fecha. No se usa DataFormatter de NPOI: en NPOI 2.8 depende
                // de SkiaSharp, que el paquete NO instala (comprobado en T2). Se escribe día/mes/año fijo (con hora solo si la hay).
                return DateUtil.IsCellDateFormatted(celda)
                    ? CeldaLeida.De(TipoCelda.Fecha, TextoDeFecha(celda))
                    : CeldaLeida.DeNumero(celda.NumericCellValue);

            case CellType.Boolean:
                return CeldaLeida.De(TipoCelda.Booleana, celda.BooleanCellValue ? "TRUE" : "FALSE");

            case CellType.Error:
                return CeldaLeida.De(TipoCelda.Error, TextoDeError(celda));

            case CellType.Formula:
                return CeldaLeida.De(TipoCelda.Formula, TextoDeFormula(celda));

            default:
                return null;
        }
    }

    private static string TextoDeFecha(ICell celda)
    {
        try
        {
            var fecha = celda.DateCellValue;
            if (fecha is null) return celda.NumericCellValue.ToString("R", CultureInfo.InvariantCulture);
            return fecha.Value.TimeOfDay == TimeSpan.Zero
                ? fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : fecha.Value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (Exception) { return celda.NumericCellValue.ToString("R", CultureInfo.InvariantCulture); }
    }

    private static string TextoDeError(ICell celda)
    {
        try { return FormulaError.ForInt(celda.ErrorCellValue).String; }
        catch (Exception) { return "#ERROR"; }
    }

    /// <summary>Texto del último valor calculado de una fórmula (no se evalúa nada).</summary>
    private static string TextoDeFormula(ICell celda)
    {
        try
        {
            return celda.CachedFormulaResultType switch
            {
                CellType.String => celda.StringCellValue ?? "",
                CellType.Numeric => celda.NumericCellValue.ToString("R", CultureInfo.InvariantCulture),
                CellType.Boolean => celda.BooleanCellValue ? "TRUE" : "FALSE",
                CellType.Error => TextoDeError(celda),
                _ => ""
            };
        }
        catch (Exception) { return ""; }
    }
}
