using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestionProvenanceAndLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IngestionJobId",
                table: "negotiated_rates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "ingestion_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "ingestion_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IngestionJobId",
                table: "cash_prices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_IngestionJobId",
                table: "negotiated_rates",
                column: "IngestionJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_LeaseExpiresAtUtc",
                table: "ingestion_jobs",
                column: "LeaseExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_cash_prices_IngestionJobId",
                table: "cash_prices",
                column: "IngestionJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_cash_prices_ingestion_jobs_IngestionJobId",
                table: "cash_prices",
                column: "IngestionJobId",
                principalTable: "ingestion_jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_negotiated_rates_ingestion_jobs_IngestionJobId",
                table: "negotiated_rates",
                column: "IngestionJobId",
                principalTable: "ingestion_jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cash_prices_ingestion_jobs_IngestionJobId",
                table: "cash_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_negotiated_rates_ingestion_jobs_IngestionJobId",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_IngestionJobId",
                table: "negotiated_rates");

            migrationBuilder.DropIndex(
                name: "IX_ingestion_jobs_LeaseExpiresAtUtc",
                table: "ingestion_jobs");

            migrationBuilder.DropIndex(
                name: "IX_cash_prices_IngestionJobId",
                table: "cash_prices");

            migrationBuilder.DropColumn(
                name: "IngestionJobId",
                table: "negotiated_rates");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "ingestion_jobs");

            migrationBuilder.DropColumn(
                name: "IngestionJobId",
                table: "cash_prices");
        }
    }
}
