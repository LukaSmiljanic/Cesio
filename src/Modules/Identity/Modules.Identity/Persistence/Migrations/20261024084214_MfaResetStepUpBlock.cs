using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MfaResetStepUpBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "step_up_blocked_until",
                schema: "auth",
                table: "users",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "step_up_blocked_until",
                schema: "auth",
                table: "users");
        }
    }
}
