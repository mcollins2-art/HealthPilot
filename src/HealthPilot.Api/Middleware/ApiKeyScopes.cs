namespace HealthPilot.Api.Middleware;

/// <summary>
/// Metadata record attached to endpoint metadata to declare the required API key scope.
/// </summary>
/// <param name="Scope">The scope string that the authenticated API key must include (e.g. <c>"estimate:read"</c>).</param>
public sealed record ApiKeyScopeRequirement(string Scope);

/// <summary>
/// Extension methods for adding API key scope requirements to endpoint route handlers.
/// </summary>
public static class ApiKeyScopeExtensions
{
    /// <summary>
    /// Requires that the authenticated API key includes <paramref name="scope"/> to access this endpoint.
    /// The check is enforced by <see cref="ApiKeyAuthenticationMiddleware"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach the requirement to.</param>
    /// <param name="scope">The required scope string.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder RequireApiKeyScope(this RouteHandlerBuilder builder, string scope)
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope));
        return builder;
    }
}