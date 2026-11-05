using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeClaimedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "claimed_at",
                table: "charges",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Las filas existentes nunca registraron su reclamo: created_at es el mejor dato disponible.
            // Con el valor por defecto (año 1), un Pending viejo se vería vencido desde siempre.
            migrationBuilder.Sql("UPDATE charges SET claimed_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "charges");
        }
    }
}
