using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Audit.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "access_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    on_behalf_of_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    on_behalf_of_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    related = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    previous_state = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    new_state = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    changes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    device_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    mfa_step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    evidence_document_ids = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    prev_hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_event_changes_json", "[changes] IS NULL OR ISJSON([changes]) = 1");
                    table.CheckConstraint("ck_audit_event_evidence_document_ids_json", "[evidence_document_ids] IS NULL OR ISJSON([evidence_document_ids]) = 1");
                    table.CheckConstraint("ck_audit_event_related_json", "[related] IS NULL OR ISJSON([related]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_entity",
                schema: "audit",
                table: "access_log",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_sequence",
                schema: "audit",
                table: "audit_events",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_org",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "on_behalf_of_org_id", "occurred_at" });

            // Append-only (audit.md §1): every UPDATE or DELETE raises, whatever the caller's permissions.
            migrationBuilder.Sql($"""
                CREATE TRIGGER audit.{AuditDbContext.AuditEventsTrigger} ON audit.audit_events
                INSTEAD OF UPDATE, DELETE AS
                THROW 51000, 'audit.audit_events is append-only.', 1;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER audit.{AuditDbContext.AccessLogTrigger} ON audit.access_log
                INSTEAD OF UPDATE, DELETE AS
                THROW 51000, 'audit.access_log is append-only.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER audit.{AuditDbContext.AccessLogTrigger};");
            migrationBuilder.Sql($"DROP TRIGGER audit.{AuditDbContext.AuditEventsTrigger};");

            migrationBuilder.DropTable(
                name: "access_log",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");
        }
    }
}
