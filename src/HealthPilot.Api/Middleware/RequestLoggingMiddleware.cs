namespace HealthPilot.Api.Middleware;

/// <summary>
/// Middleware that logs every HTTP request with its method, path, status code, and elapsed time.
/// Runs in the outermost position (after <see cref="UnhandledExceptionMiddleware"/>) so that
/// it captures the final response status code, including error responses.
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    /// <summary>
    /// Passes the request to the next middleware stage and logs the outcome.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            logger.LogInformation(
                "Request {Method} {Path} => {StatusCode} in {ElapsedMs}ms trace={TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds,
                context.TraceIdentifier);
        }
    }
}
