namespace HealthPilot.Api.Middleware;

public class UnhandledExceptionMiddleware(RequestDelegate next, ILogger<UnhandledExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var bodySize = context.Request.ContentLength ?? 0;
            logger.LogError(
                ex,
                "Unhandled exception for method {Method} path {Path} bodyBytes={BodyBytes} trace={TraceId}",
                context.Request.Method,
                context.Request.Path,
                bodySize,
                context.TraceIdentifier);

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
