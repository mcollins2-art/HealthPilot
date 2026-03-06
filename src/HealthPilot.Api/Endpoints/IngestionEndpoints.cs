using System.Security.Cryptography;
using HealthPilot.Api.Data;
using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Models;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealthPilot.Api.Endpoints;

public static class IngestionEndpoints
{
    private const int DefaultMaxAttempts = 2;
    private const int MinMaxAttempts = 1;

    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ingestion/import", HandleImportAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("ImportPricingFile")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapGet("/ingestion/checkpoints/{checkpointKey}", HandleCheckpointStatusAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("GetIngestionCheckpoint")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapGet("/ingestion/checkpoints", HandleCheckpointListAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("ListIngestionCheckpoints")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapPost("/ingestion/checkpoints/cleanup", HandleCheckpointCleanupAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("CleanupIngestionCheckpoints")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapGet("/ingestion/jobs/{jobId:long}", HandleJobStatusAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("GetIngestionJob")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapPost("/ingestion/jobs/{jobId:long}/replay", HandleReplayAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("ReplayIngestionJob")
            .WithTags("Ingestion")
            .WithOpenApi();

        endpoints.MapPost("/ingestion/pricing/cleanup", HandlePricingCleanupAsync)
            .RequireApiKeyScope("ingestion:write")
            .RequireRateLimiting("api")
            .WithName("CleanupPricingLifecycle")
            .WithTags("Ingestion")
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> HandleCheckpointStatusAsync(
        string checkpointKey,
        IIngestionCheckpointService checkpointService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkpointKey))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "checkpointKey is required.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var checkpoint = await checkpointService.GetByKeyAsync(checkpointKey, cancellationToken);
        if (checkpoint is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Checkpoint not found",
                Detail = "No checkpoint exists for the provided key.",
                Status = StatusCodes.Status404NotFound,
                Instance = httpContext.TraceIdentifier
            });
        }

        return Results.Ok(checkpoint);
    }

    private static async Task<IResult> HandleCheckpointListAsync(
        [FromQuery] int? limit,
        IIngestionCheckpointService checkpointService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var resolvedLimit = Math.Clamp(limit ?? 20, 1, 200);
        var records = await checkpointService.ListRecentAsync(resolvedLimit, cancellationToken);
        var retentionHours = configuration.GetValue<int?>("Ingestion:CheckpointRetentionHours") ?? 168;
        return Results.Ok(new { count = records.Count, retentionHours, items = records });
    }

    private static async Task<IResult> HandleCheckpointCleanupAsync(
        [FromQuery] int? retentionHours,
        IIngestionCheckpointService checkpointService,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var hours = retentionHours ?? configuration.GetValue<int?>("Ingestion:CheckpointRetentionHours") ?? 168;
        if (hours < 1)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "retentionHours must be at least 1.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var deleted = await checkpointService.CleanupExpiredAsync(TimeSpan.FromHours(hours), cancellationToken);
        return Results.Ok(new
        {
            deleted,
            retentionHours = hours,
            traceId = httpContext.TraceIdentifier
        });
    }

    private static async Task<IResult> HandleImportAsync(
        IngestionImportRequest request,
        PricingIngestionPipeline pipeline,
        AppDbContext dbContext,
        IIngestionJobQueue jobQueue,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("IngestionEndpoints");

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "FilePath is required.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var fullPath = Path.GetFullPath(request.FilePath);

        var allowedRoot = configuration["Ingestion:AllowedRootPath"];
        if (!string.IsNullOrWhiteSpace(allowedRoot))
        {
            var normalizedAllowedRoot = Path.GetFullPath(allowedRoot);
            if (!fullPath.StartsWith(normalizedAllowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid file path",
                    Detail = "FilePath is outside configured ingestion root.",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = httpContext.TraceIdentifier
                });
            }
        }

        if (!File.Exists(fullPath))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "File not found",
                Detail = "File path does not exist.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not ".csv" and not ".json")
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Unsupported file type",
                Detail = "Only .csv and .json files are supported.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var fileInfo = new FileInfo(fullPath);
        const long maxImportBytes = 1_000_000_000; // 1GB
        if (fileInfo.Length > maxImportBytes)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = "Maximum file size is 1GB.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        logger.LogInformation("Starting ingestion import for {FilePath}", fullPath);

        IngestionJob? job = null;

        try
        {
            var batchSize = request.BatchSize ?? configuration.GetValue<int?>("Ingestion:BatchSize") ?? 5000;
            var maxAttempts = Math.Max(configuration.GetValue<int?>("Ingestion:MaxAttempts") ?? DefaultMaxAttempts, MinMaxAttempts);
            var parserVersion = extension == ".csv" ? "cms_csv_v1" : "cms_json_v1";
            var hash = await ComputeFileHashAsync(fullPath, cancellationToken);

            job = new IngestionJob
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Status = request.Async ? "queued" : "in_progress",
                FilePath = fullPath,
                BatchSize = batchSize,
                ResumeFromCheckpoint = request.ResumeFromCheckpoint,
                SourceSystem = request.SourceSystem,
                FileHashSha256 = hash,
                ParserVersion = parserVersion,
                MaxAttempts = maxAttempts,
                EffectiveStartUtc = request.EffectiveStartUtc,
                EffectiveEndUtc = request.EffectiveEndUtc,
                TenantId = httpContext.Items.TryGetValue("TenantId", out var tenantId) ? tenantId?.ToString() : null
            };

            dbContext.IngestionJobs.Add(job);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (request.Async)
            {
                await jobQueue.EnqueueAsync(job.Id, cancellationToken);
                return Results.Accepted($"/ingestion/jobs/{job.Id}", new
                {
                    status = "queued",
                    jobId = job.Id,
                    job.FileHashSha256,
                    job.ParserVersion,
                    traceId = httpContext.TraceIdentifier
                });
            }

            var result = await pipeline.ImportFileWithBatchingAsync(
                fullPath,
                batchSize,
                request.ResumeFromCheckpoint,
                cancellationToken);

            job.Status = "completed";
            job.CheckpointKey = result.CheckpointKey;
            job.RowsProcessed = result.RowsProcessed;
            job.RecordsReceived = result.Persistence.RecordsReceived;
            job.RecordsSkipped = result.Persistence.RecordsSkipped;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Ingestion import complete for {FilePath}. Received={Received}, Skipped={Skipped}, Procedures={Procedures}, Facilities={Facilities}, Insurers={Insurers}, NegotiatedUpserted={Negotiated}, CashUpserted={Cash}",
                fullPath,
                result.Persistence.RecordsReceived,
                result.Persistence.RecordsSkipped,
                result.Persistence.ProceduresCreated,
                result.Persistence.FacilitiesCreated,
                result.Persistence.InsurersCreated,
                result.Persistence.NegotiatedRatesUpserted,
                result.Persistence.CashPricesUpserted);

            return Results.Ok(new
            {
                status = "completed",
                result.Persistence.RecordsReceived,
                result.Persistence.RecordsSkipped,
                result.Persistence.ProceduresCreated,
                result.Persistence.FacilitiesCreated,
                result.Persistence.InsurersCreated,
                result.Persistence.NegotiatedRatesUpserted,
                result.Persistence.CashPricesUpserted,
                result.CheckpointKey,
                jobId = job.Id,
                result.RowsResumedFrom,
                result.RowsProcessed,
                result.Completed,
                traceId = httpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion import failed for {FilePath}", fullPath);
            if (job is not null)
            {
                job.Status = "dead_lettered";
                job.AttemptCount += 1;
                job.ErrorMessage = IngestionErrorFormatter.BuildBoundedErrorMessage(ex, httpContext.TraceIdentifier);
                job.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Results.Problem(
                title: "Ingestion failed",
                detail: "An unexpected error occurred while processing the file.",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: httpContext.TraceIdentifier);
        }
    }

    private static async Task<IResult> HandleJobStatusAsync(
        long jobId,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requestTenant = httpContext.Items.TryGetValue("TenantId", out var tenantId)
            ? tenantId?.ToString()
            : null;

        var job = await dbContext.IngestionJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == jobId && (requestTenant == null || x.TenantId == requestTenant),
                cancellationToken);
        return job is null ? Results.NotFound() : Results.Ok(job);
    }

    private static async Task<IResult> HandleReplayAsync(
        long jobId,
        AppDbContext dbContext,
        IIngestionJobQueue jobQueue,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requestTenant = httpContext.Items.TryGetValue("TenantId", out var tenantId)
            ? tenantId?.ToString()
            : null;

        var sourceJob = await dbContext.IngestionJobs.SingleOrDefaultAsync(
            x => x.Id == jobId && (requestTenant == null || x.TenantId == requestTenant),
            cancellationToken);
        if (sourceJob is null)
        {
            return Results.NotFound();
        }

        var replayJob = new IngestionJob
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Status = "queued",
            FilePath = sourceJob.FilePath,
            BatchSize = sourceJob.BatchSize,
            ResumeFromCheckpoint = false,
            ReplayOfJobId = sourceJob.Id,
            SourceSystem = sourceJob.SourceSystem,
            FileHashSha256 = sourceJob.FileHashSha256,
            ParserVersion = sourceJob.ParserVersion,
            MaxAttempts = sourceJob.MaxAttempts,
            EffectiveStartUtc = sourceJob.EffectiveStartUtc,
            EffectiveEndUtc = sourceJob.EffectiveEndUtc,
            TenantId = sourceJob.TenantId
        };

        dbContext.IngestionJobs.Add(replayJob);
        await dbContext.SaveChangesAsync(cancellationToken);
        await jobQueue.EnqueueAsync(replayJob.Id, cancellationToken);

        return Results.Accepted($"/ingestion/jobs/{replayJob.Id}", new
        {
            status = "queued",
            jobId = replayJob.Id,
            replayOfJobId = sourceJob.Id
        });
    }

    private static async Task<IResult> HandlePricingCleanupAsync(
        [FromQuery] int? retentionDays,
        [FromQuery] bool? dryRun,
        [FromQuery] bool? confirm,
        IPricingLifecycleService pricingLifecycleService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var days = retentionDays ?? 365;
        if (days < 1)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "retentionDays must be at least 1.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var retention = TimeSpan.FromDays(days);
        var preview = await pricingLifecycleService.GetStalePricingCountsAsync(retention, cancellationToken);
        var isDryRun = dryRun ?? true;

        if (isDryRun)
        {
            return Results.Ok(new
            {
                dryRun = true,
                preview.NegotiatedRatesCount,
                preview.CashPricesCount,
                retentionDays = days,
                traceId = httpContext.TraceIdentifier
            });
        }

        if (!(confirm ?? false))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Confirmation required",
                Detail = "Set confirm=true when dryRun=false to execute deletion.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.TraceIdentifier
            });
        }

        var deleted = await pricingLifecycleService.CleanupStalePricingAsync(retention, cancellationToken);
        return Results.Ok(new
        {
            dryRun = false,
            deleted.NegotiatedRatesDeleted,
            deleted.CashPricesDeleted,
            preview.NegotiatedRatesCount,
            preview.CashPricesCount,
            retentionDays = days,
            traceId = httpContext.TraceIdentifier
        });
    }

    private static async Task<string> ComputeFileHashAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
