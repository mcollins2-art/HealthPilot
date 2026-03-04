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
            logger.LogError(ex, "Unhandled exception for path {Path} trace={TraceId}", context.Request.Path, context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                var isBadRequest = ex is BadHttpRequestException or System.Text.Json.JsonException;
                context.Response.StatusCode = isBadRequest
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = isBadRequest
                        ? "The request payload is invalid."
                        : "An unexpected server error occurred.",
                    traceId = context.TraceIdentifier
                });
            }
        }
    }
}
