using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebWayCMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublicationEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    MasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentMasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CustomFields = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Content", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArticleLists",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleLists", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_ArticleLists_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    AuthorName = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    ArticleListMasterId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_Articles_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CMSRoutes",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DefaultsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ConstraintsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DataTokensJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    OwningContentMasterId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwningContentType = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CMSRoutes", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_CMSRoutes_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentBlocks",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentBlocks", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_ContentBlocks_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "PageControllerRegistrations",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ControllerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ControllerTypeName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IconClass = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationTypeName = table.Column<string>(type: "text", nullable: true),
                    PropertyDefinitionsJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageControllerRegistrations", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_PageControllerRegistrations_Content_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "WidgetRegistrations",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IconClass = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationTypeName = table.Column<string>(type: "text", nullable: true),
                    PropertyDefinitionsJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetRegistrations", x => x.ContentId);
                    table.ForeignKey(
                        name: "FK_WidgetRegistrations_Content_ContentId",
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
                name: "IX_Articles_ArticleListMasterId",
                table: "Articles",
                column: "ArticleListMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CMSRoutes_OwningContentMasterId",
                table: "CMSRoutes",
                column: "OwningContentMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_CMSRoutes_Pattern",
                table: "CMSRoutes",
                column: "Pattern",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Content_MasterId",
                table: "Content",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Content_ParentMasterId",
                table: "Content",
                column: "ParentMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Content_Slug",
                table: "Content",
                column: "Slug");

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

            migrationBuilder.CreateIndex(
                name: "IX_PageControllerRegistrations_Category",
                table: "PageControllerRegistrations",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_PageControllerRegistrations_ControllerName",
                table: "PageControllerRegistrations",
                column: "ControllerName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageControllerRegistrations_IsActive",
                table: "PageControllerRegistrations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WidgetRegistrations_Category",
                table: "WidgetRegistrations",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_WidgetRegistrations_ComponentName",
                table: "WidgetRegistrations",
                column: "ComponentName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WidgetRegistrations_IsActive",
                table: "WidgetRegistrations",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleLists");

            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CMSRoutes");

            migrationBuilder.DropTable(
                name: "ContentBlocks");

            migrationBuilder.DropTable(
                name: "ContentZoneAssignments");

            migrationBuilder.DropTable(
                name: "ContentZoneItems");

            migrationBuilder.DropTable(
                name: "PageControllerRegistrations");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "WidgetRegistrations");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ContentZones");

            migrationBuilder.DropTable(
                name: "Content");
        }
    }
}
