using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BabyNamePicker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Names",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    MaleShare = table.Column<double>(type: "REAL", precision: 5, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Names", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nicknames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nicknames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NameYearStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Sex = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NameYearStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NameYearStats_Names_NameId",
                        column: x => x.NameId,
                        principalTable: "Names",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BabyNameNickname",
                columns: table => new
                {
                    NamesId = table.Column<int>(type: "INTEGER", nullable: false),
                    NicknamesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BabyNameNickname", x => new { x.NamesId, x.NicknamesId });
                    table.ForeignKey(
                        name: "FK_BabyNameNickname_Names_NamesId",
                        column: x => x.NamesId,
                        principalTable: "Names",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BabyNameNickname_Nicknames_NicknamesId",
                        column: x => x.NicknamesId,
                        principalTable: "Nicknames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BabyNameNickname_NicknamesId",
                table: "BabyNameNickname",
                column: "NicknamesId");

            migrationBuilder.CreateIndex(
                name: "IX_Names_Name",
                table: "Names",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NameYearStats_NameId_Year_Sex",
                table: "NameYearStats",
                columns: new[] { "NameId", "Year", "Sex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nicknames_Value",
                table: "Nicknames",
                column: "Value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BabyNameNickname");

            migrationBuilder.DropTable(
                name: "NameYearStats");

            migrationBuilder.DropTable(
                name: "Nicknames");

            migrationBuilder.DropTable(
                name: "Names");
        }
    }
}
