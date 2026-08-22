using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Huia.IdentityServer.Data.Migrations.Huia
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HuiaApplications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    NormalizedName = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaScopes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaScopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaSigningKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Usage = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProtectedPrivateKey = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaSigningKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUserRoles", x => new { x.UserId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "HuiaUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    NormalizedUserName = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordlessLoginEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false),
                    SecurityStamp = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Picture = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUserTokens", x => new { x.UserId, x.Provider, x.Name });
                });

            migrationBuilder.CreateTable(
                name: "HuiaAuthorizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HuiaAuthorizations_HuiaApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "HuiaApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HuiaTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HuiaTokens_HuiaApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "HuiaApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HuiaTokens_HuiaAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalTable: "HuiaAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaApplications_ClientId",
                table: "HuiaApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaAuthorizations_ApplicationId_Status_Subject_Type",
                table: "HuiaAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaRoles_NormalizedName",
                table: "HuiaRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaScopes_Name",
                table: "HuiaScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaSigningKeys_Usage_ExpiresAt",
                table: "HuiaSigningKeys",
                columns: new[] { "Usage", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_ApplicationId_Status_Subject_Type",
                table: "HuiaTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_AuthorizationId",
                table: "HuiaTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_ReferenceId",
                table: "HuiaTokens",
                column: "ReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUserLogins_UserId",
                table: "HuiaUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUsers_NormalizedEmail",
                table: "HuiaUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUsers_NormalizedPhoneNumber",
                table: "HuiaUsers",
                column: "NormalizedPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUsers_NormalizedUserName",
                table: "HuiaUsers",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuiaRoles");

            migrationBuilder.DropTable(
                name: "HuiaScopes");

            migrationBuilder.DropTable(
                name: "HuiaSigningKeys");

            migrationBuilder.DropTable(
                name: "HuiaTokens");

            migrationBuilder.DropTable(
                name: "HuiaUserLogins");

            migrationBuilder.DropTable(
                name: "HuiaUserRoles");

            migrationBuilder.DropTable(
                name: "HuiaUsers");

            migrationBuilder.DropTable(
                name: "HuiaUserTokens");

            migrationBuilder.DropTable(
                name: "HuiaAuthorizations");

            migrationBuilder.DropTable(
                name: "HuiaApplications");
        }
    }
}
