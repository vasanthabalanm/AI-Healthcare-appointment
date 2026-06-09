using ClinicalHealthcare.Api.Features.Intake;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_032 — Insurance pre-check soft validation.
/// Covers AC-001 to AC-004 and exception fall-back.
/// </summary>
public sealed class InsurancePreCheckTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static HttpContext BuildPatientContext(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    userId.ToString())],
                "TestAuth"));
        return ctx;
    }

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // InsurancePreCheckService unit tests
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-004: empty InsurerId → Skipped ─────────────────────────────────────

    [Theory]
    [InlineData(null, "PLAN01")]
    [InlineData("", "PLAN01")]
    [InlineData("  ", "PLAN01")]
    [InlineData("INS01", null)]
    [InlineData("INS01", "")]
    [InlineData("INS01", "   ")]
    [InlineData(null, null)]
    public async Task InsurancePreCheck_EmptyOrNullFields_ReturnsSkipped(string? insurerId, string? planCode)
    {
        await using var db = BuildDb();
        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);

        var result = await svc.CheckAsync(insurerId, planCode);

        Assert.Equal(InsuranceStatus.Skipped, result);
    }

    // ── AC-003: valid InsurerId + PlanCode found and active → Validated ───────

    [Fact]
    public async Task InsurancePreCheck_FoundActiveRecord_ReturnsValidated()
    {
        await using var db = BuildDb();
        db.InsuranceReferences.Add(new InsuranceReference
        {
            InsurerId   = "BC001",
            InsurerName = "BlueCross",
            PlanCode    = "GOLD",
            IsActive    = true,
        });
        await db.SaveChangesAsync();

        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);
        var result = await svc.CheckAsync("BC001", "GOLD");

        Assert.Equal(InsuranceStatus.Validated, result);
    }

    // ── AC-003: InsurerId + PlanCode not in reference table → NotVerified ─────

    [Fact]
    public async Task InsurancePreCheck_NotFoundInTable_ReturnsNotVerified()
    {
        await using var db = BuildDb();
        // Empty table — no reference data.

        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);
        var result = await svc.CheckAsync("UNKNOWN", "PLAN99");

        Assert.Equal(InsuranceStatus.NotVerified, result);
    }

    // ── AC-003: Found but IsActive=false → NotVerified ────────────────────────

    [Fact]
    public async Task InsurancePreCheck_FoundButInactive_ReturnsNotVerified()
    {
        await using var db = BuildDb();
        db.InsuranceReferences.Add(new InsuranceReference
        {
            InsurerId   = "BC002",
            InsurerName = "BlueCross",
            PlanCode    = "SILVER",
            IsActive    = false,   // inactive
        });
        await db.SaveChangesAsync();

        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);
        var result = await svc.CheckAsync("BC002", "SILVER");

        Assert.Equal(InsuranceStatus.NotVerified, result);
    }

    // ── Edge case: unexpected DB exception → NotVerified (non-blocking) ───────

    [Fact]
    public async Task InsurancePreCheck_DbException_ReturnsNotVerifiedNotThrows()
    {
        // Use a DbContext that has been disposed — any EF query on it will throw.
        var db = BuildDb();
        await db.DisposeAsync();

        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);
        var result = await svc.CheckAsync("ANY", "PLAN");

        // Must not throw and must return NotVerified (AC-001 — always non-blocking).
        Assert.Equal(InsuranceStatus.NotVerified, result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SubmitManualIntakeEndpoint integration with insurance pre-check
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-001: valid insurance → 201; InsuranceStatus=Validated ─────────────

    [Fact]
    public async Task SubmitManual_WithValidInsurance_Returns201AndValidated()
    {
        await using var db = BuildDb();
        var check = new Mock<IInsurancePreCheckService>();
        check.Setup(s => s.CheckAsync("BC001", "GOLD", It.IsAny<CancellationToken>()))
             .ReturnsAsync(InsuranceStatus.Validated);

        var req = new ManualIntakeRequest
        {
            ChiefComplaint = "Checkup",
            InsurerId      = "BC001",
            PlanCode       = "GOLD",
        };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(50), db, check.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        var record = db.IntakeRecords.Single();
        Assert.Equal(InsuranceStatus.Validated, record.InsuranceStatus);
    }

    // ── AC-001: unknown insurance → 201; InsuranceStatus=NotVerified ─────────

    [Fact]
    public async Task SubmitManual_WithUnknownInsurance_Returns201AndNotVerified()
    {
        await using var db = BuildDb();
        var check = new Mock<IInsurancePreCheckService>();
        check.Setup(s => s.CheckAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(InsuranceStatus.NotVerified);

        var req = new ManualIntakeRequest { ChiefComplaint = "Checkup", InsurerId = "UNKNOWN", PlanCode = "X" };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(51), db, check.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        Assert.Equal(InsuranceStatus.NotVerified, db.IntakeRecords.Single().InsuranceStatus);
    }

    // ── AC-004: empty insurance fields → 201; InsuranceStatus=Skipped ─────────

    [Fact]
    public async Task SubmitManual_NoInsuranceFields_Returns201AndSkipped()
    {
        await using var db = BuildDb();
        var check = new Mock<IInsurancePreCheckService>();
        check.Setup(s => s.CheckAsync(null, null, It.IsAny<CancellationToken>()))
             .ReturnsAsync(InsuranceStatus.Skipped);

        var req = new ManualIntakeRequest { ChiefComplaint = "Checkup" };  // no insurance fields

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(52), db, check.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        Assert.Equal(InsuranceStatus.Skipped, db.IntakeRecords.Single().InsuranceStatus);
    }

    // ── AC-001: pre-check returns NotVerified → 201; InsuranceStatus=NotVerified ─
    // (Exception handling is tested at service level in InsurancePreCheck_DbException_ReturnsNotVerifiedNotThrows)

    [Fact]
    public async Task SubmitManual_PreCheckReturnsNotVerified_Returns201()
    {
        await using var db = BuildDb();
        var check = new Mock<IInsurancePreCheckService>();
        // Service catches internally and returns NotVerified — simulated via mock.
        check.Setup(s => s.CheckAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(InsuranceStatus.NotVerified);

        var req = new ManualIntakeRequest { ChiefComplaint = "Checkup", InsurerId = "BC", PlanCode = "P1" };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(53), db, check.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        Assert.Equal(InsuranceStatus.NotVerified, db.IntakeRecords.Single().InsuranceStatus);
    }

    // ── EC-001: whitespace-padded policy number normalised before comparison ───

    [Theory]
    [InlineData(" BC001", "GOLD")]
    [InlineData("BC001 ", "GOLD")]
    [InlineData(" BC001 ", " GOLD ")]
    [InlineData("bc001", "gold")]
    public async Task InsurancePreCheck_WhitespacePaddedOrLowercaseInput_StillReturnsValidated(
        string insurerId, string planCode)
    {
        await using var db = BuildDb();
        db.InsuranceReferences.Add(new InsuranceReference
        {
            InsurerId   = "BC001",
            InsurerName = "BlueCross",
            PlanCode    = "GOLD",
            IsActive    = true,
        });
        await db.SaveChangesAsync();

        var svc = new InsurancePreCheckService(db, NullLogger<InsurancePreCheckService>.Instance);
        var result = await svc.CheckAsync(insurerId, planCode);

        Assert.Equal(InsuranceStatus.Validated, result);
    }
}
