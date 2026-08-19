using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWayCMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentSeedRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentSeedRecords",
                columns: table => new
                {
                    SeedId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AppliedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSeedRecords", x => x.SeedId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentSeedRecords_NodeId",
                table: "ContentSeedRecords",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentSeedRecords");
        }
    }
}
