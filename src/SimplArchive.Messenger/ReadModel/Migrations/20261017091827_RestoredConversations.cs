using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplArchive.Messenger.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class RestoredConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RestoredByUserId",
                table: "msg_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "msg_origin_members",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_origin_members", x => new { x.ConversationId, x.OriginUserId });
                });

            migrationBuilder.CreateTable(
                name: "msg_restored_conversations",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginInstance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RestoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_restored_conversations", x => x.ConversationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_msg_origin_members_TenantId",
                table: "msg_origin_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_msg_restored_conversations_TenantId_OriginConversationId",
                table: "msg_restored_conversations",
                columns: new[] { "TenantId", "OriginConversationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "msg_origin_members");

            migrationBuilder.DropTable(
                name: "msg_restored_conversations");

            migrationBuilder.DropColumn(
                name: "RestoredByUserId",
                table: "msg_messages");
        }
    }
}
