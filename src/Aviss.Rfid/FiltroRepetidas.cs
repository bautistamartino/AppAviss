namespace Aviss.Rfid;

/// <summary>
/// Ignora el mismo EID si vuelve dentro de la ventana (RF-21: 3 s). Se compara contra la última lectura <b>aceptada</b> de
/// ese EID: las ignoradas no renuevan la ventana. Solo recuerda el último EID: A, B, A deja pasar la segunda A.
/// </summary>
public sealed class FiltroRepetidas(TimeSpan ventana, Func<DateTime> ahoraUtc)
{
    private string? _ultimo;
    private DateTime _horaUltimo;

    /// <returns>Verdadero si hay que entregar la lectura; falso si es una repetida.</returns>
    public bool Aceptar(string eid)
    {
        var ahora = ahoraUtc();
        if (_ultimo == eid && ahora - _horaUltimo < ventana) return false;
        _ultimo = eid;
        _horaUltimo = ahora;
        return true;
    }

    public void Reiniciar() => _ultimo = null;
}
