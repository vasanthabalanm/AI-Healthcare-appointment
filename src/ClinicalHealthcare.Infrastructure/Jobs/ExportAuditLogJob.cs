using System.Text;
using ClinicalHealthcare.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that exports the full AuditLog table as CSV.
///
/// Invoked when the row count exceeds 10 000 (the synchronous threshold defined
/// in <c>ExportAuditLogEndpoint</c>). The job generates the CSV in-process and
/// writes the result to a timestamped file in the <c>exports/</c> directory.
///
/// Future enhancement: upload to Azure Blob Storage and notify the requesting
/// admin via email or a polling endpoint (tracked in AC-002 edge case notes).
/// </summary>
public sealed class ExportAuditLogJob
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ExportAuditLogJob> _logger;
    private readonly IConfiguration _configuration;

    public ExportAuditLogJob(
        ApplicationDbContext db,
        ILogger<ExportAuditLogJob> logger,
        IConfiguration configuration)
    {
        _db            = db;
        _logger        = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Performs the full CSV export as a Hangfire background job.
    /// The job is retried up to 3 times (configured via <c>AutomaticRetryAttribute</c>
    /// on <c>GlobalJobFilters</c> in Program.cs).
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        _logger.LogInformation("ExportAuditLogJob: starting full CSV export.");

        var rows = await _db.AuditLogs
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
            .ToListAsync(cancellationToken.ShutdownToken);

        _logger.LogInformation("ExportAuditLogJob: fetched {RowCount} rows.", rows.Count);

        var csv = new StringBuilder();
        csv.AppendLine("Id,EntityType,EntityId,ActorId,Action,BeforeValue,AfterValue,OccurredAt,CorrelationId");

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            csv.Append(row.Id);                                       csv.Append(',');
            csv.Append(EscapeCsvField(row.EntityType));               csv.Append(',');
            csv.Append(row.EntityId);                                 csv.Append(',');
            csv.Append(row.ActorId?.ToString() ?? string.Empty);      csv.Append(',');
            csv.Append(EscapeCsvField(row.Action));                   csv.Append(',');
            csv.Append(EscapeCsvField(row.BeforeValue ?? string.Empty)); csv.Append(',');
            csv.Append(EscapeCsvField(row.AfterValue  ?? string.Empty)); csv.Append(',');
            csv.Append(row.OccurredAt.ToString("O"));                 csv.Append(',');
            csv.AppendLine(EscapeCsvField(row.CorrelationId ?? string.Empty));
        }

        // F5 fix: export directory is configurable via Exports:Path; fallback to
        // <app-base>/exports/ for local development and on-premises deployments.
        var exportsDir = _configuration["Exports:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(exportsDir);

        var filename = $"audit-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        var filePath = Path.Combine(exportsDir, filename);

        await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8, cancellationToken.ShutdownToken);

        _logger.LogInformation(
            "ExportAuditLogJob: CSV export complete. {RowCount} rows written to {FilePath}.",
            rows.Count, filePath);
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";

        return field;
    }
}
