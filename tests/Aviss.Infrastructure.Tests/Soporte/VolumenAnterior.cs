namespace Aviss.Infrastructure.Tests.Soporte;

/// <summary>
/// Llena una base de la versión anterior (con Usuario, migrada hasta "EsperaYRechazo") con muchas vacas y registros, con SQL
/// por lotes (CTE recursivas), para medir cuánto tarda la migración "QuitarVeterinario" (spec 006). Los ids son
/// deterministas y con forma de GUID. Se usa tanto en la prueba de rendimiento (20.000 vacas) como en una versión chica
/// que corre siempre y comprueba que el generador y la migración andan.
/// </summary>
public static class VolumenAnterior
{
    public const int TactosPorVaca = 3;
    public const int HistorialPorVaca = 2;

    private static string Id(int prefijo, string numero) => $"printf('{prefijo:D8}-0000-0000-0000-%012d', {numero})";

    /// <summary>Devuelve la cantidad de vacas y la de registros (tactos + marcas + historial) cargados.</summary>
    public static (int Vacas, int Registros) Poblar(BaseDeDatosDePrueba db, int vacas)
    {
        var anterior = new EsquemaAnterior(db);
        var vet = anterior.Usuario("Vet Volumen");
        var rodeo = db.Rodeo(db.Establecimiento("Tambo Volumen"), new string('d', 64));
        var estado = db.Estado("SECA", "SECA", "SECA");
        var manga = anterior.Manga(rodeo, 1, vet);
        var f = BaseDeDatosDePrueba.Fecha(db.Reloj.UtcAhora);
        var dispositivo = Guid.NewGuid();

        string Serie(int hasta) => $"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {hasta}) ";
        var animal = Id(0, "i");
        var animalDe = (string divisor) => Id(0, $"(i - 1) / {divisor} + 1");

        // Una sola transacción: es mucho más rápido y es como se carga una importación grande.
        db.Ejecutar("BEGIN");
        db.Ejecutar(Serie(vacas) + $@"INSERT INTO Animal (Id, RodeoId, NumeroCaravana, EstadoReproductivoId, Origen, Anulado,
                DatosOriginalesJson, CreadoEnUtc, ActualizadoEnUtc)
            SELECT {animal}, $r, CAST(i AS TEXT), $e, 'Importado', 0, '{{}}', $f, $f FROM n",
            ("$r", rodeo), ("$e", estado), ("$f", f));
        db.Ejecutar(Serie(vacas * TactosPorVaca) + $@"INSERT INTO Tacto (Id, AnimalId, FechaHoraUtc, OrdenInsercion, EstadoNuevoId, Tipo,
                Anulado, UsuarioId, DispositivoId, CreadoEnUtc, ActualizadoEnUtc)
            SELECT {Id(1, "i")}, {animalDe(TactosPorVaca.ToString())}, $f, i, $e, 'Normal', 0, $u, $d, $f, $f FROM n",
            ("$e", estado), ("$f", f), ("$u", vet), ("$d", dispositivo));
        db.Ejecutar(Serie(vacas) + $@"INSERT INTO MarcaPasada (Id, AnimalId, MangaId, FechaHoraUtc, OrdenInsercion, UsuarioId,
                DispositivoId, Anulado, CreadoEnUtc, ActualizadoEnUtc)
            SELECT {Id(2, "i")}, {animal}, $m, $f, i, $u, $d, 0, $f, $f FROM n",
            ("$m", manga), ("$f", f), ("$u", vet), ("$d", dispositivo));
        db.Ejecutar(Serie(vacas * HistorialPorVaca) + $@"INSERT INTO Historial (Id, Entidad, EntidadId, Campo, ValorAnterior, ValorNuevo,
                FechaHoraUtc, UsuarioId, Accion, CreadoEnUtc)
            SELECT {Id(3, "i")}, 'Animal', {animalDe(HistorialPorVaca.ToString())}, 'Campo', NULL, 'x', $f, $u, 'Tacto', $f FROM n",
            ("$f", f), ("$u", vet));
        db.Ejecutar("COMMIT");

        return (vacas, vacas * (TactosPorVaca + 1 + HistorialPorVaca));
    }
}
