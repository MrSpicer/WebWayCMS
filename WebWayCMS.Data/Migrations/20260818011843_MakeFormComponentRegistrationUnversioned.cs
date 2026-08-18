using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWayCMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeFormComponentRegistrationUnversioned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FormComponentRegistrations_ContentVersions_VersionId",
                table: "FormComponentRegistrations");

            migrationBuilder.RenameColumn(
                name: "VersionId",
                table: "FormComponentRegistrations",
                newName: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Id",
                table: "FormComponentRegistrations",
                newName: "VersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_FormComponentRegistrations_ContentVersions_VersionId",
                table: "FormComponentRegistrations",
                column: "VersionId",
                principalTable: "ContentVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
