namespace HealthPilot.Api.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
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
