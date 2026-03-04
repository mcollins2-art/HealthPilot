using System.Security.Cryptography;
using System.Text;

namespace HealthPilot.Api.Middleware;

public class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    private readonly string? _configuredApiKey = configuration["Security:ApiKey"];
    private readonly string _headerName = configuration["Security:ApiKeyHeader"] ?? "X-API-Key";
    private readonly string _tenantHeaderName = configuration["Security:TenantHeader"] ?? "X-Tenant-Id";
    private readonly List<ApiKeyConfig> _configuredApiKeys = configuration
        .GetSection("Security:ApiKeys")
        .Get<List<ApiKeyConfig>>() ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        // Keep health checks open for probes.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Allow local development without key when not configured.
        if (string.IsNullOrWhiteSpace(_configuredApiKey) && _configuredApiKeys.Count == 0)
        {
            await next(context);
            return;
        }

        // Allow interactive API docs in development while still protecting data endpoints.
        if (environment.IsDevelopment()
            && (context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_headerName, out var providedValue)
            || string.IsNullOrWhiteSpace(providedValue))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = $"Missing required header '{_headerName}'." });
            return;
        }

        var provided = providedValue.ToString();
        var matchedKey = MatchKey(provided);
        if (matchedKey is null)
        {
            logger.LogWarning("Rejected request with invalid API key for path {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        var scopeRequirement = context.GetEndpoint()?.Metadata.GetMetadata<ApiKeyScopeRequirement>();
        if (scopeRequirement is not null && !matchedKey.Scopes.Contains(scopeRequirement.Scope, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Rejected request with insufficient API key scope for path {Path}. RequiredScope={Scope}",
                context.Request.Path,
                scopeRequirement.Scope);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "API key does not have required scope." });
            return;
        }

        var hasTenantRestrictions = matchedKey.Tenants.Count > 0;
        var requestTenant = context.Request.Headers.TryGetValue(_tenantHeaderName, out var tenantHeaderValue)
            ? tenantHeaderValue.ToString()
            : null;

        if (hasTenantRestrictions && string.IsNullOrWhiteSpace(requestTenant))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = $"Missing required tenant header '{_tenantHeaderName}'." });
            return;
        }

        if (!string.IsNullOrWhiteSpace(requestTenant) && hasTenantRestrictions
            && !matchedKey.Tenants.Contains(requestTenant, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "API key does not have required tenant access." });
            return;
        }

        if (!string.IsNullOrWhiteSpace(requestTenant))
        {
            context.Items["TenantId"] = requestTenant;
        }

        if (!string.IsNullOrWhiteSpace(matchedKey.Name))
        {
            context.Items["ApiKeyName"] = matchedKey.Name;
            context.Items["ApiRateLimitPartitionKey"] = hasTenantRestrictions && !string.IsNullOrWhiteSpace(requestTenant)
                ? $"{matchedKey.Name}:{requestTenant}"
                : matchedKey.Name;
        }

        await next(context);
    }

    private ApiKeyConfig? MatchKey(string provided)
    {
        if (!string.IsNullOrWhiteSpace(_configuredApiKey) && FixedTimeEquals(provided, _configuredApiKey))
        {
            return new ApiKeyConfig
            {
                Name = "legacy",
                Key = _configuredApiKey,
                Scopes = ["estimate:read", "ingestion:write"]
            };
        }

        foreach (var configured in _configuredApiKeys)
        {
            if (string.IsNullOrWhiteSpace(configured.Key))
            {
                continue;
            }

            if (FixedTimeEquals(provided, configured.Key))
            {
                return configured;
            }
        }

        return null;
    }

    private static bool FixedTimeEquals(string provided, string configured)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);

        if (providedBytes.Length != configuredBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }

    public class ApiKeyConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public List<string> Scopes { get; set; } = [];
        public List<string> Tenants { get; set; } = [];
    }
}
