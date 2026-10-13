using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamPrivacyJoinAndReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- channel_members: CanWrite/CanManage → ReadOnly (inverted semantics: ReadOnly = NOT CanWrite) ----
            // Dry-run counts (run manually before deploying; informational only, nothing below depends on them):
            //   SELECT count(*) FROM channel_members WHERE "CanWrite" = false;                      -- becomes ReadOnly = true
            //   SELECT count(*) FROM channel_members WHERE "CanWrite" = false AND "CanManage" = true; -- manage-only grants lose manage; stay view-only
            //   SELECT count(*) FROM channel_members WHERE "CanManage" = true;                      -- grants that lose CanManage (Admin+ manage membership now)
            // The old CanManage flag is dropped: managing channel membership is Admin+ only.
            migrationBuilder.AddColumn<bool>(
                name: "ReadOnly",
                table: "channel_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE channel_members SET "ReadOnly" = NOT "CanWrite";""");

            migrationBuilder.DropColumn(
                name: "CanWrite",
                table: "channel_members");

            migrationBuilder.DropColumn(
                name: "CanManage",
                table: "channel_members");

            migrationBuilder.AddColumn<int>(
                name: "DefaultJoinRole",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "JoinMode",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "JoinTokenCiphertext",
                table: "teams",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JoinTokenExpiresAt",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JoinTokenHash",
                table: "teams",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JoinTokenPrefix",
                table: "teams",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "team_join_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CooldownUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_join_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_join_requests_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_teams_join_token_hash",
                table: "teams",
                column: "JoinTokenHash",
                unique: true,
                filter: "\"JoinTokenHash\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_teams_default_join_role",
                table: "teams",
                sql: "\"DefaultJoinRole\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_team_join_requests_TeamId_Status",
                table: "team_join_requests",
                columns: new[] { "TeamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_team_join_requests_TeamId_UserId_Status",
                table: "team_join_requests",
                columns: new[] { "TeamId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_team_join_requests_UserId",
                table: "team_join_requests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_join_requests");

            migrationBuilder.DropIndex(
                name: "ux_teams_join_token_hash",
                table: "teams");

            migrationBuilder.DropCheckConstraint(
                name: "ck_teams_default_join_role",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "DefaultJoinRole",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "JoinMode",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "JoinTokenCiphertext",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "JoinTokenExpiresAt",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "JoinTokenHash",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "JoinTokenPrefix",
                table: "teams");

            migrationBuilder.AddColumn<bool>(
                name: "CanWrite",
                table: "channel_members",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""UPDATE channel_members SET "CanWrite" = NOT "ReadOnly";""");

            migrationBuilder.DropColumn(
                name: "ReadOnly",
                table: "channel_members");

            // CanManage cannot be reconstructed (data dropped in Up); restored as false.
            migrationBuilder.AddColumn<bool>(
                name: "CanManage",
                table: "channel_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
