using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimateAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estimate_audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ZipCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Insurer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CptCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    NegotiatedRateUsed = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DeductibleRemaining = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CoinsurancePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Copay = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OopMaxRemaining = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CopayAppliesBeforeDeductible = table.Column<bool>(type: "boolean", nullable: false),
                    EstimatedPatientResponsibility = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    InsurerPayment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BenefitLogicVersion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estimate_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_estimate_audit_logs_CreatedAt",
                table: "estimate_audit_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_audit_logs_TraceId",
                table: "estimate_audit_logs",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_estimate_audit_logs_ZipCode_Insurer_CptCode",
                table: "estimate_audit_logs",
                columns: new[] { "ZipCode", "Insurer", "CptCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estimate_audit_logs");
        }
    }
}
