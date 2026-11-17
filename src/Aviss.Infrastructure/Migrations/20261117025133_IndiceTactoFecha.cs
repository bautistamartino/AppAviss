using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aviss.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndiceTactoFecha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec 003 (RF-37): el avance del día y los últimos movimientos filtran los tactos por rango de fechas; sin
            // este índice SQLite recorre todos los tactos (con 100.000 registros pasaba de los 200 ms). Solo agrega un
            // índice: no reconstruye la tabla, así que sus triggers siguen vivos. No hay SQL a mano en esta migración.
            migrationBuilder.CreateIndex(
                name: "IX_Tacto_FechaHoraUtc",
                table: "Tacto",
                column: "FechaHoraUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tacto_FechaHoraUtc",
                table: "Tacto");
        }
    }
}
