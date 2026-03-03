using HealthPilot.Api.Data;
using HealthPilot.Api.Endpoints;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Ingestion.Parsers;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var maxRequestBodyBytes = builder.Configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 1_048_576;
if (maxRequestBodyBytes <= 0)
{
    throw new InvalidOperationException("Security:MaxRequestBodyBytes must be greater than 0.");
}

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

var hasLegacyApiKey = !string.IsNullOrWhiteSpace(builder.Configuration["Security:ApiKey"]);
var hasScopedApiKeys = builder.Configuration.GetSection("Security:ApiKeys").GetChildren().Any();

if (!builder.Environment.IsDevelopment()
    && !hasLegacyApiKey
    && !hasScopedApiKeys)
{
    throw new InvalidOperationException("Security:ApiKey or Security:ApiKeys must be configured in non-development environments.");
}

// Register the PostgreSQL EF Core DbContext. This is the main persistence
// boundary for the API and can be tuned further for pooling and resiliency.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(defaultConnection));

// Service registrations keep pricing retrieval and benefit logic separated.
builder.Services.AddScoped<IPricingQueryService, PricingQueryService>();
builder.Services.AddScoped<IPricingSelectionStrategy, NegotiatedMinPricingSelectionStrategy>();
builder.Services.AddScoped<IBenefitSimulationService, BenefitSimulationService>();
builder.Services.AddScoped<IEstimateAuditService, EstimateAuditService>();
builder.Services.AddScoped<IPricingPersistenceService, PricingPersistenceService>();
builder.Services.AddScoped<IPricingLifecycleService, PricingLifecycleService>();
builder.Services.AddScoped<IPricingParser, CmsCsvPricingParser>();
builder.Services.AddScoped<IPricingParser, CmsJsonPricingParser>();
builder.Services.AddScoped<IPricingParserRegistry, PricingParserRegistry>();
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
    if (permitLimit <= 0 || windowSeconds <= 0 || queueLimit < 0)
    {
        throw new InvalidOperationException("RateLimiting configuration is invalid. PermitLimit and WindowSeconds must be greater than 0 and QueueLimit must be >= 0.");
    }

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
