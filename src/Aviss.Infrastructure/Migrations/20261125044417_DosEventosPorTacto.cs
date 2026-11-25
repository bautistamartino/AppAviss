using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <summary>
    /// Spec 011 (dos eventos por tacto): <c>Tacto</c> y <c>Rechazo</c> (la tabla de los registros "solo evento") ganan
    /// <c>Evento2Id</c> y <c>Observaciones2</c>: el segundo evento queda en el MISMO registro que el primero, así Deshacer,
    /// Editar, "último vigente" y la exportación siguen viendo un registro por vaca y por tacto.
    /// El Up está ESCRITO A MANO, no es el que generó EF: el Designer y el snapshot sí son los de EF (por eso
    /// "dotnet ef migrations has-pending-model-changes" dice que no hay cambios). Como en "EventosConPasos", se usa
    /// ALTER TABLE ... ADD COLUMN: nada se reconstruye (en SQLite reconstruir una tabla se lleva sus triggers) y todas las
    /// filas existentes quedan válidas, con el evento 2 vacío. Este SQL es literal y no se comparte con otro código: una
    /// migración ya publicada no debe cambiar.
    /// El tope de 300 caracteres de las observaciones NO está en la base (decisión del responsable: pantalla y servicio).
    /// Atomicidad y copia previa (constitución §7): EF aplica la migración en una transacción y el arranque hace un backup
    /// antes y lo restaura si algo falla, así que o queda todo o no queda nada.
    /// </summary>
    public partial class DosEventosPorTacto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Tacto: el evento 2 y su observación ----
            // SQLite permite agregar una columna con FK solo si el valor por defecto es NULL, que es el caso.
            migrationBuilder.Sql(@"ALTER TABLE ""Tacto"" ADD COLUMN ""Evento2Id"" TEXT NULL CONSTRAINT ""FK_Tacto_EstadoReproductivo_Evento2Id"" REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Tacto_Evento2Id"" ON ""Tacto"" (""Evento2Id"");");
            // Observaciones2 va última porque sus CHECK de varias columnas miran las otras: tienen que existir ya.
            migrationBuilder.Sql(@"ALTER TABLE ""Tacto"" ADD COLUMN ""Observaciones2"" TEXT NULL
    CONSTRAINT ""CK_Tacto_Observaciones2"" CHECK (Observaciones2 IS NULL OR Evento2Id IS NOT NULL)
    CONSTRAINT ""CK_Tacto_Evento2Distinto"" CHECK (Evento2Id IS NULL OR (Evento2Id IS NOT EstadoNuevoId AND Evento2Id IS NOT EventoId))
    CONSTRAINT ""CK_Tacto_Evento2SinDias"" CHECK (Evento2Id IS NULL OR (DiasPreniezInformados IS NULL AND DiasAborto IS NULL))
    CONSTRAINT ""CK_Tacto_AnulacionSinEvento2"" CHECK (Tipo = 'Normal' OR Evento2Id IS NULL);");

            // ---- Rechazo: lo mismo para los registros "solo evento" ----
            migrationBuilder.Sql(@"ALTER TABLE ""Rechazo"" ADD COLUMN ""Evento2Id"" TEXT NULL CONSTRAINT ""FK_Rechazo_EstadoReproductivo_Evento2Id"" REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Rechazo_Evento2Id"" ON ""Rechazo"" (""Evento2Id"");");
            // Solo con un evento común: el Rechazo fijo (EventoId nulo) y un Aborto (con días) no combinan, y no puede ser igual al evento 1.
            migrationBuilder.Sql(@"ALTER TABLE ""Rechazo"" ADD COLUMN ""Observaciones2"" TEXT NULL
    CONSTRAINT ""CK_Rechazo_Observaciones2"" CHECK (Observaciones2 IS NULL OR Evento2Id IS NOT NULL)
    CONSTRAINT ""CK_Rechazo_Evento2ConEvento"" CHECK (Evento2Id IS NULL OR (EventoId IS NOT NULL AND DiasAborto IS NULL AND Evento2Id IS NOT EventoId));");

            // ---- Triggers: se recrean los cuatro que enumeran columnas o validan el evento (constitución §7) ----
            // Ningún otro trigger se toca (TR_Tacto_EstadoNoSoloEvento, los NoBorrar, los de Historial, etc.).
            // Sin esto, anular un registro podría cambiarle de paso el evento 2 o su observación.
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
    AND OLD.DiasAborto IS NEW.DiasAborto
    AND OLD.Evento2Id IS NEW.Evento2Id
    AND OLD.Observaciones2 IS NEW.Observaciones2)
BEGIN
    SELECT RAISE(ABORT, 'Un tacto guardado no se puede modificar; solo se puede anular.');
END;");

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
    AND OLD.Evento2Id IS NEW.Evento2Id
    AND OLD.Observaciones2 IS NEW.Observaciones2
    AND OLD.Observaciones IS NEW.Observaciones)
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo guardado no se puede modificar; solo se puede anular.');
END;");

            // Validez del evento en un tacto nuevo: el mismo cuerpo de "EventosConPasos" más las reglas del evento 2. Cada regla
            // nueva solo se activa con NEW.Evento2Id IS NOT NULL, así un tacto de un solo evento no se ve afectado. Que el
            // evento 2 no sea igual al evento 1, no lleve días y no esté en una anulación lo cubren los CHECK de arriba.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Tacto_EventoValido;");
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

    -- Spec 011, evento 2: nunca lleva pasos ni destino...
    SELECT RAISE(ABORT, 'El evento 2 no puede tener pasos ni destino.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.Evento2Id AND (Pasos IS NOT NULL OR EstadoDestinoId IS NOT NULL));
    -- ...ni es un estándar de estado (VACIA, INSEMIN, SECA...): solo OK, Revisar Nuevamente o un evento creado a mano.
    SELECT RAISE(ABORT, 'El evento 2 no puede ser un estado estándar.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.Evento2Id AND ClaveEstandar IS NOT NULL AND Tipo <> 'SoloEvento');
    -- El evento 1 real (el evento elegido, o el estado si no hay evento) no puede tener pasos ni destino.
    SELECT RAISE(ABORT, 'Un evento con pasos no admite un evento 2.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = COALESCE(NEW.EventoId, NEW.EstadoNuevoId) AND (Pasos IS NOT NULL OR EstadoDestinoId IS NOT NULL));
END;");

            // Validez de un registro "solo evento" nuevo: el mismo control de "EventosConPasos" más las reglas del evento 2.
            // Antes tenía una cláusula WHEN; ahora hay varias reglas con su propia condición, así que cada una lleva su WHERE.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rechazo_SoloEvento;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_SoloEvento BEFORE INSERT ON Rechazo
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo solo puede guardar un evento de tipo solo evento, o un Aborto con sus días.')
     WHERE NEW.EventoId IS NOT NULL
       AND NOT (
            ((SELECT Tipo FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'SoloEvento' AND NEW.DiasAborto IS NULL)
         OR ((SELECT ClaveEstandar FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS 'ABORTO' AND NEW.DiasAborto IS NOT NULL));

    -- Spec 011, evento 2 (el Rechazo fijo, el Aborto y el mismo evento los rechazan los CHECK de la tabla).
    SELECT RAISE(ABORT, 'El evento 2 no puede tener pasos ni destino.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.Evento2Id AND (Pasos IS NOT NULL OR EstadoDestinoId IS NOT NULL));
    SELECT RAISE(ABORT, 'El evento 2 no puede ser un estado estándar.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.Evento2Id AND ClaveEstandar IS NOT NULL AND Tipo <> 'SoloEvento');
    SELECT RAISE(ABORT, 'Un evento con pasos no admite un evento 2.')
     WHERE NEW.Evento2Id IS NOT NULL
       AND EXISTS (SELECT 1 FROM EstadoReproductivo WHERE Id = NEW.EventoId AND (Pasos IS NOT NULL OR EstadoDestinoId IS NOT NULL));
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El proyecto no baja versiones: las bases solo avanzan (hay backup previo en cada actualización).
        }
    }
}
