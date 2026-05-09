using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxFilingAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Pending"),
                    AssignedTo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TaxYear = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FilingType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "AssignedTo", "CreatedAt", "Description", "FilingType", "Status", "TaxYear", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "agent1@taxfiling.com", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Individual tax filing for FY2024", "Individual", "Pending", "2024", "File 1040 for John Smith", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "agent2@taxfiling.com", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Corporate tax filing for FY2024", "Corporate", "InProgress", "2024", "File 1120 for Acme Corp", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
