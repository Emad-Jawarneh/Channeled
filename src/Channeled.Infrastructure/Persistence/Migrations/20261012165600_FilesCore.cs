using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilesCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UsageBytes",
                table: "teams",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => x.Id);
                    table.CheckConstraint("ck_folders_scope", "((\"ChannelId\" IS NOT NULL AND \"OwnerUserId\" IS NULL) OR (\"ChannelId\" IS NULL AND \"OwnerUserId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_folders_folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MultipartId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CompletedFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.CheckConstraint("ck_files_scope", "((\"ChannelId\" IS NOT NULL AND \"OwnerUserId\" IS NULL) OR (\"ChannelId\" IS NULL AND \"OwnerUserId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_files_folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "upload_parts",
                columns: table => new
                {
                    UploadSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    ETag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_parts", x => new { x.UploadSessionId, x.PartNumber });
                    table.ForeignKey(
                        name: "FK_upload_parts_upload_sessions_UploadSessionId",
                        column: x => x.UploadSessionId,
                        principalTable: "upload_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_files_ChannelId_FolderId_Name",
                table: "files",
                columns: new[] { "ChannelId", "FolderId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_files_FolderId",
                table: "files",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_files_OwnerUserId_FolderId_Name",
                table: "files",
                columns: new[] { "OwnerUserId", "FolderId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_files_UploadedBy",
                table: "files",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_folders_ChannelId_ParentId_Name",
                table: "folders",
                columns: new[] { "ChannelId", "ParentId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_folders_OwnerUserId_ParentId_Name",
                table: "folders",
                columns: new[] { "OwnerUserId", "ParentId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_folders_ParentId",
                table: "folders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_UserId",
                table: "upload_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_UserId_IdempotencyKey",
                table: "upload_sessions",
                columns: new[] { "UserId", "IdempotencyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "upload_parts");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropTable(
                name: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "UsageBytes",
                table: "teams");
        }
    }
}
