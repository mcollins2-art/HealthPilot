using System.Text;
using HealthPilot.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthPilot.Api.Tests;

public class UnhandledExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CallsNext_WhenNoException()
    {
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new UnhandledExceptionMiddleware(next, NullLogger<UnhandledExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_Returns500Json_WhenExceptionThrown_AndResponseNotStarted()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var middleware = new UnhandledExceptionMiddleware(next, NullLogger<UnhandledExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Assert.Contains("unexpected server error", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trace-123", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotOverwrite_WhenResponseAlreadyStarted()
    {
        RequestDelegate next = _ => throw new InvalidOperationException("boom");

        var middleware = new UnhandledExceptionMiddleware(next, NullLogger<UnhandledExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedHttpResponseFeature());
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(context.Response.HasStarted);
    }

    private sealed class StartedHttpResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
