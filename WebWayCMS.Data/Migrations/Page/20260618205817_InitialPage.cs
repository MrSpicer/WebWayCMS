using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWayCMS.Data.Migrations.Page
{
    /// <inheritdoc />
    public partial class InitialPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Route = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ControllerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ViewName = table.Column<string>(type: "text", nullable: true),
                    ConfigurationJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_Pages_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Route",
                table: "Pages",
                column: "Route");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pages");
        }
    }
}