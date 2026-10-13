using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ON DELETE SET NULL (column list) requires PostgreSQL 15+. Fail early with a clear message.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF current_setting('server_version_num')::int < 150000 THEN
                    RAISE EXCEPTION 'Channeled public file links require PostgreSQL 15 or newer (found %).', version();
                  END IF;
                END
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "file_share_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_downloads = table.Column<int>(type: "integer", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_share_links", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_files_id_channel_id",
                table: "files",
                columns: new[] { "Id", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_share_links_channel_id",
                table: "file_share_links",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_share_links_file_id",
                table: "file_share_links",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_share_links_team_revoked",
                table: "file_share_links",
                columns: new[] { "team_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ux_file_share_links_token_hash",
                table: "file_share_links",
                column: "token_hash",
                unique: true);

            // Composite FK created via RAW SQL on purpose (not modelled in EF): EF's DeleteBehavior.SetNull
            // would null BOTH columns, but channel_id is NOT NULL. PG15+ supports a column list, so only
            // file_id is nulled when the file row is purged; ON UPDATE CASCADE carries channel_id when a file
            // moves channels (links are revoked in the same transaction; original_channel_id keeps history).
            migrationBuilder.Sql("""
                ALTER TABLE file_share_links
                  ADD CONSTRAINT fk_file_share_links_file_channel
                  FOREIGN KEY (file_id, channel_id)
                  REFERENCES files ("Id", "ChannelId")
                  ON UPDATE CASCADE
                  ON DELETE SET NULL (file_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table also drops fk_file_share_links_file_channel.
            migrationBuilder.DropTable(
                name: "file_share_links");

            migrationBuilder.DropIndex(
                name: "ux_files_id_channel_id",
                table: "files");
        }
    }
}
