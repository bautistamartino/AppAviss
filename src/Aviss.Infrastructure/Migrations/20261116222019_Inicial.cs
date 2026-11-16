using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Establecimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    Notas = table.Column<string>(type: "TEXT", nullable: true),
                    Activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Establecimiento", x => x.Id);
                    table.CheckConstraint("CK_Establecimiento_Nombre", "length(trim(Nombre)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "EstadoReproductivo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    CodigoDairyComp = table.Column<string>(type: "TEXT", nullable: false),
                    Activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    NombreClave = table.Column<string>(type: "TEXT", nullable: true, computedColumnSql: "replace(replace(replace(replace(replace(replace(replace(upper(Nombre),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü')", stored: true),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadoReproductivo", x => x.Id);
                    table.CheckConstraint("CK_Estado_Codigo", "length(Codigo) > 0 AND Codigo NOT GLOB '*[^A-Z0-9_]*'");
                });

            migrationBuilder.CreateTable(
                name: "Usuario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    Matricula = table.Column<string>(type: "TEXT", nullable: true),
                    Activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuario", x => x.Id);
                    table.CheckConstraint("CK_Usuario_Matricula", "Matricula IS NULL OR length(Matricula) <= 30");
                    table.CheckConstraint("CK_Usuario_Nombre", "length(trim(Nombre)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Rodeo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EstablecimientoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    FechaImportacionUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivoOrigenNombre = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivoOrigenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivoOrigenBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PerfilExcel = table.Column<string>(type: "TEXT", nullable: false),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    Activo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rodeo", x => x.Id);
                    table.CheckConstraint("CK_Rodeo_Archivo", "length(ArchivoOrigenBlob) > 0");
                    table.CheckConstraint("CK_Rodeo_Estado", "Estado IN ('Abierto','Exportado')");
                    table.CheckConstraint("CK_Rodeo_Hash", "length(ArchivoOrigenHash) = 64");
                    table.ForeignKey(
                        name: "FK_Rodeo_Establecimiento_EstablecimientoId",
                        column: x => x.EstablecimientoId,
                        principalTable: "Establecimiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Historial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Entidad = table.Column<string>(type: "TEXT", nullable: false),
                    EntidadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Campo = table.Column<string>(type: "TEXT", nullable: false),
                    ValorAnterior = table.Column<string>(type: "TEXT", nullable: true),
                    ValorNuevo = table.Column<string>(type: "TEXT", nullable: true),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Accion = table.Column<string>(type: "TEXT", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historial", x => x.Id);
                    table.CheckConstraint("CK_Historial_Accion", "Accion IN ('Alta','Edicion','Tacto','AnulacionTacto','AnulacionAlta')");
                    table.ForeignKey(
                        name: "FK_Historial_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Animal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RodeoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NumeroCaravana = table.Column<string>(type: "TEXT", nullable: false),
                    IdElectronico = table.Column<string>(type: "TEXT", nullable: true),
                    EstadoReproductivoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FechaUltimoTactoUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Observaciones = table.Column<string>(type: "TEXT", nullable: true),
                    Origen = table.Column<string>(type: "TEXT", nullable: false),
                    Anulado = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnuladoEnUtc = table.Column<string>(type: "TEXT", nullable: true),
                    DatosOriginalesJson = table.Column<string>(type: "TEXT", nullable: true),
                    NumeroCaravanaClave = table.Column<string>(type: "TEXT", nullable: true, computedColumnSql: "replace(replace(replace(replace(replace(replace(replace(upper(NumeroCaravana),'ñ','Ñ'),'á','Á'),'é','É'),'í','Í'),'ó','Ó'),'ú','Ú'),'ü','Ü')", stored: true),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Animal", x => x.Id);
                    table.CheckConstraint("CK_Animal_Anulado", "Anulado IN (0,1) AND (Anulado = 0 OR Origen = 'AltaEnCampo')");
                    table.CheckConstraint("CK_Animal_Caravana", "length(NumeroCaravana) BETWEEN 1 AND 20 AND NumeroCaravana = trim(NumeroCaravana)");
                    table.CheckConstraint("CK_Animal_IdElectronico", "IdElectronico IS NULL OR (length(IdElectronico) BETWEEN 1 AND 32 AND instr(IdElectronico, ' ') = 0)");
                    table.CheckConstraint("CK_Animal_Origen", "Origen IN ('Importado','AltaEnCampo')");
                    table.ForeignKey(
                        name: "FK_Animal_EstadoReproductivo_EstadoReproductivoId",
                        column: x => x.EstadoReproductivoId,
                        principalTable: "EstadoReproductivo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Animal_Rodeo_RodeoId",
                        column: x => x.RodeoId,
                        principalTable: "Rodeo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tacto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnimalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FechaHoraUtc = table.Column<string>(type: "TEXT", nullable: false),
                    OrdenInsercion = table.Column<long>(type: "INTEGER", nullable: false),
                    EstadoAnteriorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EstadoNuevoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DiasPrenez = table.Column<int>(type: "INTEGER", nullable: true),
                    Observaciones = table.Column<string>(type: "TEXT", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispositivoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    TactoAnuladoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Anulado = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActualizadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tacto", x => x.Id);
                    table.UniqueConstraint("AK_Tacto_AnimalId_Id", x => new { x.AnimalId, x.Id });
                    table.CheckConstraint("CK_Tacto_Anulacion", "(Tipo = 'Anulacion') = (TactoAnuladoId IS NOT NULL)");
                    table.CheckConstraint("CK_Tacto_Dias", "DiasPrenez IS NULL OR DiasPrenez BETWEEN 0 AND 300");
                    table.CheckConstraint("CK_Tacto_EstadoNuevo", "Tipo = 'Anulacion' OR EstadoNuevoId IS NOT NULL");
                    table.CheckConstraint("CK_Tacto_NoAnulaAnulacion", "Tipo = 'Normal' OR Anulado = 0");
                    table.CheckConstraint("CK_Tacto_Tipo", "Tipo IN ('Normal','Anulacion')");
                    table.ForeignKey(
                        name: "FK_Tacto_Animal_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tacto_EstadoReproductivo_EstadoAnteriorId",
                        column: x => x.EstadoAnteriorId,
                        principalTable: "EstadoReproductivo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tacto_EstadoReproductivo_EstadoNuevoId",
                        column: x => x.EstadoNuevoId,
                        principalTable: "EstadoReproductivo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tacto_Tacto_AnimalId_TactoAnuladoId",
                        columns: x => new { x.AnimalId, x.TactoAnuladoId },
                        principalTable: "Tacto",
                        principalColumns: new[] { "AnimalId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tacto_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Animal_EstadoReproductivoId",
                table: "Animal",
                column: "EstadoReproductivoId");

            migrationBuilder.CreateIndex(
                name: "IX_Animal_RodeoId_IdElectronico",
                table: "Animal",
                columns: new[] { "RodeoId", "IdElectronico" },
                unique: true,
                filter: "IdElectronico IS NOT NULL AND Anulado = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Animal_RodeoId_NumeroCaravanaClave",
                table: "Animal",
                columns: new[] { "RodeoId", "NumeroCaravanaClave" },
                unique: true,
                filter: "Anulado = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EstadoReproductivo_Codigo",
                table: "EstadoReproductivo",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstadoReproductivo_CodigoDairyComp",
                table: "EstadoReproductivo",
                column: "CodigoDairyComp",
                unique: true,
                filter: "Activo = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EstadoReproductivo_NombreClave",
                table: "EstadoReproductivo",
                column: "NombreClave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Historial_Entidad_EntidadId",
                table: "Historial",
                columns: new[] { "Entidad", "EntidadId" });

            migrationBuilder.CreateIndex(
                name: "IX_Historial_UsuarioId",
                table: "Historial",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Rodeo_EstablecimientoId",
                table: "Rodeo",
                column: "EstablecimientoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_AnimalId_FechaHoraUtc_OrdenInsercion",
                table: "Tacto",
                columns: new[] { "AnimalId", "FechaHoraUtc", "OrdenInsercion" });

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_AnimalId_TactoAnuladoId",
                table: "Tacto",
                columns: new[] { "AnimalId", "TactoAnuladoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_EstadoAnteriorId",
                table: "Tacto",
                column: "EstadoAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_EstadoNuevoId",
                table: "Tacto",
                column: "EstadoNuevoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_OrdenInsercion",
                table: "Tacto",
                column: "OrdenInsercion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_TactoAnuladoId",
                table: "Tacto",
                column: "TactoAnuladoId",
                unique: true,
                filter: "TactoAnuladoId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tacto_UsuarioId",
                table: "Tacto",
                column: "UsuarioId");

            // =====================================================================================================
            // SQL ESCRITO A MANO: triggers de inmutabilidad y de "no borrar".
            // EF no sabe expresarlos, así que van acá como SQL literal. Este texto NO se comparte con otro código a
            // propósito: una migración ya publicada no debe cambiar si alguien edita código después.
            // Los triggers valen aunque el intento de modificar venga de fuera de la app (RF-11, RF-12, RF-15, RF-24).
            // RAISE(ABORT, ...) corta la sentencia y deshace sus cambios.
            // Las reglas de CHECK, los índices únicos parciales (HasFilter), la columna generada de la clave de
            // caravana/nombre y la FK compuesta de la anulación las generó EF arriba desde las configuraciones.
            // =====================================================================================================

            // ---- Historial: solo se agrega (RF-11) ----
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

            // ---- Tacto: inmutable, salvo pasar Anulado de 0 a 1 (RF-12) ----
            // Se aborta todo UPDATE excepto: Anulado 0 -> 1 y que TODAS las demás columnas queden iguales
            // (se comparan con IS para que NULL = NULL). ActualizadoEnUtc sí puede cambiar junto con la marca.
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
    AND OLD.UsuarioId IS NEW.UsuarioId
    AND OLD.DispositivoId IS NEW.DispositivoId
    AND OLD.Tipo IS NEW.Tipo
    AND OLD.TactoAnuladoId IS NEW.TactoAnuladoId)
BEGIN
    SELECT RAISE(ABORT, 'Un tacto guardado no se puede modificar; solo se puede anular.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Tacto_NoBorrar BEFORE DELETE ON Tacto
BEGIN
    SELECT RAISE(ABORT, 'Un tacto no se puede borrar.');
END;");

            // ---- Animal: columnas originales inmutables y sin borrar (RF-3, RF-15, RF-25) ----
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Animal_NoBorrar BEFORE DELETE ON Animal
BEGIN
    SELECT RAISE(ABORT, 'Un animal no se puede borrar.');
END;");
            // Origen, columnas originales del Excel y rodeo no cambian nunca; la caravana de un importado tampoco;
            // y una vez anulado un animal no se puede reactivar (1 -> 0).
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Animal_Inmutables BEFORE UPDATE ON Animal
WHEN NEW.Origen IS NOT OLD.Origen
  OR NEW.DatosOriginalesJson IS NOT OLD.DatosOriginalesJson
  OR NEW.RodeoId IS NOT OLD.RodeoId
  OR (OLD.Origen = 'Importado' AND NEW.NumeroCaravana IS NOT OLD.NumeroCaravana)
  OR (OLD.Anulado = 1 AND NEW.Anulado = 0)
BEGIN
    SELECT RAISE(ABORT, 'Ese dato del animal no se puede modificar.');
END;");

            // ---- Sin borrados: solo bajas (RF-24) ----
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Establecimiento_NoBorrar BEFORE DELETE ON Establecimiento
BEGIN
    SELECT RAISE(ABORT, 'Un establecimiento no se puede borrar; solo darlo de baja.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Rodeo_NoBorrar BEFORE DELETE ON Rodeo
BEGIN
    SELECT RAISE(ABORT, 'Un rodeo no se puede borrar; solo darlo de baja.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Usuario_NoBorrar BEFORE DELETE ON Usuario
BEGIN
    SELECT RAISE(ABORT, 'Un usuario no se puede borrar; solo darlo de baja.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_EstadoReproductivo_NoBorrar BEFORE DELETE ON EstadoReproductivo
BEGIN
    SELECT RAISE(ABORT, 'Un estado reproductivo no se puede borrar; solo desactivarlo.');
END;");

            // ---- Usuario Sin asignar: no se da de baja si es el único activo (RF-7b) ----
            // Su Id fijo es Usuario.SinAsignarId (00000000-0000-0000-0000-000000000001).
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Usuario_SinAsignarProtegido BEFORE UPDATE OF Activo ON Usuario
WHEN OLD.Id = '00000000-0000-0000-0000-000000000001'
 AND NEW.Activo = 0
 AND NOT EXISTS (SELECT 1 FROM Usuario WHERE Activo = 1 AND Id <> OLD.Id)
BEGIN
    SELECT RAISE(ABORT, 'El usuario Sin asignar no se puede dar de baja si no hay otro usuario activo.');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los triggers se borran explícitamente (SQLite también los borraría junto con sus tablas).
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Usuario_SinAsignarProtegido;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_EstadoReproductivo_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Usuario_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Rodeo_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Establecimiento_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Animal_Inmutables;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Animal_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Tacto_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Tacto_NoModificar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Historial_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Historial_NoModificar;");

            migrationBuilder.DropTable(
                name: "Historial");

            migrationBuilder.DropTable(
                name: "Tacto");

            migrationBuilder.DropTable(
                name: "Animal");

            migrationBuilder.DropTable(
                name: "Usuario");

            migrationBuilder.DropTable(
                name: "EstadoReproductivo");

            migrationBuilder.DropTable(
                name: "Rodeo");

            migrationBuilder.DropTable(
                name: "Establecimiento");
        }
    }
}
