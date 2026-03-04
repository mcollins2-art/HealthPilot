namespace HealthPilot.Api.Middleware;

public class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger,
    IConfiguration configuration)
{
    private readonly long _maxRequestBodyBytes = configuration.GetValue<long?>("Security:MaxRequestBodyBytes") ?? 1_048_576;

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _maxRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = $"Request payload exceeds {_maxRequestBodyBytes} bytes."
                });
                return;
            }

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
