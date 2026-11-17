using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Mangas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Manga",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RodeoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    AbiertaPorUsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Manga", x => x.Id);
                    table.CheckConstraint("CK_Manga_Numero", "Numero >= 1");
                    table.ForeignKey(
                        name: "FK_Manga_Rodeo_RodeoId",
                        column: x => x.RodeoId,
                        principalTable: "Rodeo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Manga_Usuario_AbiertaPorUsuarioId",
                        column: x => x.AbiertaPorUsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CierreManga",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MangaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    CantidadAviss = table.Column<int>(type: "INTEGER", nullable: false),
                    CantidadConfirmada = table.Column<int>(type: "INTEGER", nullable: true),
                    Diferencia = table.Column<int>(type: "INTEGER", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Anulado = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CierreManga", x => x.Id);
                    table.CheckConstraint("CK_CierreManga_Anulado", "Anulado IN (0,1)");
                    table.CheckConstraint("CK_CierreManga_CantidadAviss", "CantidadAviss >= 0");
                    table.CheckConstraint("CK_CierreManga_CantidadConfirmada", "CantidadConfirmada IS NULL OR CantidadConfirmada >= 0");
                    table.CheckConstraint("CK_CierreManga_Diferencia", "(CantidadConfirmada IS NULL AND Diferencia IS NULL) OR (CantidadConfirmada IS NOT NULL AND Diferencia = CantidadConfirmada - CantidadAviss)");
                    table.CheckConstraint("CK_CierreManga_Tipo", "Tipo IN ('Manual','TerminoElTacto','Automatico')");
                    table.ForeignKey(
                        name: "FK_CierreManga_Manga_MangaId",
                        column: x => x.MangaId,
                        principalTable: "Manga",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CierreManga_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarcaPasada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MangaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    OrdenInsercion = table.Column<long>(type: "INTEGER", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Anulado = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarcaPasada", x => x.Id);
                    table.CheckConstraint("CK_MarcaPasada_Anulado", "Anulado IN (0,1)");
                    table.ForeignKey(
                        name: "FK_MarcaPasada_Animal_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarcaPasada_Manga_MangaId",
                        column: x => x.MangaId,
                        principalTable: "Manga",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarcaPasada_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CierreManga_MangaId",
                table: "CierreManga",
                column: "MangaId",
                unique: true,
                filter: "Anulado = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CierreManga_UsuarioId",
                table: "CierreManga",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Manga_AbiertaPorUsuarioId",
                table: "Manga",
                column: "AbiertaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Manga_RodeoId_Numero",
                table: "Manga",
                columns: new[] { "RodeoId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarcaPasada_AnimalId_FechaHoraUtc_OrdenInsercion",
                table: "MarcaPasada",
                columns: new[] { "AnimalId", "FechaHoraUtc", "OrdenInsercion" });

            migrationBuilder.CreateIndex(
                name: "IX_MarcaPasada_FechaHoraUtc",
                table: "MarcaPasada",
                column: "FechaHoraUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarcaPasada_MangaId_AnimalId",
                table: "MarcaPasada",
                columns: new[] { "MangaId", "AnimalId" },
                unique: true,
                filter: "Anulado = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MarcaPasada_OrdenInsercion",
                table: "MarcaPasada",
                column: "OrdenInsercion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarcaPasada_UsuarioId",
                table: "MarcaPasada",
                column: "UsuarioId");

            // =====================================================================================================
            // A PARTIR DE ACÁ, TODO A MANO (EF no sabe expresarlo). Como en "Inicial", este SQL es literal y NO se
            // comparte con otro código: una migración ya publicada no debe cambiar si alguien edita código después.
            // =====================================================================================================


            // ---- Historial: nuevo CHECK de acciones (RF-38) con sus triggers recreados ----
            // SQLite no puede cambiar un CHECK en el lugar: hay que reconstruir la tabla. EF lo haría solo, pero
            // sus reconstrucciones se ejecutan SIEMPRE al final de la migración (después de cualquier migrationBuilder.Sql),
            // y al borrar la tabla original se pierden sus triggers: el historial quedaría modificable sin que nada
            // fallara. Por eso la reconstrucción va escrita a mano, con los triggers recreados justo después
            // (texto exacto de "Inicial"). Historial no es referenciada por ninguna clave foránea: reconstruirla es seguro.
            ReconstruirHistorial(migrationBuilder,
                "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta','MarcaPasada','AnulacionMarca','MangaAbierta','MangaCerrada','AnulacionCierre')");

            // ---- Manga: no cambia nunca y no se borra (RF-4, RF-8) ----
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Manga_NoModificar BEFORE UPDATE ON Manga
BEGIN
    SELECT RAISE(ABORT, 'Una manga guardada no se puede modificar.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Manga_NoBorrar BEFORE DELETE ON Manga
BEGIN
    SELECT RAISE(ABORT, 'Una manga no se puede borrar.');
END;");

            // ---- CierreManga: inmutable salvo pasar Anulado de 0 a 1 (reabrir la manga), igual que Tacto (RF-8, RF-9) ----
            // Se compara con IS para que NULL = NULL. ActualizadoEnUtc sí puede cambiar junto con la marca.
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_CierreManga_NoModificar BEFORE UPDATE ON CierreManga
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.MangaId IS NEW.MangaId
    AND OLD.Tipo IS NEW.Tipo
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.CantidadAviss IS NEW.CantidadAviss
    AND OLD.CantidadConfirmada IS NEW.CantidadConfirmada
    AND OLD.Diferencia IS NEW.Diferencia
    AND OLD.UsuarioId IS NEW.UsuarioId
    AND OLD.DispositivoId IS NEW.DispositivoId)
BEGIN
    SELECT RAISE(ABORT, 'Un cierre de manga guardado no se puede modificar; solo se puede anular.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_CierreManga_NoBorrar BEFORE DELETE ON CierreManga
BEGIN
    SELECT RAISE(ABORT, 'Un cierre de manga no se puede borrar.');
END;");

            // ---- MarcaPasada: inmutable salvo Anulado 0 -> 1 (desmarcar), sin borrar (RF-20, RF-26) ----
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_NoModificar BEFORE UPDATE ON MarcaPasada
WHEN NOT (
        OLD.Anulado = 0 AND NEW.Anulado = 1
    AND OLD.Id IS NEW.Id
    AND OLD.CreadoEnUtc IS NEW.CreadoEnUtc
    AND OLD.AnimalId IS NEW.AnimalId
    AND OLD.MangaId IS NEW.MangaId
    AND OLD.FechaHoraUtc IS NEW.FechaHoraUtc
    AND OLD.OrdenInsercion IS NEW.OrdenInsercion
    AND OLD.UsuarioId IS NEW.UsuarioId
    AND OLD.DispositivoId IS NEW.DispositivoId)
BEGIN
    SELECT RAISE(ABORT, 'Una marca de pasada guardada no se puede modificar; solo se puede anular.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_NoBorrar BEFORE DELETE ON MarcaPasada
BEGIN
    SELECT RAISE(ABORT, 'Una marca de pasada no se puede borrar.');
END;");
            // La vaca y la manga tienen que ser del mismo rodeo: la base, no solo la app, impide marcar una vaca en la
            // manga de otro rodeo (RF-20). Si alguna de las dos no existe, la comparación falla igual y se aborta
            // (de todos modos las claves foráneas también lo rechazarían).
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_MarcaPasada_MismoRodeo BEFORE INSERT ON MarcaPasada
WHEN (SELECT RodeoId FROM Animal WHERE Id = NEW.AnimalId) IS NOT (SELECT RodeoId FROM Manga WHERE Id = NEW.MangaId)
BEGIN
    SELECT RAISE(ABORT, 'La vaca y la manga tienen que ser del mismo rodeo.');
END;");

            // ---- Usuario: el nombre de un veterinario no se repite (RF-1) ----
            // 1) Limpieza previa: si una base ya tiene nombres repetidos (sin distinguir mayúsculas ni espacios de los
            //    extremos), a los repetidos se les agrega ' (<rowid>)' para diferenciarlos; si no, el índice de abajo
            //    fallaría y la actualización dejaría la app sin arrancar. Se conserva uno por grupo: "Sin asignar" si
            //    está en el grupo y, si no, el de menor rowid. Hoy solo existe "Sin asignar", pero una base de
            //    desarrollo puede tener duplicados.
            migrationBuilder.Sql(@"
UPDATE Usuario
SET Nombre = Nombre || ' (' || rowid || ')'
WHERE Id <> '00000000-0000-0000-0000-000000000001'
  AND (
        rowid <> (SELECT MIN(u2.rowid) FROM Usuario u2
                  WHERE replace(replace(replace(replace(replace(replace(replace(upper(trim(u2.Nombre)),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü')
                      = replace(replace(replace(replace(replace(replace(replace(upper(trim(Usuario.Nombre)),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü'))
     OR EXISTS (SELECT 1 FROM Usuario s
                WHERE s.Id = '00000000-0000-0000-0000-000000000001'
                  AND replace(replace(replace(replace(replace(replace(replace(upper(trim(s.Nombre)),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü')
                    = replace(replace(replace(replace(replace(replace(replace(upper(trim(Usuario.Nombre)),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü'))
      );");
            // 2) Índice único por EXPRESIÓN (misma regla que Caravana.CalcularClave, aplicada a trim(Nombre)). No se usa
            //    una columna generada porque agregarla obligaría a EF a reconstruir Usuario (que tiene claves foráneas
            //    entrantes) y se perderían sus triggers. Ojo: EF no conoce este índice; si una migración futura
            //    reconstruye Usuario, hay que recrearlo (lo cubre UsuarioNombreUnicoTests).
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IX_Usuario_NombreClave ON Usuario (
    replace(replace(replace(replace(replace(replace(replace(upper(trim(Nombre)),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lo hecho a mano se deshace primero. Las tablas nuevas se borran más abajo.
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Usuario_NombreClave;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_MarcaPasada_MismoRodeo;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_MarcaPasada_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_MarcaPasada_NoModificar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_CierreManga_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_CierreManga_NoModificar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Manga_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Manga_NoModificar;");

            migrationBuilder.DropTable(
                name: "CierreManga");

            migrationBuilder.DropTable(
                name: "MarcaPasada");

            migrationBuilder.DropTable(
                name: "Manga");

            // OJO: volver el CHECK de Historial al anterior ABORTA si ya hay registros con las acciones nuevas
            // (MarcaPasada, MangaAbierta, ...): no se pierden datos, simplemente no se puede volver atrás.
            ReconstruirHistorial(migrationBuilder,
                "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta')");
        }

        /// <summary>
        /// Reconstruye Historial con el CHECK de acciones indicado (copia los datos, borra la original, renombra,
        /// recrea índices) y vuelve a crear sus dos triggers. Ver el comentario en Up.
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
