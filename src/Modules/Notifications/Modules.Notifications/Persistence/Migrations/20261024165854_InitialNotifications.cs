using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    link = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    email_attempts = table.Column<int>(type: "int", nullable: false),
                    next_email_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferences", x => new { x.user_id, x.type, x.channel });
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    language = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_email_status_next_email_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "email_status", "next_email_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_event_id_recipient_key_type",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "event_id", "recipient_key", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_created_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "user_settings",
                schema: "notifications");
        }
    }
}
