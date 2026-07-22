using System;
using BabyNamePicker.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BabyNamePicker.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722040000_AddNameMetadata")]
    /// <inheritdoc />
    public partial class AddNameMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NameMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Meaning = table.Column<string>(type: "TEXT", nullable: true),
                    Origins = table.Column<string>(type: "TEXT", nullable: true),
                    Pronunciation = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Themes = table.Column<string>(type: "TEXT", nullable: true),
                    Variants = table.Column<string>(type: "TEXT", nullable: true),
                    EnrichmentSource = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EnrichedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NameMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NameMetadata_Names_NameId",
                        column: x => x.NameId,
                        principalTable: "Names",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NameMetadata_NameId",
                table: "NameMetadata",
                column: "NameId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NameMetadata");
        }
    }
}
