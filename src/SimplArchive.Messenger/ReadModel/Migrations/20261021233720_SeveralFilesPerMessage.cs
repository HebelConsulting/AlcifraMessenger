using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplArchive.Messenger.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class SeveralFilesPerMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "msg_message_files",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_msg_message_files", x => new { x.MessageId, x.Position });
                });

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_files_DocumentId",
                table: "msg_message_files",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_msg_message_files_TenantId",
                table: "msg_message_files",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "msg_message_files");
        }
    }
}
