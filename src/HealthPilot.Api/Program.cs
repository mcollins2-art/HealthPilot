using HealthPilot.Api.Data;
using HealthPilot.Api.Endpoints;
using HealthPilot.Api.Ingestion;
using HealthPilot.Api.Middleware;
using HealthPilot.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
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

// Register the PostgreSQL EF Core DbContext. This is the main persistence
// boundary for the API and can be tuned further for pooling and resiliency.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Service registrations keep pricing retrieval and benefit logic separated.
builder.Services.AddScoped<IPricingQueryService, PricingQueryService>();
builder.Services.AddScoped<IPricingSelectionStrategy, NegotiatedMinPricingSelectionStrategy>();
builder.Services.AddScoped<IBenefitSimulationService, BenefitSimulationService>();
builder.Services.AddScoped<IEstimateAuditService, EstimateAuditService>();
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

    options.AddPolicy("api", httpContext =>
    {
        var keyName = httpContext.Items.TryGetValue("ApiKeyName", out var value)
            ? value?.ToString()
            : null;
        var partitionKey = string.IsNullOrWhiteSpace(keyName) ? "anonymous" : keyName;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

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
var v1 = app.MapGroup("/api/v1");
v1.MapHealthEndpoints(includeNames: false);
v1.MapEstimateEndpoints(includeNames: false);
v1.MapIngestionEndpoints(includeNames: false);

app.Run();

public partial class Program;
