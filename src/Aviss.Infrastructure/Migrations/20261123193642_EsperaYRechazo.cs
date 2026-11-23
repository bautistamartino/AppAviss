using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EsperaYRechazo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Espera",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    ResueltaEnUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ResueltaPorUsuarioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RegistroResolucionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Espera", x => x.Id);
                    table.CheckConstraint("CK_Espera_Estado", "Estado IN ('Pendiente','Resuelta','Salteada')");
                    table.CheckConstraint("CK_Espera_Resolucion", "(Estado = 'Pendiente' AND ResueltaEnUtc IS NULL AND ResueltaPorUsuarioId IS NULL AND RegistroResolucionId IS NULL) OR (Estado = 'Resuelta' AND ResueltaEnUtc IS NOT NULL AND ResueltaPorUsuarioId IS NOT NULL AND RegistroResolucionId IS NOT NULL) OR (Estado = 'Salteada' AND ResueltaEnUtc IS NOT NULL AND ResueltaPorUsuarioId IS NOT NULL AND RegistroResolucionId IS NULL)");
                    table.ForeignKey(
                        name: "FK_Espera_Animal_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Espera_Usuario_ResueltaPorUsuarioId",
                        column: x => x.ResueltaPorUsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Espera_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rechazo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    OrdenInsercion = table.Column<long>(type: "INTEGER", nullable: false),
                    Observaciones = table.Column<string>(type: "TEXT", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Anulado = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnuladoEnUtc = table.Column<string>(type: "TEXT", nullable: true),
                    AnuladoPorUsuarioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rechazo", x => x.Id);
                    table.CheckConstraint("CK_Rechazo_Anulacion", "(Anulado = 0 AND AnuladoEnUtc IS NULL AND AnuladoPorUsuarioId IS NULL) OR (Anulado = 1 AND AnuladoEnUtc IS NOT NULL AND AnuladoPorUsuarioId IS NOT NULL)");
                    table.CheckConstraint("CK_Rechazo_Anulado", "Anulado IN (0,1)");
                    table.ForeignKey(
                        name: "FK_Rechazo_Animal_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rechazo_Usuario_AnuladoPorUsuarioId",
                        column: x => x.AnuladoPorUsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rechazo_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Espera_AnimalId",
                table: "Espera",
                column: "AnimalId",
                unique: true,
                filter: "Estado = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "IX_Espera_ResueltaPorUsuarioId",
                table: "Espera",
                column: "ResueltaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Espera_UsuarioId",
                table: "Espera",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Rechazo_AnimalId_FechaHoraUtc_OrdenInsercion",
                table: "Rechazo",
                columns: new[] { "AnimalId", "FechaHoraUtc", "OrdenInsercion" });

            migrationBuilder.CreateIndex(
                name: "IX_Rechazo_AnuladoPorUsuarioId",
                table: "Rechazo",
                column: "AnuladoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Rechazo_OrdenInsercion",
                table: "Rechazo",
                column: "OrdenInsercion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rechazo_UsuarioId",
                table: "Rechazo",
                column: "UsuarioId");

            // =====================================================================================================
            // A PARTIR DE ACÁ, TODO A MANO (EF no sabe expresarlo). Como en "Inicial" y "Mangas", este SQL es literal y
            // NO se comparte con otro código: una migración ya publicada no debe cambiar si alguien edita código después.
            // =====================================================================================================

            // ---- Historial: nuevo CHECK con las cinco acciones de la spec 004 (RF-12), triggers recreados ----
            // Igual que en "Mangas": SQLite no cambia un CHECK en el lugar, y la reconstrucción automática de EF corre
            // al final y se llevaría los triggers de Historial. Por eso va a mano, con los triggers recreados.
            ReconstruirHistorial(migrationBuilder,
                "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta','MarcaPasada','AnulacionMarca','MangaAbierta','MangaCerrada','AnulacionCierre','EsperaCreada','EsperaResuelta','EsperaSalteada','RechazoCreado','RechazoAnulado')");

            // ---- Espera: inmutable salvo Pendiente -> Resuelta/Salteada, una sola vez; sin borrar (RF-13) ----
            // Se compara con IS para que NULL = NULL. Los datos de resolución (ResueltaEnUtc, ResueltaPorUsuarioId,
            // RegistroResolucionId) y ActualizadoEnUtc sí pueden cambiar en esa transición; su coherencia con el
            // Estado la exige el CHECK CK_Espera_Resolucion. Una espera cerrada no vuelve a cambiar (OLD.Estado = 'Pendiente').
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Espera_NoModificar BEFORE UPDATE ON Espera
WHEN NOT (
        OLD.Estado = 'Pendiente' AND NEW.Estado IN ('Resuelta','Salteada')
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.UsuarioId IS NEW.UsuarioId
    AND OLD.DispositivoId IS NEW.DispositivoId)
BEGIN
    SELECT RAISE(ABORT, 'Una espera guardada no se puede modificar; solo se puede resolver o saltear, una vez.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Espera_NoBorrar BEFORE DELETE ON Espera
BEGIN
    SELECT RAISE(ABORT, 'Una espera no se puede borrar.');
END;");

            // ---- Rechazo: inmutable salvo Anulado 0 -> 1 (deshacer), sin borrar (RF-6) ----
            // AnuladoEnUtc, AnuladoPorUsuarioId y ActualizadoEnUtc cambian junto con la marca; el CHECK
            // CK_Rechazo_Anulacion exige que estén completos al anular. Nunca vuelve a 0 (OLD.Anulado = 0 es requisito).
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_NoModificar BEFORE UPDATE ON Rechazo
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.Observaciones IS NEW.Observaciones
    AND OLD.UsuarioId IS NEW.UsuarioId
    AND OLD.DispositivoId IS NEW.DispositivoId)
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo guardado no se puede modificar; solo se puede anular.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rechazo_NoBorrar BEFORE DELETE ON Rechazo
BEGIN
    SELECT RAISE(ABORT, 'Un rechazo no se puede borrar.');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lo hecho a mano se deshace primero; las tablas se borran después.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rechazo_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rechazo_NoModificar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Espera_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Espera_NoModificar;");

            migrationBuilder.DropTable(
                name: "Espera");

            migrationBuilder.DropTable(
                name: "Rechazo");

            // OJO: volver el CHECK de Historial al anterior ABORTA si ya hay registros con las acciones nuevas
            // (EsperaCreada, RechazoCreado, ...): no se pierden datos, simplemente no se puede volver atrás.
            ReconstruirHistorial(migrationBuilder,
                "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta','MarcaPasada','AnulacionMarca','MangaAbierta','MangaCerrada','AnulacionCierre')");
        }

        /// <summary>
        /// Reconstruye Historial con el CHECK de acciones indicado (copia los datos, borra la original, renombra,
        /// recrea índices) y vuelve a crear sus dos triggers. Mismo SQL que la migración "Mangas" (ver su Up).
        /// </summary>
        private static void ReconstruirHistorial(MigrationBuilder migrationBuilder, string checkAccion)
        {
            migrationBuilder.Sql(@"
CREATE TABLE ""Historial_nueva"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Historial"" PRIMARY KEY,
    ""Accion"" TEXT NOT NULL,
    ""Campo"" TEXT NOT NULL,
    ""CreadoEnUtc"" TEXT NOT NULL,
    ""Entidad"" TEXT NOT NULL,
    ""EntidadId"" TEXT NOT NULL,
    ""FechaHoraUtc"" TEXT NOT NULL,
    ""UsuarioId"" TEXT NOT NULL,
    ""ValorAnterior"" TEXT NULL,
    ""ValorNuevo"" TEXT NULL,
    CONSTRAINT ""CK_Historial_Accion"" CHECK (" + checkAccion + @"),
    CONSTRAINT ""FK_Historial_Usuario_UsuarioId"" FOREIGN KEY (""UsuarioId"") REFERENCES ""Usuario"" (""Id"") ON DELETE RESTRICT
);");
            migrationBuilder.Sql(@"
INSERT INTO ""Historial_nueva"" (""Id"", ""Accion"", ""Campo"", ""CreadoEnUtc"", ""Entidad"", ""EntidadId"", ""FechaHoraUtc"", ""UsuarioId"", ""ValorAnterior"", ""ValorNuevo"")
SELECT ""Id"", ""Accion"", ""Campo"", ""CreadoEnUtc"", ""Entidad"", ""EntidadId"", ""FechaHoraUtc"", ""UsuarioId"", ""ValorAnterior"", ""ValorNuevo""
FROM ""Historial"";");
            // DROP TABLE no dispara los triggers de borrado, y se lleva los triggers e índices de la tabla.
            migrationBuilder.Sql(@"DROP TABLE ""Historial"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Historial_nueva"" RENAME TO ""Historial"";");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Historial_Entidad_EntidadId"" ON ""Historial"" (""Entidad"", ""EntidadId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Historial_UsuarioId"" ON ""Historial"" (""UsuarioId"");");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Historial_NoModificar BEFORE UPDATE ON Historial
BEGIN
    SELECT RAISE(ABORT, 'El historial no se puede modificar.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Historial_NoBorrar BEFORE DELETE ON Historial
BEGIN
    SELECT RAISE(ABORT, 'El historial no se puede borrar.');
END;");
        }
    }
}
