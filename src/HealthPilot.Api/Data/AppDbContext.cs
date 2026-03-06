using HealthPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Insurer> Insurers => Set<Insurer>();
    public DbSet<NegotiatedRate> NegotiatedRates => Set<NegotiatedRate>();
    public DbSet<CashPrice> CashPrices => Set<CashPrice>();
    public DbSet<EstimateAuditLog> EstimateAuditLogs => Set<EstimateAuditLog>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();
    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Procedure>(entity =>
        {
            entity.ToTable("procedures");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CptCode).IsUnique();
            entity.Property(x => x.CptCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Facility>(entity =>
        {
            entity.ToTable("facilities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.State).HasMaxLength(2).IsRequired();
            entity.Property(x => x.Zip).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => x.Zip);
            entity.HasIndex(x => new { x.Name, x.City, x.State, x.Zip }).IsUnique();
        });

        modelBuilder.Entity<Insurer>(entity =>
        {
            entity.ToTable("insurers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<NegotiatedRate>(entity =>
        {
            entity.ToTable("negotiated_rates");
            entity.HasKey(x => new { x.ProcedureId, x.FacilityId, x.InsurerId });

            entity.Property(x => x.Rate).HasPrecision(12, 2);
            entity.Property(x => x.RateType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PolicyVersion).HasMaxLength(100);
            entity.Property(x => x.LastUpdated).IsRequired();

            entity.HasOne(x => x.Procedure)
                .WithMany(x => x.NegotiatedRates)
                .HasForeignKey(x => x.ProcedureId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Facility)
                .WithMany(x => x.NegotiatedRates)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Insurer)
                .WithMany(x => x.NegotiatedRates)
                .HasForeignKey(x => x.InsurerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ProcedureId, x.InsurerId, x.FacilityId });
            entity.HasIndex(x => x.LastUpdated);
            entity.HasIndex(x => new { x.EffectiveStartUtc, x.EffectiveEndUtc });
        });

        modelBuilder.Entity<CashPrice>(entity =>
        {
            entity.ToTable("cash_prices");
            entity.HasKey(x => new { x.ProcedureId, x.FacilityId });

            entity.Property(x => x.CashPriceAmount).HasColumnName("cash_price").HasPrecision(12, 2);
            entity.Property(x => x.LastUpdated).IsRequired();

            entity.HasOne(x => x.Procedure)
                .WithMany(x => x.CashPrices)
                .HasForeignKey(x => x.ProcedureId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Facility)
                .WithMany(x => x.CashPrices)
                .HasForeignKey(x => x.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ProcedureId, x.FacilityId });
            entity.HasIndex(x => x.LastUpdated);
        });

        modelBuilder.Entity<EstimateAuditLog>(entity =>
        {
            entity.ToTable("estimate_audit_logs");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.TraceId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ZipCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Insurer).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CptCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.NegotiatedRateUsed).HasPrecision(12, 2);
            entity.Property(x => x.DeductibleRemaining).HasPrecision(12, 2);
            entity.Property(x => x.CoinsurancePercent).HasPrecision(5, 2);
            entity.Property(x => x.Copay).HasPrecision(12, 2);
            entity.Property(x => x.OopMaxRemaining).HasPrecision(12, 2);
            entity.Property(x => x.EstimatedPatientResponsibility).HasPrecision(12, 2);
            entity.Property(x => x.InsurerPayment).HasPrecision(12, 2);
            entity.Property(x => x.BenefitLogicVersion).HasMaxLength(30).IsRequired();

            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.TraceId);
            entity.HasIndex(x => new { x.ZipCode, x.Insurer, x.CptCode });
        });

        modelBuilder.Entity<IngestionCheckpoint>(entity =>
        {
            entity.ToTable("ingestion_checkpoints");
            entity.HasKey(x => x.CheckpointKey);
            entity.Property(x => x.CheckpointKey).HasMaxLength(64);
            entity.Property(x => x.FilePath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<IngestionJob>(entity =>
        {
            entity.ToTable("ingestion_jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.FilePath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.CheckpointKey).HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
            entity.Property(x => x.SourceSystem).HasMaxLength(100);
            entity.Property(x => x.FileHashSha256).HasMaxLength(64);
            entity.Property(x => x.ParserVersion).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TenantId).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.ReplayOfJobId);
            entity.HasIndex(x => x.FileHashSha256);
        });
    }
}
