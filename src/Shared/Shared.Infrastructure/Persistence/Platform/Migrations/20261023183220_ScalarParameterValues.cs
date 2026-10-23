using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Infrastructure.Persistence.Platform.Migrations
{
    /// <inheritdoc />
    public partial class ScalarParameterValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_business_parameter_value_json",
                schema: "platform",
                table: "business_parameters");

            migrationBuilder.AddCheckConstraint(
                name: "ck_business_parameter_value_json",
                schema: "platform",
                table: "business_parameters",
                sql: "ISJSON([value], VALUE) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_business_parameter_value_json",
                schema: "platform",
                table: "business_parameters");

            migrationBuilder.AddCheckConstraint(
                name: "ck_business_parameter_value_json",
                schema: "platform",
                table: "business_parameters",
                sql: "ISJSON([value]) = 1");
        }
    }
}
