using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Huia.TodoApi.Data.Migrations.Huia
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "huia");

            migrationBuilder.CreateTable(
                name: "HuiaApplications",
                schema: "huia",
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
                name: "HuiaPhoneUsers",
                schema: "huia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Picture = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_HuiaPhoneUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaRoles",
                schema: "huia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaScopes",
                schema: "huia",
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
                schema: "huia",
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
                name: "HuiaUserClaims",
                schema: "huia",
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
                    table.PrimaryKey("PK_HuiaUserClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserLogins",
                schema: "huia",
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
                name: "HuiaUsers",
                schema: "huia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Picture = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    NormalizedPhoneNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_HuiaUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserTokens",
                schema: "huia",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                });

            migrationBuilder.CreateTable(
                name: "HuiaAuthorizations",
                schema: "huia",
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
                        principalSchema: "huia",
                        principalTable: "HuiaApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HuiaRoleClaims",
                schema: "huia",
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
                    table.PrimaryKey("PK_HuiaRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HuiaRoleClaims_HuiaRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "huia",
                        principalTable: "HuiaRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HuiaUserRoles",
                schema: "huia",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HuiaUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_HuiaUserRoles_HuiaRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "huia",
                        principalTable: "HuiaRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HuiaTokens",
                schema: "huia",
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
                        principalSchema: "huia",
                        principalTable: "HuiaApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HuiaTokens_HuiaAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "huia",
                        principalTable: "HuiaAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaApplications_ClientId",
                schema: "huia",
                table: "HuiaApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaAuthorizations_ApplicationId_Status_Subject_Type",
                schema: "huia",
                table: "HuiaAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaPhoneUsers_NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaPhoneUsers",
                column: "NormalizedPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "PhoneUser_EmailIndex",
                schema: "huia",
                table: "HuiaPhoneUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "PhoneUser_UserNameIndex",
                schema: "huia",
                table: "HuiaPhoneUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaRoleClaims_RoleId",
                schema: "huia",
                table: "HuiaRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "huia",
                table: "HuiaRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaScopes_Name",
                schema: "huia",
                table: "HuiaScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaSigningKeys_Usage_ExpiresAt",
                schema: "huia",
                table: "HuiaSigningKeys",
                columns: new[] { "Usage", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_ApplicationId_Status_Subject_Type",
                schema: "huia",
                table: "HuiaTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_AuthorizationId",
                schema: "huia",
                table: "HuiaTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaTokens_ReferenceId",
                schema: "huia",
                table: "HuiaTokens",
                column: "ReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUserRoles_RoleId",
                schema: "huia",
                table: "HuiaUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_HuiaUsers_NormalizedPhoneNumber",
                schema: "huia",
                table: "HuiaUsers",
                column: "NormalizedPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "StandardUser_EmailIndex",
                schema: "huia",
                table: "HuiaUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "StandardUser_UserNameIndex",
                schema: "huia",
                table: "HuiaUsers",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HuiaPhoneUsers",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaRoleClaims",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaScopes",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaSigningKeys",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaTokens",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaUserClaims",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaUserLogins",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaUserRoles",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaUsers",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaUserTokens",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaAuthorizations",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaRoles",
                schema: "huia");

            migrationBuilder.DropTable(
                name: "HuiaApplications",
                schema: "huia");
        }
    }
}
