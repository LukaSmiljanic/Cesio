using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Documents.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "stored_documents",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    blob_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_documents_blob_key",
                schema: "documents",
                table: "stored_documents",
                column: "blob_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_documents_owner_org_id",
                schema: "documents",
                table: "stored_documents",
                column: "owner_org_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_documents",
                schema: "documents");
        }
    }
}
