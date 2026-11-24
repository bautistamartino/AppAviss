using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <summary>
    /// Spec 007 (exportación): tabla nueva <c>Exportacion</c>, la constancia de cada archivo de vuelta entregado. Es una migración
    /// SOLO ADITIVA: crea una tabla y su índice y no reconstruye ni toca ninguna tabla existente (tampoco <c>Historial</c>: no se
    /// agrega ninguna acción nueva, así su CHECK y sus triggers quedan como están). La tabla, la clave foránea, los CHECK
    /// (formato, huella de 64, nombre no vacío, cantidades) y el índice los generó EF desde la configuración; lo que EF no sabe
    /// expresar, los dos triggers de "solo se agrega", está escrito a mano abajo, como SQL literal (una migración publicada no debe cambiar).
    /// Atomicidad y copia previa (constitución §7): EF aplica la migración en una transacción y el arranque hace un backup antes y
    /// lo restaura si algo falla, así que o queda todo o no queda nada.
    /// </summary>
    public partial class Exportaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exportacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RodeoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FechaUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivoNombre = table.Column<string>(type: "TEXT", nullable: false),
                    Formato = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    CantidadFilas = table.Column<int>(type: "INTEGER", nullable: false),
                    CantidadVacasConEvento = table.Column<int>(type: "INTEGER", nullable: false),
                    CreadoEnUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exportacion", x => x.Id);
                    table.CheckConstraint("CK_Exportacion_Cantidades", "CantidadFilas >= 0 AND CantidadVacasConEvento >= 0 AND CantidadVacasConEvento <= CantidadFilas");
                    table.CheckConstraint("CK_Exportacion_Formato", "Formato IN ('Csv','Xls')");
                    table.CheckConstraint("CK_Exportacion_Hash", "length(Hash) = 64");
                    table.CheckConstraint("CK_Exportacion_Nombre", "length(trim(ArchivoNombre)) > 0");
                    table.ForeignKey(
                        name: "FK_Exportacion_Rodeo_RodeoId",
                        column: x => x.RodeoId,
                        principalTable: "Rodeo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exportacion_RodeoId_FechaUtc",
                table: "Exportacion",
                columns: new[] { "RodeoId", "FechaUtc" });

            // ---- Exportacion: solo se agrega, nunca se modifica ni se borra (RF-21) ----
            // Los triggers valen aunque el intento venga de fuera de la app. RAISE(ABORT, ...) corta la sentencia y deshace sus cambios.
            // Son lo último que crea la migración a propósito: el test de falla sabotea el nombre del último para comprobar que todo vuelve atrás.
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Exportacion_NoModificar BEFORE UPDATE ON Exportacion
BEGIN
    SELECT RAISE(ABORT, 'Una exportación guardada no se puede modificar.');
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Exportacion_NoBorrar BEFORE DELETE ON Exportacion
BEGIN
    SELECT RAISE(ABORT, 'Una exportación guardada no se puede borrar.');
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Exportacion_NoBorrar;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Exportacion_NoModificar;");
            migrationBuilder.DropTable(
                name: "Exportacion");
        }
    }
}
