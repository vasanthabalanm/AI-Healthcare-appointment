using Serilog.Context;

namespace ClinicalHealthcare.Api.Middleware;

/// <summary>
/// Reads the <c>X-Correlation-ID</c> request header and propagates it through the pipeline.
///
/// Behaviour:
/// <list type="bullet">
///   <item>Header present → use the provided value as-is.</item>
///   <item>Header absent → generate a new <see cref="Guid"/> and attach it.</item>
///   <item>In both cases the value is pushed into Serilog's <see cref="LogContext"/> as
///         <c>CorrelationId</c> and echoed back on the response header.</item>
/// </list>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.Request.Headers[HeaderName].FirstOrDefault()
                  ?? Guid.NewGuid().ToString();

        // Truncate to 64 characters to prevent log injection via oversized header values (OWASP A03).
        var correlationId = raw.Length > 64 ? raw[..64] : raw;

        // Ensure the value is surfaced on the response so callers can trace requests.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push into Serilog's ambient log context for the duration of this request.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
