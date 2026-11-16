using Aviss.Application.Puertos;

namespace Aviss.Infrastructure.Sistema;

/// <summary>
/// Identificador de esta tablet (RF-23). Se genera la primera vez y se guarda en el archivo <c>dispositivo.id</c>,
/// FUERA de la base: así recrear o restaurar la base no lo cambia.
/// </summary>
public sealed class DispositivoService : IDispositivoService
{
    private readonly RutasAviss _rutas;
    private readonly object _candado = new();
    private Guid? _id;

    public DispositivoService(RutasAviss rutas) => _rutas = rutas;

    public Guid ObtenerId()
    {
        lock (_candado)
        {
            if (_id is Guid ya) return ya;

            // Si el archivo existe y es válido, se respeta. Si está dañado se genera uno nuevo (no hay otra opción).
            if (File.Exists(_rutas.ArchivoDispositivo)
                && Guid.TryParse(File.ReadAllText(_rutas.ArchivoDispositivo).Trim(), out var existente)
                && existente != Guid.Empty)
            {
                _id = existente;
                return existente;
            }

            var nuevo = Guid.NewGuid();
            Directory.CreateDirectory(_rutas.DirectorioDatos);
            // Se escribe a un temporal y se mueve: un corte de energía nunca deja el archivo a medias.
            var temporal = _rutas.ArchivoDispositivo + ".tmp";
            File.WriteAllText(temporal, nuevo.ToString());
            File.Move(temporal, _rutas.ArchivoDispositivo, overwrite: true);
            _id = nuevo;
            return nuevo;
        }
    }
}
