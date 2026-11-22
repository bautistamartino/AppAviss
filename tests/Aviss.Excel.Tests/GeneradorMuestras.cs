using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Aviss.Excel.Tests;

/// <summary>Celda de fórmula para armar archivos de prueba (el valor se calcula al abrirlo; aquí solo importa el tipo).</summary>
public sealed record FormulaExcel(string Formula);

/// <summary>Celda de error de Excel (#N/A) para armar archivos de prueba.</summary>
public sealed record ErrorExcel;

/// <summary>
/// Arma en memoria los archivos de muestra. NUNCA se usa un Excel real de un tambo (constitución 6): todo es inventado
/// con una semilla fija, así que el resultado es siempre el mismo y se puede comparar con lo commiteado en
/// <c>tests/muestras/</c>. Imita la estructura de Dairy Comp: mismas columnas, fechas como texto con espacios y una fila
/// final "Total:N".
/// </summary>
public static class GeneradorMuestras
{
    public static readonly string[] EncabezadosDairyComp =
        ["ID", "CVET", "FPART", "LACT", "DEL", "FCELO", "VINS", "UTORO", "COM", "RPRO", "FAPAR"];

    private static readonly string[] Estados = ["PREÑADA", "INSEMIN", "SECA", "VACIA", "FRESCA", "NOINSEM", ""];

    // ---- Muestras que se commitean en tests/muestras/ (nombre de archivo -> contenido) ----

    public static IReadOnlyDictionary<string, byte[]> TodasLasMuestras() => new Dictionary<string, byte[]>
    {
        ["dairy-comp-226-vacas.xls"] = Muestra226Vacas(),
        ["filas-con-problemas.xls"] = FilasConProblemas(),
        ["vacio.xls"] = [],
        ["corrupto.xls"] = Corrupto(),
        ["xlsx-renombrado.xls"] = XlsxRenombrado(),
        ["con-contrasena.xls"] = ConContrasena(),
        ["xlsx-con-contrasena.xls"] = XlsxConContrasena(),
        ["sin-hojas.xls"] = SinHojas(),
        ["sin-columna-id.xls"] = SinColumna("ID"),
        ["sin-columna-rpro.xls"] = SinColumna("RPRO"),
        ["encabezado-duplicado.xls"] = EncabezadoDuplicado(),
        ["solo-encabezados.xls"] = SoloEncabezados(),
        ["texto-plano.xls"] = System.Text.Encoding.UTF8.GetBytes("esto no es un excel\n"),
        ["dairy-comp-282-vacas.csv"] = Csv282Vacas(),
    };

    /// <summary>
    /// Imita un listado de Dairy Comp guardado como CSV (formato visto en un archivo real, con todos los datos inventados):
    /// separador ";", encabezados entre comillas, campos con espacios de relleno, un ";" de más al final de cada fila,
    /// Windows-1252 (la Ñ de PREÑADA es un solo byte), "-" en las fechas vacías, CVET con códigos de texto o "0" y la fila
    /// final "Total:;282" con el número en la celda de al lado.
    /// </summary>
    public static byte[] Csv282Vacas()
    {
        const int cantidad = 282;
        string[] toros = ["TOROUNO", "TORODOS", "TOROTRES", "TOROCUATRO", "TOROCINCO"];
        var azar = new Random(20261122); // semilla fija
        var sb = new System.Text.StringBuilder();
        sb.Append("\"ID\";\"CVET\";\"LACT\";\"DEL\";\"FPART\";\"FCELO\";\"UTORO\";\"DDUC\";\"DCC\";\"RPRO\";\"FAPAR\";\"LECHE\";\n");

        string Fecha(int dias) => new DateTime(2026, 11, 22).AddDays(-dias).ToString("dd/MM/yy");

        for (int i = 0; i < cantidad; i++)
        {
            int id = 20001 + i * 7;                      // únicos e inventados, distintos de los reales
            int lactancia = azar.Next(1, 7);
            int del = azar.Next(3, 600);
            string estado = azar.Next(10) switch
            {
                < 4 => "PREÑADA", < 6 => "INSEMIN", 6 => "FRESCA", 7 => "SECA", 8 => "VACIA", _ => "NOINSEM",
            };
            string cvet = estado switch
            {
                "INSEMIN" => azar.Next(3) == 0 ? "0" : "PRE",
                "FRESCA" => "FRES",
                "VACIA" => "DLSC",
                "PREÑADA" => azar.Next(30) == 0 ? "ASEC" : "0",
                _ => "0",
            };
            bool sinCelo = estado is "FRESCA" or "VACIA";
            bool prenada = estado is "PREÑADA" or "SECA";
            int ddu = sinCelo ? 0 : azar.Next(5, 280);
            string celo = sinCelo ? "-" : Fecha(azar.Next(1, 200));
            string toro = sinCelo ? "-" : toros[azar.Next(toros.Length)];
            string fapar = prenada ? Fecha(-azar.Next(30, 280)) : "-";
            int leche = lactancia > 1 && azar.Next(4) == 0 ? azar.Next(20, 60) : 0;

            sb.Append($" {id,4} ;{cvet,-5};{lactancia,4} ;{del,4} ;{Fecha(del)};{celo,8};{toro,-10};{ddu,4} ;{(prenada ? ddu : 0),4} ;{estado,-8};{fapar,8};{leche,6} ;\n");
        }
        sb.Append($"Total:;{cantidad};;;;;;;;;;;\n");
        return System.Text.Encoding.Latin1.GetBytes(sb.ToString()); // Windows-1252: la Ñ es 0xD1
    }

    /// <summary>226 vacas inventadas (ID únicos), estados repartidos y la fila final Total:226.</summary>
    public static byte[] Muestra226Vacas()
    {
        var filas = Vacas(226).ToList();
        filas.Add(["Total:226"]);
        return Construir(EncabezadosDairyComp, filas);
    }

    /// <summary>Filas buenas y malas: ID vacío, repetido (también "ñ1"/"Ñ1"), largo, con espacios y un estado desconocido.</summary>
    public static byte[] FilasConProblemas()
    {
        var filas = new List<object?[]>
        {
            Fila("1001", "PREÑADA"),
            Fila("1002", "INSEMIN"),
            Fila(null, "VACIA"),            // ID vacío
            Fila("1003", "ESTADO-RARO"),    // estado desconocido: entra sin estado
            Fila("1004", "SECA"),
            Fila("1004", "SECA"),           // repetido
            Fila("ñ1", "VACIA"),
            Fila("Ñ1", "VACIA"),            // repetido con "ñ1" aunque cambie la capitalización
            Fila("123456789012345678901", "FRESCA"), // largo (21)
            Fila("10 05", "FRESCA"),        // espacio en el medio
            Fila(2001.0, "PREÑADA"),        // numérica entera
            Fila(2002.5, "PREÑADA"),        // numérica con decimales
            new object?[] { "Total:12" },
        };
        return Construir(EncabezadosDairyComp, filas);
    }

    public static byte[] Corrupto()
    {
        // Cabecera OLE2 correcta pero el resto es basura: ni siquiera se puede abrir el contenedor.
        var bytes = new byte[600];
        byte[] cabecera = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        cabecera.CopyTo(bytes, 0);
        for (int i = cabecera.Length; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 % 251);
        return bytes;
    }

    /// <summary>Un .xlsx de verdad (zip) con extensión .xls: el formato se detecta por contenido, no por extensión.</summary>
    public static byte[] XlsxRenombrado()
    {
        // Se arma el zip a mano (y no con XSSFWorkbook) para que los bytes sean siempre idénticos: NPOI escribe la
        // fecha actual en cada entrada del zip y así el archivo commiteado nunca coincidiría con el generado.
        var partes = new (string Ruta, string Contenido)[]
        {
            ("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>"""),
            ("_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"""),
            ("xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Hoja1" sheetId="1" r:id="rId1"/></sheets></workbook>"""),
            ("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"""),
            ("xl/worksheets/sheet1.xml", """<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>ID</t></is></c><c r="B1" t="inlineStr"><is><t>RPRO</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>1</t></is></c><c r="B2" t="inlineStr"><is><t>VACIA</t></is></c></row></sheetData></worksheet>"""),
        };

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (ruta, contenido) in partes)
            {
                var entrada = zip.CreateEntry(ruta, System.IO.Compression.CompressionLevel.NoCompression);
                entrada.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); // fecha fija
                using var escritor = new StreamWriter(entrada.Open(), new System.Text.UTF8Encoding(false));
                escritor.Write(contenido);
            }
        return ms.ToArray();
    }

    /// <summary>
    /// .xls cifrado con el método RC4 de Excel 97: lleva un registro FILEPASS justo después del BOF. NPOI no sabe ESCRIBIR
    /// archivos cifrados, así que se parte de una muestra chica y se inserta el registro a mano (datos inventados).
    /// El verificador es basura fija: lo único que importa es que NPOI lo reconozca como protegido.
    /// </summary>
    public static byte[] ConContrasena()
    {
        var original = Construir(["ID", "RPRO"], [Fila2("1", "VACIA")]);
        var fsEntrada = new POIFSFileSystem(new MemoryStream(original));
        byte[] flujo;
        using (var entrada = fsEntrada.CreateDocumentInputStream("Workbook"))
        {
            flujo = new byte[entrada.Length];
            for (int leidos = 0; leidos < flujo.Length;) { int k = entrada.Read(flujo, leidos, flujo.Length - leidos); if (k <= 0) break; leidos += k; }
        }

        // Primer registro = BOF: 2 bytes de tipo + 2 de largo + datos.
        int largoBof = BitConverter.ToUInt16(flujo, 2) + 4;
        var filePass = new List<byte>();
        filePass.AddRange(BitConverter.GetBytes((ushort)0x002F)); // tipo FILEPASS
        filePass.AddRange(BitConverter.GetBytes((ushort)54));      // largo
        filePass.AddRange(BitConverter.GetBytes((ushort)1));       // cifrado RC4
        filePass.AddRange(BitConverter.GetBytes((ushort)1));       // versión mayor
        filePass.AddRange(BitConverter.GetBytes((ushort)1));       // versión menor
        for (int i = 0; i < 48; i++) filePass.Add((byte)(i * 7 + 3)); // sal + verificador inventados

        var nuevo = new List<byte>();
        nuevo.AddRange(flujo.Take(largoBof));
        nuevo.AddRange(filePass);
        nuevo.AddRange(flujo.Skip(largoBof));

        var fsSalida = new POIFSFileSystem();
        fsSalida.CreateDocument(new MemoryStream(nuevo.ToArray()), "Workbook");
        using var salida = new MemoryStream();
        fsSalida.WriteFileSystem(salida);
        return salida.ToArray();
    }

    /// <summary>Contenedor OLE2 con "EncryptionInfo" y "EncryptedPackage": así guarda Excel un .xlsx con contraseña.</summary>
    public static byte[] XlsxConContrasena()
    {
        var fs = new POIFSFileSystem();
        fs.CreateDocument(new MemoryStream(new byte[64]), "EncryptionInfo");
        fs.CreateDocument(new MemoryStream(new byte[64]), "EncryptedPackage");
        using var salida = new MemoryStream();
        fs.WriteFileSystem(salida);
        return salida.ToArray();
    }

    /// <summary>Un .xls válido pero sin ninguna hoja.</summary>
    public static byte[] SinHojas()
    {
        using var libro = new HSSFWorkbook();
        return Guardar(libro);
    }

    /// <summary>Encabezados de Dairy Comp pero sin la columna indicada.</summary>
    public static byte[] SinColumna(string columna)
    {
        var encabezados = EncabezadosDairyComp.Where(e => e != columna).ToArray();
        var filas = new List<object?[]> { Enumerable.Range(0, encabezados.Length).Select(i => (object?)(1000 + i).ToString()).ToArray() };
        return Construir(encabezados, filas);
    }

    public static byte[] EncabezadoDuplicado()
        => Construir(["ID", "RPRO", "lact", "LACT"], [Fila2("1", "VACIA")]);

    public static byte[] SoloEncabezados() => Construir(EncabezadosDairyComp, []);

    // ---- Constructor genérico usado por los tests ----

    /// <summary>
    /// Arma un .xls con una sola hoja: la fila 1 son los encabezados y después las filas dadas. Valores: string = texto,
    /// número = numérica, DateTime = fecha, bool = booleana, <see cref="FormulaExcel"/>, <see cref="ErrorExcel"/>; null = celda sin crear.
    /// <paramref name="filasVaciasAntes"/> deja esa cantidad de filas sin crear antes de los encabezados.
    /// </summary>
    public static byte[] Construir(IReadOnlyList<string> encabezados, IEnumerable<object?[]> filas, int filasVaciasAntes = 0, int hojasExtra = 0)
    {
        using var libro = new HSSFWorkbook();
        var estiloFecha = libro.CreateCellStyle();
        estiloFecha.DataFormat = libro.CreateDataFormat().GetFormat("dd/mm/yyyy");

        var hoja = libro.CreateSheet("Hoja1");
        int numeroFila = filasVaciasAntes;
        var filaEncabezados = hoja.CreateRow(numeroFila++);
        for (int c = 0; c < encabezados.Count; c++)
            filaEncabezados.CreateCell(c).SetCellValue(encabezados[c]);

        foreach (var valores in filas)
        {
            var fila = hoja.CreateRow(numeroFila++);
            for (int c = 0; c < valores.Length; c++)
                Escribir(fila, c, valores[c], estiloFecha);
        }

        // Hojas de más: el perfil debe leer solo la primera.
        for (int h = 0; h < hojasExtra; h++)
        {
            var extra = libro.CreateSheet($"Extra{h}");
            extra.CreateRow(0).CreateCell(0).SetCellValue("OTRA");
        }
        return Guardar(libro);
    }

    private static void Escribir(IRow fila, int columna, object? valor, ICellStyle estiloFecha)
    {
        switch (valor)
        {
            case null: return;
            case string s: fila.CreateCell(columna).SetCellValue(s); break;
            case DateTime d:
                var celdaFecha = fila.CreateCell(columna);
                celdaFecha.SetCellValue(d);
                celdaFecha.CellStyle = estiloFecha;
                break;
            case bool b: fila.CreateCell(columna).SetCellValue(b); break;
            case FormulaExcel f: fila.CreateCell(columna).SetCellFormula(f.Formula); break;
            case ErrorExcel: fila.CreateCell(columna).SetCellErrorValue((byte)FormulaError.NA.Code); break;
            case IConvertible n: fila.CreateCell(columna).SetCellValue(Convert.ToDouble(n, System.Globalization.CultureInfo.InvariantCulture)); break;
            default: throw new ArgumentException("Tipo de valor no soportado en el generador.");
        }
    }

    private static byte[] Guardar(IWorkbook libro)
    {
        using var ms = new MemoryStream();
        libro.Write(ms, true);
        var bytes = ms.ToArray();
        PonerEnCeroLasFechasDelContenedor(bytes);
        return bytes;
    }

    /// <summary>
    /// El contenedor OLE2 de un .xls guarda en cada entrada de su directorio la fecha de creación y de modificación (bytes 100 a
    /// 115 de cada entrada de 128). NPOI pone la hora actual en la raíz, y eso hacía que cada generación saliera distinta y que la
    /// muestra guardada nunca coincidiera. Se ponen en cero: Excel y NPOI los ignoran al leer.
    /// </summary>
    private static void PonerEnCeroLasFechasDelContenedor(byte[] bytes)
    {
        var tamanoSector = 1 << BitConverter.ToUInt16(bytes, 30);
        var tablaFat = LeerTablaFat(bytes, tamanoSector);

        var sector = BitConverter.ToInt32(bytes, 48);   // primer sector del directorio
        while (sector >= 0 && sector < tablaFat.Length)
        {
            var inicio = (sector + 1) * tamanoSector;
            for (var entrada = inicio; entrada + 128 <= inicio + tamanoSector && entrada + 128 <= bytes.Length; entrada += 128)
                Array.Clear(bytes, entrada + 100, 16);
            sector = tablaFat[sector];
        }
    }

    private static int[] LeerTablaFat(byte[] bytes, int tamanoSector)
    {
        var porSector = tamanoSector / 4;
        var cantidad = BitConverter.ToInt32(bytes, 44);   // sectores de la FAT
        var fat = new List<int>();
        for (var i = 0; i < cantidad && i < 109; i++)      // las muestras son chicas: alcanza con la DIFAT de la cabecera
        {
            var sectorFat = BitConverter.ToInt32(bytes, 76 + i * 4);
            var inicio = (sectorFat + 1) * tamanoSector;
            for (var j = 0; j < porSector; j++) fat.Add(BitConverter.ToInt32(bytes, inicio + j * 4));
        }
        return [.. fat];
    }

    // ---- Datos inventados ----

    private static object?[] Fila(object? id, string rpro)
    {
        var fila = new object?[EncabezadosDairyComp.Length];
        fila[0] = id;
        fila[9] = rpro; // columna RPRO
        return fila;
    }

    private static object?[] Fila2(string id, string rpro) => [id, rpro];

    private static IEnumerable<object?[]> Vacas(int cantidad)
    {
        var azar = new Random(20261116); // semilla fija: la muestra siempre sale igual
        for (int i = 0; i < cantidad; i++)
        {
            int id = 10000 + i * 13;                   // únicos e inventados
            int lactancia = azar.Next(1, 6);
            int del = azar.Next(0, 400);
            string estado = Estados[azar.Next(Estados.Length)];
            yield return
            [
                (double)id,                             // ID numérico
                $"V{azar.Next(100, 999)}",              // CVET
                FechaComoTexto(azar),                   // FPART
                (double)lactancia,                      // LACT
                (double)del,                            // DEL
                FechaComoTexto(azar),                   // FCELO
                (double)azar.Next(0, 7),                // VINS: cantidad de inseminaciones (número, no fecha)
                $"TORO{azar.Next(1, 20)}",              // UTORO
                i % 17 == 0 ? "Revisar pata" : null,    // COM
                estado.Length > 0 ? estado : null,      // RPRO
                FechaComoTexto(azar),                   // FAPAR
            ];
        }
    }

    /// <summary>Fecha como la deja Dairy Comp: texto con espacios de relleno (por ejemplo " 3/ 5/26").</summary>
    private static string FechaComoTexto(Random azar) => $"{azar.Next(1, 29),2}/{azar.Next(1, 13),2}/{azar.Next(24, 27)}";
}
