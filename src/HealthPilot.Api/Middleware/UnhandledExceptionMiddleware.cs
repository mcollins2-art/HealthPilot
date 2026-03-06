namespace HealthPilot.Api.Middleware;

/// <summary>
/// Outermost middleware that catches any unhandled exception from downstream pipeline stages
/// and returns a consistent <c>500 Internal Server Error</c> JSON response. Prevents stack
/// traces and internal details from leaking to API clients.
/// </summary>
public class UnhandledExceptionMiddleware(RequestDelegate next, ILogger<UnhandledExceptionMiddleware> logger)
{
    /// <summary>
    /// Invokes the next middleware and handles any unhandled exceptions.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for path {Path} trace={TraceId}", context.Request.Path, context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "An unexpected server error occurred.",
                    traceId = context.TraceIdentifier
                });
            }
        }
    }
}
