using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: GET /audit/export
///
/// Exports AuditLog rows as CSV.
/// <list type="bullet">
///   <item>≤ 10 000 rows — generated synchronously and returned as a <c>text/csv</c> attachment (AC-002).</item>
///   <item>&gt; 10 000 rows — enqueued as a Hangfire background job; returns 202 Accepted with the Hangfire job ID (AC-002).</item>
/// </list>
/// Only Admin role may access this endpoint (AC-005).
/// </summary>
public sealed class ExportAuditLogEndpoint : IEndpointDefinition
{
    /// <summary>Row threshold above which the export is delegated to a Hangfire job (AC-002).</summary>
    public const int SyncExportThreshold = 10_000;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Authorization is centralised in Program.cs; idempotent guard here prevents
        // duplicate policy registration when multiple slices call AddAuthorization.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/audit/export", HandleExportAuditLog)
           .RequireAuthorization("AdminOnly")
           .WithName("ExportAuditLog")
           .WithTags("Audit")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status202Accepted)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    // ── GET /audit/export?format=csv ─────────────────────────────────────────

    public static async Task<IResult> HandleExportAuditLog(
        ApplicationDbContext db,
        [Microsoft.AspNetCore.Mvc.FromServices] IBackgroundJobClient jobClient,
        HttpContext httpContext,
        string format = "csv",
        CancellationToken ct = default)
    {
        var rowCount = await db.AuditLogs.CountAsync(ct);

        // F3 fix: only "csv" is supported; reject anything else explicitly.
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = $"Unsupported export format '{format}'. Only 'csv' is currently supported." });

        if (rowCount > SyncExportThreshold)
        {
            // AC-002 — async path: enqueue a Hangfire job and return 202 Accepted.
            var jobId = jobClient.Enqueue<ExportAuditLogJob>(
                job => job.ExecuteAsync(JobCancellationToken.Null));

            return Results.Accepted(
                uri: (string?)null,
                value: new { message = "Export queued", jobId });
        }

        // AC-002 — synchronous path: stream CSV via StringBuilder.
        var rows = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new
            {
                a.Id,
                a.EntityType,
                a.EntityId,
                a.ActorId,
                a.Action,
                a.BeforeValue,
                a.AfterValue,
                a.OccurredAt,
                a.CorrelationId
            })
            .ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("Id,EntityType,EntityId,ActorId,Action,BeforeValue,AfterValue,OccurredAt,CorrelationId");

        foreach (var row in rows)
        {
            csv.Append(row.Id);                     csv.Append(',');
            csv.Append(EscapeCsvField(row.EntityType)); csv.Append(',');
            csv.Append(row.EntityId);               csv.Append(',');
            csv.Append(row.ActorId?.ToString() ?? string.Empty); csv.Append(',');
            csv.Append(EscapeCsvField(row.Action)); csv.Append(',');
            csv.Append(EscapeCsvField(row.BeforeValue ?? string.Empty)); csv.Append(',');
            csv.Append(EscapeCsvField(row.AfterValue  ?? string.Empty)); csv.Append(',');
            csv.Append(row.OccurredAt.ToString("O")); csv.Append(',');
            csv.AppendLine(EscapeCsvField(row.CorrelationId ?? string.Empty));
        }

        var bytes    = Encoding.UTF8.GetBytes(csv.ToString());
        var filename = $"audit-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

        return Results.File(bytes, "text/csv", filename);
    }

    // Wraps a field in double-quotes if it contains commas, double-quotes, or newlines.
    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";

        return field;
    }
}
