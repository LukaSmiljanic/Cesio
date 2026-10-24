using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Deals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Disputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to",
                schema: "deals",
                table: "disputes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "deals",
                table: "disputes",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                schema: "deals",
                table: "disputes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "opened_by_org_id",
                schema: "deals",
                table: "disputes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_summary",
                schema: "deals",
                table: "disputes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dispute_comments",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    @internal = table.Column<bool>(name: "internal", type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_comments", x => x.id);
                    table.CheckConstraint("ck_dispute_comment_content", "[text] IS NOT NULL OR [document_id] IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_dispute_comments_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalSchema: "deals",
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_opened_at",
                schema: "deals",
                table: "disputes",
                columns: new[] { "status", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_comments_dispute_id_created_at",
                schema: "deals",
                table: "dispute_comments",
                columns: new[] { "dispute_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_comments",
                schema: "deals");

            migrationBuilder.DropIndex(
                name: "ix_disputes_status_opened_at",
                schema: "deals",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "assigned_to",
                schema: "deals",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "deals",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "closed_by",
                schema: "deals",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "opened_by_org_id",
                schema: "deals",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "resolution_summary",
                schema: "deals",
                table: "disputes");
        }
    }
}
