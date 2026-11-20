using System.Globalization;
using NPOI.HSSF.EventUserModel;
using NPOI.HSSF.Record;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;

namespace Aviss.Excel;

/// <summary>
/// Plan B para leer un .xls cuando <c>new HSSFWorkbook(...)</c> de NPOI se rinde con un libro que Excel sí abre (por
/// ejemplo "Unexpected record type (SelectionRecord)": registros en un orden que NPOI no espera). Lee el archivo registro
/// por registro con el modo de eventos de NPOI, que no arma estructuras ni valida el orden, y reconstruye un libro limpio
/// en memoria con los valores (texto, números, fechas, booleanos y errores) para que el perfil use la misma lógica de siempre.
/// No se tocan ni se escriben archivos. Las fórmulas se toman como su último valor calculado.
/// </summary>
internal static class LectorBiffTolerante
{
    // Diferencia entre los dos sistemas de fechas de Excel (el de 1900 y el de 1904), en días.
    private const double DiasEntreSistemasDeFechas = 1462;

    public static HSSFWorkbook Reconstruir(POIFSFileSystem sistemaArchivos)
    {
        var oyente = new Oyente();
        var pedido = new HSSFRequest();
        pedido.AddListenerForAllRecords(oyente);
        new HSSFEventFactory().ProcessWorkbookEvents(pedido, sistemaArchivos);
        return oyente.Construir();
    }

    private abstract record Valor;
    private sealed record Texto(string Cadena) : Valor;
    private sealed record Numero(double Dato, int Xf) : Valor;
    private sealed record Booleano(bool Dato) : Valor;
    private sealed record Error(byte Codigo) : Valor;

    private sealed class Oyente : IHSSFListener
    {
        private readonly List<(int Hoja, int Fila, int Columna, Valor Valor)> _celdas = [];
        private readonly Dictionary<int, string> _formatos = [];
        private readonly List<int> _formatoDeCadaXf = [];
        private SSTRecord? _cadenas;
        private int _hoja = -1;
        private bool _fechas1904;
        private (int Fila, int Columna)? _formulaEsperandoTexto;

        public void ProcessRecord(Record registro)
        {
            switch (registro)
            {
                case BOFRecord { Type: BOFRecordType.Worksheet }:
                    _hoja++;
                    break;
                case DateWindow1904Record fecha1904:
                    _fechas1904 = fecha1904.Windowing == 1;
                    break;
                case FormatRecord formato:
                    _formatos[formato.IndexCode] = formato.FormatString;
                    break;
                case ExtendedFormatRecord xf:
                    _formatoDeCadaXf.Add(xf.FormatIndex);
                    break;
                case SSTRecord cadenas:
                    _cadenas = cadenas;
                    break;
                case LabelSSTRecord celdaCadena:
                    if (_cadenas is not null)
                        Agregar(celdaCadena.Row, celdaCadena.Column, new Texto(_cadenas.GetString(celdaCadena.SSTIndex).String));
                    break;
                case LabelRecord etiqueta:
                    Agregar(etiqueta.Row, etiqueta.Column, new Texto(etiqueta.Value ?? ""));
                    break;
                case NumberRecord numero:
                    Agregar(numero.Row, numero.Column, new Numero(numero.Value, numero.XFIndex));
                    break;
                case RKRecord rk:
                    Agregar(rk.Row, rk.Column, new Numero(rk.RKNumber, rk.XFIndex));
                    break;
                case MulRKRecord variasRk:
                    for (var i = 0; i < variasRk.NumColumns; i++)
                        Agregar(variasRk.Row, variasRk.FirstColumn + i, new Numero(variasRk.GetRKNumberAt(i), variasRk.GetXFAt(i)));
                    break;
                case BoolErrRecord boolOError:
                    Agregar(boolOError.Row, boolOError.Column,
                        boolOError.IsBoolean ? new Booleano(boolOError.BooleanValue) : new Error(boolOError.ErrorValue));
                    break;
                case FormulaRecord formula:
                    // Se toma el último valor calculado: un texto viene en el registro siguiente (StringRecord).
                    if (formula.HasCachedResultString) _formulaEsperandoTexto = (formula.Row, formula.Column);
                    else if (!double.IsNaN(formula.Value)) Agregar(formula.Row, formula.Column, new Numero(formula.Value, formula.XFIndex));
                    break;
                case StringRecord textoDeFormula when _formulaEsperandoTexto is { } posicion:
                    Agregar(posicion.Fila, posicion.Columna, new Texto(textoDeFormula.String ?? ""));
                    _formulaEsperandoTexto = null;
                    break;
            }
        }

        private void Agregar(int fila, int columna, Valor valor)
        {
            if (_hoja < 0) return; // celdas fuera de una hoja de cálculo (no debería pasar)
            _celdas.Add((_hoja, fila, columna, valor));
        }

        public HSSFWorkbook Construir()
        {
            var libro = new HSSFWorkbook();
            var estiloFecha = libro.CreateCellStyle();
            estiloFecha.DataFormat = libro.CreateDataFormat().GetFormat("dd/mm/yyyy");

            var hojas = new Dictionary<int, ISheet>();
            for (var i = 0; i <= _hoja; i++)
                hojas[i] = libro.CreateSheet("Hoja" + (i + 1).ToString(CultureInfo.InvariantCulture));

            foreach (var (hoja, fila, columna, valor) in _celdas)
            {
                var filaNpoi = hojas[hoja].GetRow(fila) ?? hojas[hoja].CreateRow(fila);
                var celda = filaNpoi.CreateCell(columna);
                switch (valor)
                {
                    case Texto t:
                        celda.SetCellValue(t.Cadena);
                        break;
                    case Numero n when EsFecha(n.Xf):
                        celda.SetCellValue(_fechas1904 ? n.Dato + DiasEntreSistemasDeFechas : n.Dato);
                        celda.CellStyle = estiloFecha;
                        break;
                    case Numero n:
                        celda.SetCellValue(n.Dato);
                        break;
                    case Booleano b:
                        celda.SetCellValue(b.Dato);
                        break;
                    case Error e:
                        celda.SetCellErrorValue(e.Codigo);
                        break;
                }
            }
            return libro;
        }

        // Una celda numérica es una fecha si el formato de su estilo (XF) lo es, igual que en Excel.
        private bool EsFecha(int xf)
        {
            if (xf < 0 || xf >= _formatoDeCadaXf.Count) return false;
            var indice = _formatoDeCadaXf[xf];
            var texto = _formatos.TryGetValue(indice, out var propio) ? propio : HSSFDataFormat.GetBuiltinFormat((short)indice);
            return DateUtil.IsADateFormat(indice, texto);
        }
    }
}
