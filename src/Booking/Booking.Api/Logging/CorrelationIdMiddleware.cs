using Contracts;

namespace Booking.Api.Logging;

// Reads the correlation id that Availability sends on its HTTP calls and puts it, with the
// service name, in a logging scope so every log line written while the request runs carries it. The id is
// echoed back in the same header so the caller can check what Booking recorded.
// It never rejects a request: a missing id is simply not logged, an invalid one is replaced by a marker
// (see CorrelationIdPolicy). Authentication stays untouched: this runs before it only to scope the logs.
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationIdPolicy.Sanitize(context.Request.Headers[CorrelationIdPolicy.HeaderName]);

        // Only a valid value is echoed: the marker for a rejected value is a log aid, not an id.
        if (correlationId is not null && correlationId != CorrelationIdPolicy.InvalidValue)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationIdPolicy.HeaderName] = correlationId;
                return Task.CompletedTask;
            });
        }

        using var scope = logger.BeginScope(
            "Service:{Service} CorrelationId:{CorrelationId}",
            BookingAuditEvents.ServiceName,
            correlationId);

        // Debug: Availability polls the status every few seconds, which is not audit noise.
        logger.LogDebug(
            BookingAuditEvents.HttpRequestReceived,
            "HTTP request received. Method: {Method}; Path: {Path}.",
            context.Request.Method,
            context.Request.Path.Value);

        await next(context);
    }
}

internal static class CorrelationIdApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
