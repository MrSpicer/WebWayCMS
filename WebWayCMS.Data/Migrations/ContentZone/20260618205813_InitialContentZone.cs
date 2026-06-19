using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWayCMS.Data.Migrations.ContentZone
{
    /// <inheritdoc />
    public partial class InitialContentZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentZones",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentZones", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_ContentZones_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentZoneAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentPageMasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentZoneId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentZoneAssignments", x => x.Id);
                    table.CheckConstraint("CK_ContentZoneAssignments_OneParent", "(\"ParentPageMasterId\" IS NOT NULL AND \"ParentZoneId\" IS NULL) OR (\"ParentPageMasterId\" IS NULL AND \"ParentZoneId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ContentZoneAssignments_ContentZones_ContentZoneId",
                        column: x => x.ContentZoneId,
                        principalTable: "ContentZones",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentZoneAssignments_ContentZones_ParentZoneId",
                        column: x => x.ParentZoneId,
                        principalTable: "ContentZones",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentZoneItems",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ComponentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ComponentPropertiesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentZoneItems", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_ContentZoneItems_ContentZones_ContentZoneId",
                        column: x => x.ContentZoneId,
                        principalTable: "ContentZones",
                        principalColumn: "ContentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentZoneItems_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentZoneAssignments_ContentZoneId",
                table: "ContentZoneAssignments",
                column: "ContentZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentZoneAssignments_PageSlot",
                table: "ContentZoneAssignments",
                columns: new[] { "ParentPageMasterId", "SlotName" },
                unique: true,
                filter: "\"ParentPageMasterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentZoneAssignments_ZoneSlot",
                table: "ContentZoneAssignments",
                columns: new[] { "ParentZoneId", "SlotName" },
                unique: true,
                filter: "\"ParentZoneId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentZoneItems_ContentZoneId_Ordinal",
                table: "ContentZoneItems",
                columns: new[] { "ContentZoneId", "Ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentZoneAssignments");

            migrationBuilder.DropTable(
                name: "ContentZoneItems");

            migrationBuilder.DropTable(
                name: "ContentZones");
        }
    }
}
