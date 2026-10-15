using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ListingKeysetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContainerId",
                table: "folders",
                type: "uuid",
                nullable: false,
                computedColumnSql: "COALESCE(\"ChannelId\", \"OwnerUserId\")",
                stored: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContainerId",
                table: "files",
                type: "uuid",
                nullable: false,
                computedColumnSql: "COALESCE(\"ChannelId\", \"OwnerUserId\")",
                stored: true);

            // Keyset listing: one index per sort, keyed by container + folder, live rows only. Sort expressions must
            // match ItemListing exactly (lower(Name), lower(ContentType)) so Postgres can walk the index in order.
            migrationBuilder.Sql("""
                CREATE INDEX ix_files_list_name ON files ("ContainerId", "FolderId", lower("Name"), "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_files_list_size ON files ("ContainerId", "FolderId", "SizeBytes", "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_files_list_updated ON files ("ContainerId", "FolderId", "UpdatedAt", "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_files_list_created ON files ("ContainerId", "FolderId", "CreatedAt", "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_files_list_type ON files ("ContainerId", "FolderId", lower("ContentType"), lower("Name"), "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_files_trash ON files ("ContainerId", "DeletedAt") WHERE "DeletedAt" IS NOT NULL;
                CREATE INDEX ix_folders_list_name ON folders ("ContainerId", "ParentId", lower("Name"), "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_folders_list_updated ON folders ("ContainerId", "ParentId", "UpdatedAt", "Id") WHERE "DeletedAt" IS NULL;
                CREATE INDEX ix_folders_trash ON folders ("ContainerId", "DeletedAt") WHERE "DeletedAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_files_list_name;
                DROP INDEX IF EXISTS ix_files_list_size;
                DROP INDEX IF EXISTS ix_files_list_updated;
                DROP INDEX IF EXISTS ix_files_list_created;
                DROP INDEX IF EXISTS ix_files_list_type;
                DROP INDEX IF EXISTS ix_files_trash;
                DROP INDEX IF EXISTS ix_folders_list_name;
                DROP INDEX IF EXISTS ix_folders_list_updated;
                DROP INDEX IF EXISTS ix_folders_trash;
                """);

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "files");
        }
    }
}
