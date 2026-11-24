using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <summary>
    /// Spec 005, segunda vuelta (eventos con pasos): Aborto pasa a tener pasos (días y resultado), aparece "Preñada fecha
    /// previa" (la fila nueva la crea el sembrador) y los registros guardan el evento elegido y sus días.
    /// El Up está ESCRITO A MANO, no es el que generó EF: el Designer y el snapshot sí son los de EF (por eso
    /// "dotnet ef migrations has-pending-model-changes" dice que no hay cambios). EF habría reconstruido las tablas
    /// EstadoReproductivo, Tacto y Rechazo para agregar los CHECK y las claves foráneas, y en SQLite eso se lleva sus
    /// triggers; con ALTER TABLE ... ADD COLUMN nada se reconstruye y las filas viejas quedan válidas (todas las
    /// columnas nuevas admiten nulo). Como en las migraciones anteriores, este SQL es literal y no se comparte con otro
    /// código: una migración ya publicada no debe cambiar. Los rangos de días (1 a 400 y 0 a 400) son PROVISORIOS
    /// (pregunta P9): si cambian, va una migración chica que recree los CHECK; en el código viven en RangosDias.
    /// Atomicidad (RF-19): EF aplica la migración en una transacción y el arranque hace un backup antes y lo restaura si
    /// falla, así que o queda todo o no queda nada.
    /// </summary>
    public partial class EventosConPasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- EstadoReproductivo: los pasos que pide el evento y el estado en que deja a la vaca ----
            // Pasos: lista de nombres separados por coma ("DiasAborto,ResultadoAborto"). El CHECK solo pide letras y comas:
            // los valores válidos los conoce el código, así un tercer evento con pasos no necesita migración.
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""Pasos"" TEXT NULL CONSTRAINT ""CK_Estado_Pasos"" CHECK (Pasos IS NULL OR (length(Pasos) > 0 AND Pasos NOT GLOB '*[^A-Za-z,]*'));");
            // SQLite permite agregar una columna con FK solo si el valor por defecto es NULL, que es el caso.
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""EstadoDestinoId"" TEXT NULL CONSTRAINT ""FK_EstadoReproductivo_EstadoReproductivo_EstadoDestinoId"" REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_EstadoReproductivo_EstadoDestinoId"" ON ""EstadoReproductivo"" (""EstadoDestinoId"");");

            // ---- Tacto: el evento elegido y sus días ----
            migrationBuilder.Sql(@"ALTER TABLE ""Tacto"" ADD COLUMN ""EventoId"" TEXT NULL CONSTRAINT ""FK_Tacto_EstadoReproductivo_EventoId"" REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Tacto_EventoId"" ON ""Tacto"" (""EventoId"");");
            migrationBuilder.Sql(@"ALTER TABLE ""Tacto"" ADD COLUMN ""DiasPreniezInformados"" INTEGER NULL CONSTRAINT ""CK_Tacto_DiasPreniezInformados"" CHECK (DiasPreniezInformados IS NULL OR DiasPreniezInformados BETWEEN 1 AND 400);");
            // DiasAborto va última porque sus CHECK de varias columnas miran las otras dos: tienen que existir ya.
            migrationBuilder.Sql(@"ALTER TABLE ""Tacto"" ADD COLUMN ""DiasAborto"" INTEGER NULL
    CONSTRAINT ""CK_Tacto_DiasAborto"" CHECK (DiasAborto IS NULL OR DiasAborto BETWEEN 0 AND 400)
    CONSTRAINT ""CK_Tacto_DiasExclusivos"" CHECK (DiasPreniezInformados IS NULL OR DiasAborto IS NULL)
    CONSTRAINT ""CK_Tacto_DiasConEvento"" CHECK (EventoId IS NOT NULL OR (DiasPreniezInformados IS NULL AND DiasAborto IS NULL))
    CONSTRAINT ""CK_Tacto_AnulacionSinEvento"" CHECK (Tipo = 'Normal' OR (EventoId IS NULL AND DiasPreniezInformados IS NULL AND DiasAborto IS NULL));");

            // ---- Rechazo: los días de un Aborto con resultado Rechazo ----
            migrationBuilder.Sql(@"ALTER TABLE ""Rechazo"" ADD COLUMN ""DiasAborto"" INTEGER NULL
    CONSTRAINT ""CK_Rechazo_DiasAborto"" CHECK (DiasAborto IS NULL OR DiasAborto BETWEEN 0 AND 400)
    CONSTRAINT ""CK_Rechazo_DiasConEvento"" CHECK (DiasAborto IS NULL OR EventoId IS NOT NULL);");

            // ---- Datos: lo único que se decide con SQL (lo demás lo hace el sembrador, que necesita C#) ----
            // Aborto deja de ser "solo evento": ahora cambia el estado según su resultado y pide días y resultado. Los
            // registros viejos de Aborto (en Rechazo, con DiasAborto nulo) no se tocan: se ven como "Aborto" a secas (RF-31).
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""Tipo"" = 'Estado', ""Pasos"" = 'DiasAborto,ResultadoAborto', ""EsPrincipal"" = 1, ""Orden"" = 6 WHERE ""ClaveEstandar"" = 'ABORTO';");
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""Orden"" = 7 WHERE ""ClaveEstandar"" = 'NOINSEM';");

            // ---- Triggers: se recrean los que enumeran columnas y se agrega el de validez del evento (constitución §7) ----
            // Inmutabilidad de Tacto: el mismo trigger con las tres columnas nuevas. Sin esto, anular un tacto podría
            // cambiarle de paso el evento o los días.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Tacto_NoModificar;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_NoModificar BEFORE UPDATE ON Tacto
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.EstadoAnteriorId IS NEW.EstadoAnteriorId
    AND OLD.EstadoNuevoId IS NEW.EstadoNuevoId
    AND OLD.DiasPrenez IS NEW.DiasPrenez
    AND OLD.Observaciones IS NEW.Observaciones
    AND OLD.Tipo IS NEW.Tipo
    AND OLD.TactoAnuladoId IS NEW.TactoAnuladoId
    AND OLD.EventoId IS NEW.EventoId
    AND OLD.DiasPreniezInformados IS NEW.DiasPreniezInformados
    AND OLD.DiasAborto IS NEW.DiasAborto)
BEGIN
    SELECT RAISE(ABORT, 'Un tacto guardado no se puede modificar; solo se puede anular.');
END;");

            // Inmutabilidad de Rechazo: el mismo trigger con DiasAborto.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rechazo_NoModificar;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_NoModificar BEFORE UPDATE ON Rechazo
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.EventoId IS NEW.EventoId
    AND OLD.DiasAborto IS NEW.DiasAborto
    AND OLD.Observaciones IS NEW.Observaciones)
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo guardado no se puede modificar; solo se puede anular.');
END;");

            // Inserción de Rechazo: antes solo aceptaba un evento "solo evento". Ahora acepta (1) un solo evento común,
            // sin días de aborto, o (2) el Aborto estándar, CON días de aborto (un Aborto con resultado Rechazo). Un
            // Rechazo con días para cualquier otro evento queda rechazado por la base.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rechazo_SoloEvento;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_SoloEvento BEFORE INSERT ON Rechazo
WHEN NEW.EventoId IS NOT NULL
 AND NOT (
        ((SELECT Tipo FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'SoloEvento' AND NEW.DiasAborto IS NULL)
     OR ((SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'ABORTO' AND NEW.DiasAborto IS NOT NULL))
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo solo puede guardar un evento de tipo solo evento, o un Aborto con sus días.');
END;");

            // Validez del evento en un tacto nuevo (solo los Normal; la anulación no lleva evento). Varias reglas en un
            // trigger: cada SELECT RAISE ... WHERE corta la inserción si su condición se cumple.
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_EventoValido BEFORE INSERT ON Tacto
WHEN NEW.Tipo = 'Normal'
BEGIN
    -- Un evento de tipo solo evento no deja a la vaca en ningún estado: no va en un tacto.
    SELECT RAISE(ABORT, 'Un tacto no puede apuntar a un evento de tipo solo evento.')
     WHERE NEW.EventoId IS NOT NULL
       AND (SELECT Tipo FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'SoloEvento';
    -- Un evento con destino (Preñada fecha previa) deja a la vaca en ese destino y no en otro estado.
    SELECT RAISE(ABORT, 'El estado final no coincide con el destino del evento.')
     WHERE NEW.EventoId IS NOT NULL
       AND (SELECT EstadoDestinoId FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS NOT NULL
       AND NEW.EstadoNuevoId IS NOT (SELECT EstadoDestinoId FROM EstadoReproductivo WHERE Id = NEW.EventoId);
    -- Los días solo se aceptan si el evento declara el paso...
    SELECT RAISE(ABORT, 'Este evento no pide días de preñez.')
     WHERE NEW.DiasPreniezInformados IS NOT NULL
       AND COALESCE((SELECT Pasos FROM EstadoReproductivo WHERE Id = NEW.EventoId), '') NOT LIKE '%DiasPreniez%';
    SELECT RAISE(ABORT, 'Este evento no pide días de aborto.')
     WHERE NEW.DiasAborto IS NOT NULL
       AND COALESCE((SELECT Pasos FROM EstadoReproductivo WHERE Id = NEW.EventoId), '') NOT LIKE '%DiasAborto%';
    -- ...y son obligatorios si lo declara.
    SELECT RAISE(ABORT, 'Falta informar los días de preñez.')
     WHERE NEW.EventoId IS NOT NULL AND NEW.DiasPreniezInformados IS NULL
       AND COALESCE((SELECT Pasos FROM EstadoReproductivo WHERE Id = NEW.EventoId), '') LIKE '%DiasPreniez%';
    SELECT RAISE(ABORT, 'Falta informar los días de aborto.')
     WHERE NEW.EventoId IS NOT NULL AND NEW.DiasAborto IS NULL
       AND COALESCE((SELECT Pasos FROM EstadoReproductivo WHERE Id = NEW.EventoId), '') LIKE '%DiasAborto%';
    -- Un Aborto solo puede terminar en Vacía, Inseminada, Seca o Fresca (Rechazo no es un tacto).
    SELECT RAISE(ABORT, 'Un aborto solo puede dejar a la vaca en VACIA, INSEMIN, SECA o FRESCA.')
     WHERE NEW.EventoId IS NOT NULL
       AND (SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'ABORTO'
       AND COALESCE((SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = NEW.EstadoNuevoId), '') NOT IN ('VACIA','INSEMIN','SECA','FRESCA');
    -- Un evento con pasos o con destino (Aborto, fecha previa) nunca es el estado en que queda una vaca.
    SELECT RAISE(ABORT, 'Un tacto no puede dejar a la vaca en un evento con pasos.')
     WHERE EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.EstadoNuevoId AND (Pasos IS NOT NULL OR EstadoDestinoId IS NOT NULL));
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El proyecto no baja versiones: las bases solo avanzan (hay backup previo en cada actualización).
        }
    }
}
