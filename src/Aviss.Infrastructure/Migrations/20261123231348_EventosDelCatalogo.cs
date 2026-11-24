using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <summary>
    /// Spec 005 (eventos del catálogo). Va DESPUÉS de QuitarVeterinario (spec 006): parte de la base ya sin veterinario, por
    /// eso el trigger de inmutabilidad de Rechazo que recrea no compara usuario ni dispositivo. El Up está ESCRITO A MANO, no es el que generó EF: el Designer y el snapshot sí son
    /// los de EF (por eso "dotnet ef migrations has-pending-model-changes" dice que no hay cambios). EF habría
    /// reconstruido las tablas para agregar los CHECK y la FK, y en SQLite eso se lleva los triggers de Rechazo; con
    /// ALTER TABLE ... ADD COLUMN nada se reconstruye. Como en "Inicial", "Mangas" y "EsperaYRechazo", este SQL es literal
    /// y no se comparte con otro código: una migración ya publicada no debe cambiar.
    /// </summary>
    public partial class EventosDelCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- EstadoReproductivo: cuatro columnas nuevas, todas con valor por defecto (las filas viejas quedan válidas) ----
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""Tipo"" TEXT NOT NULL DEFAULT 'Estado' CONSTRAINT ""CK_Estado_Tipo"" CHECK (Tipo IN ('Estado','SoloEvento'));");
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""EsPrincipal"" INTEGER NOT NULL DEFAULT 0 CONSTRAINT ""CK_Estado_EsPrincipal"" CHECK (EsPrincipal IN (0,1));");
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""Orden"" INTEGER NOT NULL DEFAULT 0 CONSTRAINT ""CK_Estado_Orden"" CHECK (""Orden"" >= 0);");
            migrationBuilder.Sql(@"ALTER TABLE ""EstadoReproductivo"" ADD COLUMN ""ClaveEstandar"" TEXT NULL CONSTRAINT ""CK_Estado_ClaveEstandar"" CHECK (ClaveEstandar IS NULL OR (length(ClaveEstandar) > 0 AND ClaveEstandar NOT GLOB '*[^A-Z0-9_]*'));");

            // Un evento estándar no se puede duplicar: la clave es única entre los que la tienen.
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_EstadoReproductivo_ClaveEstandar"" ON ""EstadoReproductivo"" (""ClaveEstandar"") WHERE ClaveEstandar IS NOT NULL;");

            // ---- Relleno de los 6 iniciales (RF-19) ----
            // Solo clave, principal y orden. NO toca Nombre, CodigoDairyComp ni Activo: un inicial desactivado a mano
            // sigue desactivado. Los creados a mano quedan con los valores por defecto (Estado, no principal, sin clave).
            // Los tres eventos nuevos (OK, Aborto, Revisar Nuevamente) y la adopción de uno creado a mano los hace el
            // sembrador (necesita lógica de C#), en una sola transacción.
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""ClaveEstandar"" = ""Codigo"" WHERE ""Codigo"" IN ('PRENADA','INSEMIN','SECA','VACIA','FRESCA','NOINSEM');");
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""EsPrincipal"" = 1, ""Orden"" = 3 WHERE ""Codigo"" = 'VACIA';");
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""EsPrincipal"" = 1, ""Orden"" = 4 WHERE ""Codigo"" = 'PRENADA';");
            migrationBuilder.Sql(@"UPDATE ""EstadoReproductivo"" SET ""EsPrincipal"" = 1, ""Orden"" = 6 WHERE ""Codigo"" = 'NOINSEM';");

            // ---- Rechazo: el evento del catálogo que guarda (nulo = el Rechazo fijo de la aplicación) ----
            // SQLite permite agregar una columna con FK solo si el valor por defecto es NULL, que es el caso.
            migrationBuilder.Sql(@"ALTER TABLE ""Rechazo"" ADD COLUMN ""EventoId"" TEXT NULL CONSTRAINT ""FK_Rechazo_EstadoReproductivo_EventoId"" REFERENCES ""EstadoReproductivo"" (""Id"") ON DELETE RESTRICT;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Rechazo_EventoId"" ON ""Rechazo"" (""EventoId"");");

            // ---- Triggers de inserción: la regla también vale en la base (constitución §7) ----
            // Un Rechazo solo puede apuntar a un evento de tipo SoloEvento.
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_SoloEvento BEFORE INSERT ON Rechazo
WHEN NEW.EventoId IS NOT NULL
 AND (SELECT Tipo FROM EstadoReproductivo WHERE Id = NEW.EventoId) IS NOT 'SoloEvento'
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo solo puede guardar un evento de tipo solo evento.');
END;");
            // Un tacto Normal no puede dejar a la vaca en un SoloEvento (esos no cambian el estado). Solo bloquea
            // inserciones nuevas: los tactos viejos que apuntan a un estado adoptado como solo evento no se tocan.
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_EstadoNoSoloEvento BEFORE INSERT ON Tacto
WHEN NEW.Tipo = 'Normal' AND NEW.EstadoNuevoId IS NOT NULL
 AND (SELECT Tipo FROM EstadoReproductivo WHERE Id = NEW.EstadoNuevoId) IS 'SoloEvento'
BEGIN
    SELECT RAISE(ABORT, 'Un tacto no puede dejar a la vaca en un evento de tipo solo evento.');
END;");

            // ---- Inmutabilidad de Rechazo: recreada para incluir EventoId (el de EsperaYRechazo enumera columnas) ----
            // Sin esto, anular un rechazo podría cambiarle el evento de paso. Es el mismo trigger con una línea más.
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
    AND OLD.Observaciones IS NEW.Observaciones)
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo guardado no se puede modificar; solo se puede anular.');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El proyecto no baja versiones: las bases solo avanzan (hay backup previo en cada actualización).
        }
    }
}
