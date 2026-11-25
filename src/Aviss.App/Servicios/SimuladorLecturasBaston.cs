#if DEBUG
using Aviss.Application.Puertos;
using Aviss.Rfid;

namespace Aviss.App.Servicios;

/// <summary>
/// Simulador de lecturas de la compilación de desarrollo (spec 003, RF-31): el panel "lee" un código a través del lector real,
/// así pasa por el mismo filtro de repetidas y la misma cola que el bastón (spec 008, RF-25). No existe en Release.
/// </summary>
public sealed class SimuladorLecturasBaston(LectorBastonXrs2 lector) : ISimuladorLecturas
{
    public void Emitir(string codigo) => lector.Inyectar(codigo);
}
#endif
