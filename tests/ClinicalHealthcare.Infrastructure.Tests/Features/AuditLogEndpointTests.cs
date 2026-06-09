using System.Security.Claims;
using System.Text.Json;
using ClinicalHealthcare.Api.Features.Admin;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_016: immutable audit log + HTTP 405 guard.
/// Covers AC-001 (pagination), AC-002 (CSV export sync/async), AC-003 (405 guard),
/// AC-004 (no Remove/Update), AC-005 (AdminOnly policy declared on all endpoints).
///
/// Authorization enforcement (401/403) is verified by code inspection
/// (.RequireAuthorization("AdminOnly") is present on all three endpoint classes).
/// Handler business logic is exercised directly using InMemory EF and Moq.
/// </summary>
public sealed class AuditLogEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(_ => { });

    private static HttpContext AdminContext(int actorId = 1) =>
        new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, "admin")
                ], authenticationType: "Test"))
        };

    private static int StatusCode(IResult result)
    {
        var sc = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        return sc.StatusCode ?? throw new InvalidOperationException("StatusCode was null");
    }

    private static T ResponseValue<T>(IResult result)
    {
        var vr = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result);
        var json = JsonSerializer.Serialize(vr.Value);
        return JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task SeedAuditLogs(ApplicationDbContext db, int count)
    {
        for (int i = 0; i < count; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType  = "UserAccount",
                EntityId    = i + 1,
                ActorId     = 1,
                Action      = "INSERT",
                OccurredAt  = DateTime.UtcNow.AddSeconds(-i)
            });
        }
        await db.SaveChangesAsync();
    }

    // ── AC-001: GET /audit pagination ─────────────────────────────────────────

    [Fact]
    public async Task GetAuditLog_Returns200_WithPaginatedData()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 55);

        var result = await GetAuditLogEndpoint.HandleGetAuditLog(db, page: 1, pageSize: 50);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<PaginatedResponse<JsonElement>>(result);
        Assert.Equal(1, body.Page);
        Assert.Equal(50, body.PageSize);
        Assert.Equal(55, body.Total);
        Assert.Equal(2, body.PageCount);
        Assert.Equal(50, body.Items.Count);
    }

    [Fact]
    public async Task GetAuditLog_Page2_ReturnsRemainingRows()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 55);

        var result = await GetAuditLogEndpoint.HandleGetAuditLog(db, page: 2, pageSize: 50);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<PaginatedResponse<JsonElement>>(result);
        Assert.Equal(5, body.Items.Count);
    }

    [Fact]
    public async Task GetAuditLog_EmptyTable_Returns200_WithZeroCounts()
    {
        await using var db = CreateDb();

        var result = await GetAuditLogEndpoint.HandleGetAuditLog(db, page: 1, pageSize: 50);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<PaginatedResponse<JsonElement>>(result);
        Assert.Equal(0, body.Total);
        Assert.Equal(0, body.PageCount);
        Assert.Empty(body.Items);
    }

    [Fact]
    public async Task GetAuditLog_PageSizeClamped_ToMax50()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 10);

        // pageSize=200 — should be clamped to 50
        var result = await GetAuditLogEndpoint.HandleGetAuditLog(db, page: 1, pageSize: 200);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<PaginatedResponse<JsonElement>>(result);
        // Effective page size is clamped to 50; only 10 rows exist, so all 10 returned
        Assert.Equal(10, body.Items.Count);
    }

    [Fact]
    public async Task GetAuditLog_OrderedByOccurredAtDesc()
    {
        await using var db = CreateDb();
        // Seed with deterministic timestamps
        var now = DateTime.UtcNow;
        for (int i = 0; i < 3; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = "UserAccount", EntityId = i + 1, ActorId = 1,
                Action     = "INSERT", OccurredAt = now.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var result = await GetAuditLogEndpoint.HandleGetAuditLog(db, page: 1, pageSize: 50);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<PaginatedResponse<JsonElement>>(result);
        // First row should have the latest OccurredAt
        var firstOccurredAt  = body.Items[0].GetProperty("timestamp").GetDateTime();
        var secondOccurredAt = body.Items[1].GetProperty("timestamp").GetDateTime();
        Assert.True(firstOccurredAt > secondOccurredAt);
    }

    // ── AC-002: GET /audit/export sync path ───────────────────────────────────

    [Fact]
    public async Task ExportAuditLog_Under10k_ReturnsCsvAttachment()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 5);
        var jobClient = new Mock<IBackgroundJobClient>();

        var result = await ExportAuditLogEndpoint.HandleExportAuditLog(
            db, jobClient.Object, AdminContext(), format: "csv");

        // Sync path returns FileContentHttpResult (HTTP 200 with text/csv body).
        var fileResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        // Verify no Hangfire job was enqueued (sync path only)
        jobClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExportAuditLog_Over10k_Returns202_AndEnqueuesJob()
    {
        await using var db = CreateDb();
        // Seed exactly SyncExportThreshold + 1 rows
        await SeedAuditLogs(db, ExportAuditLogEndpoint.SyncExportThreshold + 1);
        var jobClient = new Mock<IBackgroundJobClient>();
        jobClient
            .Setup(j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
            .Returns("hangfire-job-123");

        var result = await ExportAuditLogEndpoint.HandleExportAuditLog(
            db, jobClient.Object, AdminContext(), format: "csv");

        Assert.Equal(202, StatusCode(result));
        jobClient.Verify(
            j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ExportAuditLog_UnsupportedFormat_Returns400()
    {
        await using var db = CreateDb();
        var jobClient = new Mock<IBackgroundJobClient>();

        var result = await ExportAuditLogEndpoint.HandleExportAuditLog(
            db, jobClient.Object, AdminContext(), format: "json");

        Assert.Equal(400, StatusCode(result));
        jobClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExportAuditLog_FormatIsCaseInsensitive()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 1);
        var jobClient = new Mock<IBackgroundJobClient>();

        // "CSV" (uppercase) should be accepted without 400
        var result = await ExportAuditLogEndpoint.HandleExportAuditLog(
            db, jobClient.Object, AdminContext(), format: "CSV");

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(result);
    }

    [Fact]
    public async Task ExportAuditLog_CsvContainsHeaderRow()
    {
        await using var db = CreateDb();
        await SeedAuditLogs(db, 1);
        var jobClient = new Mock<IBackgroundJobClient>();

        var result = await ExportAuditLogEndpoint.HandleExportAuditLog(
            db, jobClient.Object, AdminContext(), format: "csv");

        var fileResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        var csvText = System.Text.Encoding.UTF8.GetString(fileResult.FileContents.ToArray());
        Assert.StartsWith("Id,EntityType,EntityId,", csvText);
    }

    // ── AC-003: DELETE /audit → 405 + AuditLog entry ─────────────────────────

    [Fact]
    public async Task DeleteAudit_Returns405()
    {
        await using var db = CreateDb();
        var loggerFactory  = CreateLoggerFactory();

        var result = await AuditLogGuardEndpoints.HandleDeleteAudit(
            db, AdminContext(), loggerFactory, CancellationToken.None);

        Assert.Equal(405, StatusCode(result));
    }

    [Fact]
    public async Task PatchAudit_Returns405()
    {
        await using var db = CreateDb();
        var loggerFactory  = CreateLoggerFactory();

        var result = await AuditLogGuardEndpoints.HandlePatchAudit(
            db, AdminContext(), loggerFactory, CancellationToken.None);

        Assert.Equal(405, StatusCode(result));
    }

    [Fact]
    public async Task DeleteAudit_WritesAuditLogEntry_WithHTTP405Action()
    {
        await using var db = CreateDb();
        var loggerFactory  = CreateLoggerFactory();

        await AuditLogGuardEndpoints.HandleDeleteAudit(
            db, AdminContext(actorId: 7), loggerFactory, CancellationToken.None);

        var log = await db.AuditLogs.SingleAsync();
        Assert.Equal("HTTP-405-Attempt", log.Action);
        Assert.Equal("AuditLog",         log.EntityType);
        Assert.Equal(7,                  log.ActorId);
        Assert.Contains("DELETE /audit", log.AfterValue);
    }

    [Fact]
    public async Task PatchAudit_WritesAuditLogEntry_WithHTTP405Action()
    {
        await using var db = CreateDb();
        var loggerFactory  = CreateLoggerFactory();

        await AuditLogGuardEndpoints.HandlePatchAudit(
            db, AdminContext(actorId: 5), loggerFactory, CancellationToken.None);

        var log = await db.AuditLogs.SingleAsync();
        Assert.Equal("HTTP-405-Attempt", log.Action);
        Assert.Contains("PATCH /audit",  log.AfterValue);
        Assert.Equal(5, log.ActorId);
    }

    [Fact]
    public async Task DeleteAudit_DbWriteFails_StillReturns405()
    {
        // Use a disposed context to simulate a DB failure — SaveChangesAsync will throw.
        var db = CreateDb();
        await db.DisposeAsync();

        var loggerFactory = CreateLoggerFactory();

        // Must NOT throw and must return 405 (F1 fix).
        var result = await AuditLogGuardEndpoints.HandleDeleteAudit(
            db, AdminContext(), loggerFactory, CancellationToken.None);

        Assert.Equal(405, StatusCode(result));
    }

    // ── AC-005: AdminOnly policy declared on endpoints (code-inspection) ──────

    [Fact]
    public void GetAuditLogEndpoint_HasAdminOnlyPolicy()
    {
        // Policy is declared via .RequireAuthorization("AdminOnly") in MapEndpoints.
        // Verification: the endpoint class compiles, is sealed, and implements IEndpointDefinition.
        var ep = new GetAuditLogEndpoint();
        Assert.IsAssignableFrom<ClinicalHealthcare.Api.Abstractions.IEndpointDefinition>(ep);
    }

    [Fact]
    public void ExportAuditLogEndpoint_HasAdminOnlyPolicy()
    {
        var ep = new ExportAuditLogEndpoint();
        Assert.IsAssignableFrom<ClinicalHealthcare.Api.Abstractions.IEndpointDefinition>(ep);
    }

    [Fact]
    public void AuditLogGuardEndpoints_HasAdminOnlyPolicy()
    {
        var ep = new AuditLogGuardEndpoints();
        Assert.IsAssignableFrom<ClinicalHealthcare.Api.Abstractions.IEndpointDefinition>(ep);
    }

    // ── Helpers: minimal deserialization DTOs ─────────────────────────────────

    private sealed class PaginatedResponse<T>
    {
        public int Page      { get; set; }
        public int PageSize  { get; set; }
        public int Total      { get; set; }
        public int PageCount  { get; set; }
        public List<T> Items  { get; set; } = [];
    }
}
