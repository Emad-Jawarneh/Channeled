using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_files_Name_trgm" ON files USING gin ("Name" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_channels_Name_trgm" ON channels USING gin ("Name" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS "IX_files_ChannelId_DeletedAt" ON files ("ChannelId", "DeletedAt");
                CREATE INDEX IF NOT EXISTS "IX_files_OwnerUserId_DeletedAt" ON files ("OwnerUserId", "DeletedAt");
                """);

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_CreatedAt",
                table: "jobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Type",
                table: "jobs",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_files_OwnerUserId_DeletedAt";
                DROP INDEX IF EXISTS "IX_files_ChannelId_DeletedAt";
                DROP INDEX IF EXISTS "IX_channels_Name_trgm";
                DROP INDEX IF EXISTS "IX_files_Name_trgm";
                """);

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
