using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNegotiatedRatePolicyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveEndUtc",
                table: "negotiated_rates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveStartUtc",
                table: "negotiated_rates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyVersion",
                table: "negotiated_rates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_negotiated_rates_EffectiveStartUtc_EffectiveEndUtc",
                table: "negotiated_rates",
                columns: new[] { "EffectiveStartUtc", "EffectiveEndUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_negotiated_rates_EffectiveStartUtc_EffectiveEndUtc",
                table: "negotiated_rates");

            migrationBuilder.DropColumn(
                name: "EffectiveEndUtc",
                table: "negotiated_rates");

            migrationBuilder.DropColumn(
                name: "EffectiveStartUtc",
                table: "negotiated_rates");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "negotiated_rates");
        }
    }
}
