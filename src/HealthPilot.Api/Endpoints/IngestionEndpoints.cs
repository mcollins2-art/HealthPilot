using HealthPilot.Api.Dtos;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace HealthPilot.Api.Endpoints;

public static class IngestionEndpoints
{
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

        try
        {
            var batchSize = request.BatchSize ?? configuration.GetValue<int?>("Ingestion:BatchSize") ?? 5000;
            var result = await pipeline.ImportFileWithBatchingAsync(
                fullPath,
                batchSize,
                request.ResumeFromCheckpoint,
                cancellationToken);

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
                result.RowsResumedFrom,
                result.RowsProcessed,
                result.Completed,
                traceId = httpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion import failed for {FilePath}", fullPath);
            return Results.Problem(
                title: "Ingestion failed",
                detail: "An unexpected error occurred while processing the file.",
                statusCode: StatusCodes.Status500InternalServerError,
                instance: httpContext.TraceIdentifier);
        }
    }
}
