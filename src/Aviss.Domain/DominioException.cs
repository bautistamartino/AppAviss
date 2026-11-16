namespace Aviss.Domain;

/// <summary>
/// Se lanza cuando se intenta crear o cambiar una entidad violando una regla del negocio
/// (por ejemplo, una caravana de 21 caracteres). Los servicios de Application la atrapan
/// y la convierten en un <c>Resultado</c> con mensaje; el mensaje nunca lleva datos de animales ni de tambos.
/// </summary>
public sealed class DominioException : Exception
{
    public DominioException(string mensaje) : base(mensaje) { }
}
