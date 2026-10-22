using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Infrastructure.Persistence.Platform.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "business_parameters",
                schema: "platform",
                columns: table => new
                {
                    param_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_parameters", x => new { x.param_key, x.version });
                    table.CheckConstraint("ck_business_parameter_value_json", "ISJSON([value]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    idem_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    endpoint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    response_status = table.Column<int>(type: "int", nullable: true),
                    response_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.org_id, x.endpoint, x.idem_key });
                    table.CheckConstraint("ck_idempotency_record_response_body_json", "[response_body] IS NULL OR ISJSON([response_body]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                    table.CheckConstraint("ck_outbox_message_payload_json", "ISJSON([payload]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "platform",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consumer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.event_id, x.consumer });
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "platform",
                table: "outbox",
                column: "next_attempt_at",
                filter: "[processed_at] IS NULL AND [dead_lettered_at] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_parameters",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "platform");
        }
    }
}
