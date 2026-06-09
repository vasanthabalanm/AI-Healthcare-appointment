using ClinicalHealthcare.Api.Features.Intake;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_030 — Manual intake form submission.
/// Verifies AC-001 to AC-004 and guard branches.
/// </summary>
public sealed class SubmitManualIntakeEndpointTests
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
        if (result is Microsoft.AspNetCore.Http.IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    /// <summary>Returns a no-op insurance pre-check that always returns Skipped.</summary>
    private static IInsurancePreCheckService NoOpCheck()
    {
        var m = new Mock<IInsurancePreCheckService>();
        m.Setup(s => s.CheckAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(InsuranceStatus.Skipped);
        return m.Object;
    }

    private static ManualIntakeRequest ValidRequest() => new()
    {
        ChiefComplaint = "Persistent headache",
        CurrentMeds    = "Ibuprofen 400mg",
        Allergies      = "Penicillin",
        MedicalHistory = "Hypertension",
    };

    // ── AC-001: valid submission → 201 + IntakeRecord(Source=Manual) ──────────

    [Fact]
    public async Task SubmitManual_Valid_ReturnsCreatedAndPersistsRecord()
    {
        await using var db = BuildDb();

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            ValidRequest(), BuildPatientContext(10), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        var record = db.IntakeRecords.Single();
        Assert.Equal(IntakeSource.Manual, record.Source);
        Assert.Equal(10, record.PatientId);
        Assert.Equal(1, record.Version);
        Assert.True(record.IsLatest);
        Assert.NotEqual(Guid.Empty, record.IntakeGroupId);
        Assert.Equal("Persistent headache", record.ChiefComplaint);
    }

    // ── AC-001: optional fields accepted as null ──────────────────────────────

    [Fact]
    public async Task SubmitManual_OptionalFieldsNull_ReturnsCreated()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest { ChiefComplaint = "Fever" };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(11), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        var record = db.IntakeRecords.Single();
        Assert.Null(record.CurrentMeds);
        Assert.Null(record.Allergies);
        Assert.Null(record.MedicalHistory);
    }

    // ── AC-002: missing required field → 422 ─────────────────────────────────

    [Fact]
    public async Task SubmitManual_MissingChiefComplaint_Returns422()
    {
        await using var db = BuildDb();
        // ChiefComplaint is empty string — violates [Required]
        var req = new ManualIntakeRequest { ChiefComplaint = string.Empty };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(12), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── AC-002: field exceeds MaxLength → 422 ────────────────────────────────

    [Fact]
    public async Task SubmitManual_ChiefComplaintTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest { ChiefComplaint = new string('x', 1001) };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(13), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── AC-003: existing active intake → 409 ────────────────────────────────

    [Fact]
    public async Task SubmitManual_ExistingActiveIntake_Returns409()
    {
        await using var db = BuildDb();

        // Seed an active record for patient 20.
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId      = 20,
            Source         = IntakeSource.Manual,
            IntakeGroupId  = Guid.NewGuid(),
            Version        = 1,
            IsLatest       = true,
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = "existing",
        });
        await db.SaveChangesAsync();

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            ValidRequest(), BuildPatientContext(20), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
        Assert.Equal(1, db.IntakeRecords.Count()); // no new record
    }

    // ── AC-003: non-active (IsLatest=false) prior record → NOT a duplicate ───

    [Fact]
    public async Task SubmitManual_PriorRecordNotLatest_ReturnsCreated()
    {
        await using var db = BuildDb();

        // Seed an archived record (IsLatest=false) — should not block new submission.
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId      = 21,
            Source         = IntakeSource.Manual,
            IntakeGroupId  = Guid.NewGuid(),
            Version        = 1,
            IsLatest       = false,   // archived
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = "old record",
        });
        await db.SaveChangesAsync();

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            ValidRequest(), BuildPatientContext(21), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
    }

    // ── AC-004: no raw SQL — structural check via grep ──────────────────────

    [Fact]
    public void SubmitManual_EndpointFile_ContainsNoFromSqlRawInterpolation()
    {
        // Walk up from the test assembly until we find the src/ folder (repo root marker).
        var assemblyDir = System.IO.Path.GetDirectoryName(typeof(SubmitManualIntakeEndpointTests).Assembly.Location)!;
        var repoRoot    = assemblyDir;
        while (repoRoot is not null &&
               !System.IO.Directory.Exists(System.IO.Path.Combine(repoRoot, "src")))
        {
            repoRoot = System.IO.Directory.GetParent(repoRoot)?.FullName;
        }

        Assert.NotNull(repoRoot);

        var endpointPath = System.IO.Path.Combine(
            repoRoot!,
            "src", "ClinicalHealthcare.Api", "Features", "Intake", "SubmitManualIntakeEndpoint.cs");

        var source = System.IO.File.ReadAllText(endpointPath);
        Assert.DoesNotContain("FromSqlRaw($", source);
        Assert.DoesNotContain("FromSqlInterpolated(", source);
        Assert.DoesNotContain("ExecuteSqlRaw($", source);
    }

    // ── AC-002: whitespace-only ChiefComplaint → 422 (F1 fix) ───────────────

    [Fact]
    public async Task SubmitManual_WhitespaceChiefComplaint_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest { ChiefComplaint = "   " };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(40), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── AC-002: optional field exceeds MaxLength → 422 (F3 additions) ────────

    [Fact]
    public async Task SubmitManual_CurrentMedsTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest
        {
            ChiefComplaint = "Headache",
            CurrentMeds    = new string('x', 2001),
        };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(41), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    [Fact]
    public async Task SubmitManual_AllergiesTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest
        {
            ChiefComplaint = "Headache",
            Allergies      = new string('x', 2001),
        };

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(42), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── Guard: unauthenticated request → 401 ────────────────────────────────

    [Fact]
    public async Task SubmitManual_NoJwtSub_Returns401()
    {
        await using var db = BuildDb();
        var ctx = new DefaultHttpContext(); // no claims

        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            ValidRequest(), ctx, db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }

    // ── MaxLength boundary: InsurerId > 100 chars → 422 ────────────────────────

    [Fact]
    public async Task SubmitManual_InsurerIdTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest
        {
            ChiefComplaint = "Checkup",
            InsurerId      = new string('X', 101),
            PlanCode       = "PLAN01",
        };
        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(60), db, NoOpCheck(), CancellationToken.None);
        Assert.Equal(422, StatusCode(result));
    }

    // ── MaxLength boundary: PlanCode > 100 chars → 422 ───────────────────────

    [Fact]
    public async Task SubmitManual_PlanCodeTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new ManualIntakeRequest
        {
            ChiefComplaint = "Checkup",
            InsurerId      = "INS01",
            PlanCode       = new string('Y', 101),
        };
        var result = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            req, BuildPatientContext(61), db, NoOpCheck(), CancellationToken.None);
        Assert.Equal(422, StatusCode(result));
    }

    // ── Patient ID isolation: different patients, both can have active records ─

    [Fact]
    public async Task SubmitManual_DifferentPatients_BothSucceed()
    {
        await using var db = BuildDb();

        var r1 = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            new ManualIntakeRequest { ChiefComplaint = "cough" },
            BuildPatientContext(30), db, NoOpCheck(), CancellationToken.None);

        var r2 = await SubmitManualIntakeEndpoint.HandleSubmitManualIntake(
            new ManualIntakeRequest { ChiefComplaint = "fever" },
            BuildPatientContext(31), db, NoOpCheck(), CancellationToken.None);

        Assert.Equal(201, StatusCode(r1));
        Assert.Equal(201, StatusCode(r2));
        Assert.Equal(2, db.IntakeRecords.IgnoreQueryFilters().Count());
    }
}
