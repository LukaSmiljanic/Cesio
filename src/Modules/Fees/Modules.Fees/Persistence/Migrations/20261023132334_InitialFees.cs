using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Fees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fees");

            migrationBuilder.CreateTable(
                name: "fee_schedules",
                schema: "fees",
                columns: table => new
                {
                    version = table.Column<int>(type: "int", nullable: false),
                    definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_schedules", x => x.version);
                    table.CheckConstraint("ck_fee_schedule_four_eyes", "[approved_by] IS NULL OR [approved_by] <> [created_by]");
                    table.CheckConstraint("ck_fee_schedule_row_definition_json", "ISJSON([definition]) = 1");
                });

            migrationBuilder.InsertData(
                schema: "fees",
                table: "fee_schedules",
                columns: new[] { "version", "approved_at", "approved_by", "created_at", "created_by", "definition", "effective_from" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), "{\"payer\":\"Factor\",\"basis\":\"Discount\",\"share\":0.15,\"sellerShareOfFee\":null,\"minimumFee\":null,\"maximumFee\":null,\"vatTreatment\":\"TBD\",\"currency\":\"RSD\"}", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_fee_schedules_effective_from",
                schema: "fees",
                table: "fee_schedules",
                column: "effective_from");

            // Versions are immutable (task 017a): no deletes; only the approval is set, once.
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.NoDeleteTrigger} ON fees.fee_schedules
                INSTEAD OF DELETE AS
                THROW 51000, 'fees.fee_schedules rows cannot be deleted.', 1;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER fees.{FeesDbContext.ImmutableTrigger} ON fees.fee_schedules
                AFTER UPDATE AS
                IF UPDATE(version) OR UPDATE(definition) OR UPDATE(effective_from) OR UPDATE(created_by) OR UPDATE(created_at)
                   OR EXISTS (SELECT 1 FROM deleted WHERE approved_by IS NOT NULL)
                    THROW 51000, 'A fee schedule version is immutable; only its approval is set, once.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.ImmutableTrigger};");
            migrationBuilder.Sql($"DROP TRIGGER fees.{FeesDbContext.NoDeleteTrigger};");

            migrationBuilder.DropTable(
                name: "fee_schedules",
                schema: "fees");
        }
    }
}
