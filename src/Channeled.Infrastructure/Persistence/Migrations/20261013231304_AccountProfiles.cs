using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarContentType",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarStorageKey",
                table: "AspNetUsers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasChangedUsername",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsernameChangedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsernameSlug",
                table: "AspNetUsers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "address_rate_counters",
                columns: table => new
                {
                    AddressKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address_rate_counters", x => x.AddressKeyHash);
                });

            migrationBuilder.CreateTable(
                name: "operations_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SmtpVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SmtpSettingsFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pending_email_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NewEmailNormalized = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ConfirmTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CancelTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_email_changes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "username_reservations",
                columns: table => new
                {
                    NormalizedUsername = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PreviousOwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_username_reservations", x => x.NormalizedUsername);
                });

            // Backfill unique usernames from email local-part / id; sanitize empty DisplayName separately in app code on read.
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers" AS u
                SET "UsernameSlug" = LEFT(
                  REGEXP_REPLACE(
                    LOWER(SPLIT_PART(COALESCE(u."Email", u."Id"::text), '@', 1)),
                    '[^a-z0-9._-]',
                    '',
                    'g'
                  ) || SUBSTRING(REPLACE(u."Id"::text, '-', '') FROM 1 FOR 6),
                  30
                )
                WHERE u."UsernameSlug" IS NULL OR u."UsernameSlug" = '';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers" AS u
                SET "DisplayName" = COALESCE(NULLIF(BTRIM(u."DisplayName"), ''), SPLIT_PART(COALESCE(u."Email", 'user'), '@', 1), 'User')
                WHERE BTRIM(COALESCE(u."DisplayName", '')) = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UsernameSlug",
                table: "AspNetUsers",
                column: "UsernameSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_address_rate_counters_ExpiresAt",
                table: "address_rate_counters",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_pending_email_changes_CancelTokenHash",
                table: "pending_email_changes",
                column: "CancelTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_email_changes_ConfirmTokenHash",
                table: "pending_email_changes",
                column: "ConfirmTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_email_changes_NewEmailNormalized",
                table: "pending_email_changes",
                column: "NewEmailNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_pending_email_changes_UserId",
                table: "pending_email_changes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_username_reservations_ExpiresAt",
                table: "username_reservations",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_rate_counters");

            migrationBuilder.DropTable(
                name: "operations_settings");

            migrationBuilder.DropTable(
                name: "pending_email_changes");

            migrationBuilder.DropTable(
                name: "username_reservations");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_UsernameSlug",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AvatarContentType",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AvatarStorageKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HasChangedUsername",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UsernameChangedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UsernameSlug",
                table: "AspNetUsers");
        }
    }
}
