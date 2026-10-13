using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepliesMentionsNotificationsFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToMessageId",
                table: "channel_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_mentions",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_mentions", x => new { x.MessageId, x.UserId });
                    table.ForeignKey(
                        name: "FK_message_mentions_channel_messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "channel_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    BodyPreview = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_messages_reply_to",
                table: "channel_messages",
                column: "ReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_user",
                table: "message_mentions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_created",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_read",
                table: "notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_channel_messages_channel_messages_ReplyToMessageId",
                table: "channel_messages",
                column: "ReplyToMessageId",
                principalTable: "channel_messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channel_messages_channel_messages_ReplyToMessageId",
                table: "channel_messages");

            migrationBuilder.DropTable(
                name: "message_mentions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_channel_messages_reply_to",
                table: "channel_messages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "channel_messages");
        }
    }
}
