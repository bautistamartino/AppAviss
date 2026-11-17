namespace Aviss.Application.Importacion;

public enum EstadoVistaPrevia { Pendiente, Confirmando, Consumida }

/// <summary>Qué pasó al intentar tomar una vista previa para confirmarla.</summary>
public enum ResultadoToma { Tomada, NoDisponible, EnCurso, YaConfirmada }

/// <summary>
/// Guarda A LO SUMO UNA vista previa en memoria (RF-22) y controla que se confirme una sola vez (RF-19).
/// Transiciones: Pendiente → Confirmando (Tomar) → Consumida (Consumir) o de vuelta a Pendiente (Devolver, si falló).
/// Todo bajo un candado, así que dos confirmaciones a la vez no pueden ganar las dos.
/// </summary>
public sealed class AlmacenVistasPrevias
{
    private readonly object _candado = new();
    private VistaPreviaImportacion? _vista;     // se libera al consumir (los bytes pueden pesar hasta 25 MB)
    private Guid? _id;
    private EstadoVistaPrevia _estado;

    /// <summary>Guarda la vista como Pendiente; si había otra, se descarta.</summary>
    public void Guardar(VistaPreviaImportacion vista)
    {
        lock (_candado)
        {
            _vista = vista;
            _id = vista.Id;
            _estado = EstadoVistaPrevia.Pendiente;
        }
    }

    /// <summary>Pendiente → Confirmando de forma atómica. Solo una llamada la gana.</summary>
    public ResultadoToma Tomar(Guid id, out VistaPreviaImportacion? vista)
    {
        lock (_candado)
        {
            vista = null;
            if (_id != id) return ResultadoToma.NoDisponible;
            switch (_estado)
            {
                case EstadoVistaPrevia.Confirmando: return ResultadoToma.EnCurso;
                case EstadoVistaPrevia.Consumida: return ResultadoToma.YaConfirmada;
                default:
                    _estado = EstadoVistaPrevia.Confirmando;
                    vista = _vista;
                    return ResultadoToma.Tomada;
            }
        }
    }

    /// <summary>Confirmando → Consumida (la confirmación terminó bien). Libera los bytes pero recuerda el Id.</summary>
    public bool Consumir(Guid id)
    {
        lock (_candado)
        {
            if (_id != id || _estado != EstadoVistaPrevia.Confirmando) return false;
            _estado = EstadoVistaPrevia.Consumida;
            _vista = null;
            return true;
        }
    }

    /// <summary>Confirmando → Pendiente (la confirmación falló: se puede reintentar).</summary>
    public bool Devolver(Guid id)
    {
        lock (_candado)
        {
            if (_id != id || _estado != EstadoVistaPrevia.Confirmando) return false;
            _estado = EstadoVistaPrevia.Pendiente;
            return true;
        }
    }

    /// <summary>Descarta una vista Pendiente (o ya Consumida). Una que se está confirmando no se toca.</summary>
    public bool Descartar(Guid id)
    {
        lock (_candado)
        {
            if (_id != id || _estado == EstadoVistaPrevia.Confirmando) return false;
            _vista = null;
            _id = null;
            return true;
        }
    }

    /// <summary>Estado de la vista indicada; nulo si no existe (o fue reemplazada o descartada).</summary>
    public EstadoVistaPrevia? EstadoDe(Guid id)
    {
        lock (_candado) return _id == id ? _estado : null;
    }
}
