using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplArchive.Messenger.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class InitialMessenger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "msg_direct",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserA = table.Column<Guid>(type: "uuid", nullable: false),
                    UserB = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_direct", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "msg_epoch_keys",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_epoch_keys", x => new { x.ConversationId, x.Epoch, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "msg_epochs",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_epochs", x => new { x.ConversationId, x.Epoch });
                });

            migrationBuilder.CreateTable(
                name: "msg_hidden",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_hidden", x => new { x.MessageId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "msg_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Epoch = table.Column<int>(type: "integer", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplyToId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttachmentDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "msg_receipts",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadUpTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_receipts", x => new { x.ConversationId, x.UserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_msg_direct_TenantId_UserA_UserB",
                table: "msg_direct",
                columns: new[] { "TenantId", "UserA", "UserB" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msg_epoch_keys_TenantId_UserId",
                table: "msg_epoch_keys",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_epochs_TenantId",
                table: "msg_epochs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_msg_hidden_TenantId",
                table: "msg_hidden",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_msg_messages_ConversationId_SentAt",
                table: "msg_messages",
                columns: new[] { "ConversationId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_msg_messages_TenantId",
                table: "msg_messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_msg_receipts_TenantId",
                table: "msg_receipts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "msg_direct");

            migrationBuilder.DropTable(
                name: "msg_epoch_keys");

            migrationBuilder.DropTable(
                name: "msg_epochs");

            migrationBuilder.DropTable(
                name: "msg_hidden");

            migrationBuilder.DropTable(
                name: "msg_messages");

            migrationBuilder.DropTable(
                name: "msg_receipts");
        }
    }
}
