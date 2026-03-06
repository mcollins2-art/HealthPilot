using HealthPilot.Api.Data;
using HealthPilot.Api.Endpoints;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var hasLegacyApiKey = !string.IsNullOrWhiteSpace(builder.Configuration["Security:ApiKey"]);
var hasScopedApiKeys = builder.Configuration.GetSection("Security:ApiKeys").GetChildren().Any();

if (!builder.Environment.IsDevelopment()
    && !hasLegacyApiKey
    && !hasScopedApiKeys)
{
    throw new InvalidOperationException("Security:ApiKey or Security:ApiKeys must be configured in non-development environments.");
}

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

// Register the PostgreSQL EF Core DbContext using connection pooling.
// AddDbContextPool reduces per-request context allocation overhead by up to 25%
// at high concurrency and registers DbContextOptions<AppDbContext> as a singleton.
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// AddDbContextPool does not register IDbContextFactory automatically.
// PricingQueryService (parallel lookups) and BackgroundAuditWriter need a factory
// to create independent context instances for concurrent use.
builder.Services.AddSingleton<IDbContextFactory<AppDbContext>>(sp =>
    new PooledDbContextFactory<AppDbContext>(sp.GetRequiredService<DbContextOptions<AppDbContext>>()));

// In-memory cache used by PricingQueryService to avoid repeated DB hits for
// identical (zip, insurer, CPT) combinations within a 1-minute window.
builder.Services.AddMemoryCache();

// Service registrations keep pricing retrieval and benefit logic separated.
builder.Services.AddScoped<IPricingQueryService, PricingQueryService>();
builder.Services.AddScoped<IPricingSelectionStrategy, NegotiatedMinPricingSelectionStrategy>();
builder.Services.AddScoped<IBenefitSimulationService, BenefitSimulationService>();
builder.Services.AddScoped<IEstimateAuditService, EstimateAuditService>();
builder.Services.AddSingleton<BackgroundAuditChannel>();
builder.Services.AddHostedService<BackgroundAuditWriter>();
builder.Services.AddScoped<IPricingPersistenceService, PricingPersistenceService>();
builder.Services.AddScoped<IPricingLifecycleService, PricingLifecycleService>();
builder.Services.AddScoped<PricingIngestionPipeline>();
builder.Services.AddScoped<IIngestionCheckpointService, DbIngestionCheckpointService>();
builder.Services.AddSingleton<IIngestionJobQueue, IngestionJobQueue>();
builder.Services.AddHostedService<IngestionJobWorker>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var permitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 120;
    var windowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
    var queueLimit = builder.Configuration.GetValue<int?>("RateLimiting:QueueLimit") ?? 0;

    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = permitLimit;
        limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
        limiterOptions.QueueLimit = queueLimit;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<UnhandledExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Endpoint definitions are split to keep Program.cs clean and scalable.
app.MapHealthEndpoints();
app.MapEstimateEndpoints();
app.MapIngestionEndpoints();

app.Run();

public partial class Program;
