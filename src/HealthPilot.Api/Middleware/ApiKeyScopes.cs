namespace HealthPilot.Api.Middleware;

public sealed record ApiKeyScopeRequirement(string Scope);

public static class ApiKeyScopeExtensions
{
    public static RouteHandlerBuilder RequireApiKeyScope(this RouteHandlerBuilder builder, string scope)
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope));
        return builder;
    }
}