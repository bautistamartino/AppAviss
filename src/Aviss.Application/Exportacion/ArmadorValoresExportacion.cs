using System.Globalization;
using Aviss.Domain.Catalogo;
using Aviss.Domain.Enums;
using Aviss.Domain.Reglas;

namespace Aviss.Application.Exportacion;

/// <summary>
/// Convierte el registro elegido de una vaca en los seis valores de texto de las columnas nuevas (spec 007, RF-6 a RF-10). Qué columna
/// queda vacía lo dicen los datos del registro: los días de preñez solo existen en "Preñada fecha previa"; los días de aborto y el
/// resultado solo en un Aborto con sus pasos. Todo lo demás sale en blanco.
/// </summary>
public static class ArmadorValoresExportacion
{
    public static ValoresExportacion Armar(RegistroExportable registro, TimeZoneInfo zona)
    {
        var diasPreniez = registro.DiasPreniezInformados is int dp ? dp.ToString(CultureInfo.InvariantCulture) : "";

        // Un Aborto viejo (guardado antes de los pasos) no tiene días: sale como "Aborto" a secas, sin resultado.
        var diasAborto = registro.DiasAborto is int da ? da.ToString(CultureInfo.InvariantCulture) : "";
        var resultado = registro.DiasAborto is not null && registro.ResultadoAborto is ResultadoAborto r
            ? ReglasResultadoAborto.Etiqueta(r) : "";

        return new ValoresExportacion(
            FormatoColumnasExportacion.Fecha(registro.FechaHoraUtc, zona),
            FormatoColumnasExportacion.Hora(registro.FechaHoraUtc, zona),
            registro.NombreEvento,
            diasPreniez, diasAborto, resultado);
    }
}
