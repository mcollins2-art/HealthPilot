using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class OptimizePricingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_FacilityId_InsurerId_ProcedureId",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_cash_prices_FacilityId_ProcedureId",
                table: "cash_prices");

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_FacilityId",
                table: "negotiated_rates",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_LastUpdated",
                table: "negotiated_rates",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_ProcedureId_InsurerId_FacilityId",
                table: "negotiated_rates",
                columns: new[] { "ProcedureId", "InsurerId", "FacilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_prices_FacilityId",
                table: "cash_prices",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_prices_LastUpdated",
                table: "cash_prices",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_cash_prices_ProcedureId_FacilityId",
                table: "cash_prices",
                columns: new[] { "ProcedureId", "FacilityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_FacilityId",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_LastUpdated",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_ProcedureId_InsurerId_FacilityId",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_cash_prices_FacilityId",
                table: "cash_prices");

            migrationBuilder.DropIndex(
                name: "IX_cash_prices_LastUpdated",
                table: "cash_prices");

            migrationBuilder.DropIndex(
                name: "IX_cash_prices_ProcedureId_FacilityId",
                table: "cash_prices");

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_FacilityId_InsurerId_ProcedureId",
                table: "negotiated_rates",
                columns: new[] { "FacilityId", "InsurerId", "ProcedureId" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_prices_FacilityId_ProcedureId",
                table: "cash_prices",
                columns: new[] { "FacilityId", "ProcedureId" });
        }
    }
}
