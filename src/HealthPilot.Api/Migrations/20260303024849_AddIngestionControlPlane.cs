using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HealthPilot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestionControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingestion_checkpoints",
                columns: table => new
                {
                    CheckpointKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    RowsProcessed = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_checkpoints", x => x.CheckpointKey);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    ResumeFromCheckpoint = table.Column<bool>(type: "boolean", nullable: false),
                    CheckpointKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RowsProcessed = table.Column<int>(type: "integer", nullable: false),
                    RecordsReceived = table.Column<int>(type: "integer", nullable: false),
                    RecordsSkipped = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReplayOfJobId = table.Column<long>(type: "bigint", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FileHashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_checkpoints_UpdatedAtUtc",
                table: "ingestion_checkpoints",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_CreatedAtUtc",
                table: "ingestion_jobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_FileHashSha256",
                table: "ingestion_jobs",
                column: "FileHashSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_ReplayOfJobId",
                table: "ingestion_jobs",
                column: "ReplayOfJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_Status",
                table: "ingestion_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingestion_checkpoints");

            migrationBuilder.DropTable(
                name: "ingestion_jobs");
        }
    }
}
