using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeletionLifecycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBatchId",
                table: "folders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBatchId",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletePhase",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletePhaseChangedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trash_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trash_batches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_folders_DeletedBatchId",
                table: "folders",
                column: "DeletedBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_files_DeletedBatchId",
                table: "files",
                column: "DeletedBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DeletePhase",
                table: "AspNetUsers",
                column: "DeletePhase");

            migrationBuilder.CreateIndex(
                name: "IX_trash_batches_DeletedAt",
                table: "trash_batches",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_trash_batches_State_DeletedAt",
                table: "trash_batches",
                columns: new[] { "State", "DeletedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_files_trash_batches_DeletedBatchId",
                table: "files",
                column: "DeletedBatchId",
                principalTable: "trash_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_folders_trash_batches_DeletedBatchId",
                table: "folders",
                column: "DeletedBatchId",
                principalTable: "trash_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_trash_batches_DeletedBatchId",
                table: "files");

            migrationBuilder.DropForeignKey(
                name: "FK_folders_trash_batches_DeletedBatchId",
                table: "folders");

            migrationBuilder.DropTable(
                name: "trash_batches");

            migrationBuilder.DropIndex(
                name: "IX_folders_DeletedBatchId",
                table: "folders");

            migrationBuilder.DropIndex(
                name: "IX_files_DeletedBatchId",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DeletePhase",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletedBatchId",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "DeletedBatchId",
                table: "files");

            migrationBuilder.DropColumn(
                name: "DeletePhase",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletePhaseChangedAt",
                table: "AspNetUsers");
        }
    }
}
