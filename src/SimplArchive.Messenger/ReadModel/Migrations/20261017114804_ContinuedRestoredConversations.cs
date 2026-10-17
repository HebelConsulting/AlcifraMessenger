using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplArchive.Messenger.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class ContinuedRestoredConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContinuedInConversationId",
                table: "msg_restored_conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDirect",
                table: "msg_restored_conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_msg_restored_conversations_ContinuedInConversationId",
                table: "msg_restored_conversations",
                column: "ContinuedInConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_msg_restored_conversations_ContinuedInConversationId",
                table: "msg_restored_conversations");

            migrationBuilder.DropColumn(
                name: "ContinuedInConversationId",
                table: "msg_restored_conversations");

            migrationBuilder.DropColumn(
                name: "IsDirect",
                table: "msg_restored_conversations");
        }
    }
}
