namespace HealthPilot.Api.Middleware;

/// <summary>
/// Middleware that appends security-hardening HTTP response headers to every response.
/// These headers instruct browsers and proxies to enforce security policies that
/// mitigate common web vulnerabilities (clickjacking, MIME-sniffing, information disclosure).
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Adds security response headers and delegates to the next middleware stage.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
        context.Response.Headers["Cache-Control"] = "no-store";

        await next(context);
    }
}
