using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_messages", x => x.Id);
                    table.CheckConstraint("ck_channel_messages_body_length", "char_length(\"Body\") <= 4000");
                    table.CheckConstraint("ck_channel_messages_deleted_scrubbed", "\"DeletedAt\" IS NULL OR \"Body\" = ''");
                    table.ForeignKey(
                        name: "FK_channel_messages_channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_messages_channel_created_id",
                table: "channel_messages",
                columns: new[] { "ChannelId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_messages_deleted_at",
                table: "channel_messages",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NOT NULL");

            // Channel kind is immutable: messages only ever live in text channels and files only in file
            // channels, so a kind flip would orphan data. The API never offers it; this makes it impossible
            // at the database level too.
            migrationBuilder.Sql("""
                CREATE FUNCTION channels_kind_immutable() RETURNS trigger AS $$
                BEGIN
                  IF NEW."Kind" IS DISTINCT FROM OLD."Kind" THEN
                    RAISE EXCEPTION 'channels.Kind is immutable' USING ERRCODE = 'check_violation';
                  END IF;
                  RETURN NEW;
                END
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_channels_kind_immutable
                BEFORE UPDATE OF "Kind" ON channels
                FOR EACH ROW EXECUTE FUNCTION channels_kind_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_channels_kind_immutable ON channels;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS channels_kind_immutable();");

            migrationBuilder.DropTable(
                name: "channel_messages");
        }
    }
}
