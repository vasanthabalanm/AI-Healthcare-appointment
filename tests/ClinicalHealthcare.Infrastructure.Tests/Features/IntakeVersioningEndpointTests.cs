using ClinicalHealthcare.Api.Features.Intake;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_031 — Intake editing with version history.
/// Covers AC-001 to AC-005, no-op PATCH, ownership guard, and historical GET.
/// </summary>
public sealed class IntakeVersioningEndpointTests
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

    private static bool IsForbid(IResult result) =>
        result.GetType().Name == "ForbidHttpResult";

    /// <summary>Builds an HttpContext with role 'staff' — can read any patient's intake.</summary>
    private static HttpContext BuildStaffContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, "staff")],
                "TestAuth"));
        return ctx;
    }

    private static async Task<IntakeRecord> SeedLatestRecord(
        ApplicationDbContext db,
        int patientId,
        Guid? intakeGroupId = null,
        int version = 1,
        string chiefComplaint = "Headache",
        string? currentMeds   = "Ibuprofen",
        string? allergies     = "Penicillin",
        string? medicalHistory = "Hypertension")
    {
        var record = new IntakeRecord
        {
            PatientId      = patientId,
            Source         = IntakeSource.Manual,
            IntakeGroupId  = intakeGroupId ?? Guid.NewGuid(),
            Version        = version,
            IsLatest       = true,
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = chiefComplaint,
            CurrentMeds    = currentMeds,
            Allergies      = allergies,
            MedicalHistory = medicalHistory,
        };
        db.IntakeRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PATCH /intake/{intakeGroupId} — UpdateIntakeEndpoint
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-001: PATCH with changes → new version row; prior IsLatest=false ───

    [Fact]
    public async Task UpdateIntake_ChangedValues_CreatesNewVersionAndMarksOldNotLatest()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 1);
        var groupId = seed.IntakeGroupId;

        var req = new UpdateIntakeRequest { ChiefComplaint = "Severe migraine" };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            groupId, req, BuildPatientContext(1), db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Two rows exist for this group
        var all = db.IntakeRecords.IgnoreQueryFilters()
            .Where(r => r.IntakeGroupId == groupId)
            .OrderBy(r => r.Version)
            .ToList();

        Assert.Equal(2, all.Count);

        var v1 = all[0];
        var v2 = all[1];

        Assert.Equal(1, v1.Version);
        Assert.False(v1.IsLatest);              // prior version demoted

        Assert.Equal(2, v2.Version);
        Assert.True(v2.IsLatest);               // new version is latest
        Assert.Equal("Severe migraine", v2.ChiefComplaint);
        Assert.Equal(seed.CurrentMeds, v2.CurrentMeds);   // unchanged fields copied
        Assert.Equal(seed.Allergies, v2.Allergies);
        Assert.Equal(seed.MedicalHistory, v2.MedicalHistory);
    }

    // ── AC-001: Version number is sequential (current.Version + 1) ───────────

    [Fact]
    public async Task UpdateIntake_SequentialVersions_IncrementByOne()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 2);
        var groupId = seed.IntakeGroupId;

        var req1 = new UpdateIntakeRequest { Allergies = "Sulfa" };
        await UpdateIntakeEndpoint.HandleUpdateIntake(groupId, req1, BuildPatientContext(2), db, CancellationToken.None);

        var req2 = new UpdateIntakeRequest { Allergies = "Sulfa, Aspirin" };
        await UpdateIntakeEndpoint.HandleUpdateIntake(groupId, req2, BuildPatientContext(2), db, CancellationToken.None);

        var versions = db.IntakeRecords.IgnoreQueryFilters()
            .Where(r => r.IntakeGroupId == groupId)
            .OrderBy(r => r.Version)
            .Select(r => r.Version)
            .ToList();

        Assert.Equal([1, 2, 3], versions);
    }

    // ── AC-004: No-op PATCH (same values) → 200; no new version ──────────────

    [Fact]
    public async Task UpdateIntake_SameValues_ReturnsOkWithNoOp_NoNewVersion()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 3);
        var groupId = seed.IntakeGroupId;

        // Request exactly matching current values — no change.
        var req = new UpdateIntakeRequest
        {
            ChiefComplaint = seed.ChiefComplaint,
            CurrentMeds    = seed.CurrentMeds,
            Allergies      = seed.Allergies,
            MedicalHistory = seed.MedicalHistory,
        };

        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            groupId, req, BuildPatientContext(3), db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Still exactly one record.
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == groupId));
    }

    // ── AC-004: null fields in request → no change for those fields ──────────

    [Fact]
    public async Task UpdateIntake_NullFields_TreatedAsNoChange()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 4);
        var groupId = seed.IntakeGroupId;

        // Only change CurrentMeds; leave everything else null (= no change).
        var req = new UpdateIntakeRequest { CurrentMeds = "Paracetamol" };
        await UpdateIntakeEndpoint.HandleUpdateIntake(groupId, req, BuildPatientContext(4), db, CancellationToken.None);

        var latest = db.IntakeRecords.First(r => r.IntakeGroupId == groupId);
        Assert.Equal(seed.ChiefComplaint, latest.ChiefComplaint);   // carried forward
        Assert.Equal("Paracetamol", latest.CurrentMeds);            // updated
        Assert.Equal(seed.Allergies, latest.Allergies);             // carried forward
        Assert.Equal(seed.MedicalHistory, latest.MedicalHistory);   // carried forward
    }

    // ── AC-005: wrong patient → 403 Forbidden ────────────────────────────────

    [Fact]
    public async Task UpdateIntake_WrongPatient_Returns403()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 5);

        var req = new UpdateIntakeRequest { ChiefComplaint = "Attempted edit" };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(99), db, CancellationToken.None);

        Assert.True(IsForbid(result));
        // No new version created.
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
    }

    // ── Guard: non-existent intakeGroupId → 404 ──────────────────────────────

    [Fact]
    public async Task UpdateIntake_UnknownGroupId_Returns404()
    {
        await using var db = BuildDb();

        var req = new UpdateIntakeRequest { ChiefComplaint = "anything" };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            Guid.NewGuid(), req, BuildPatientContext(6), db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── Guard: unauthenticated (no JWT sub) → 401 ────────────────────────────

    [Fact]
    public async Task UpdateIntake_NoJwtSub_Returns401()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 7);

        var req = new UpdateIntakeRequest { ChiefComplaint = "Anything" };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, new DefaultHttpContext(), db, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }

    // ── AC-002: field-length violation → 422 ─────────────────────────────────

    [Fact]
    public async Task UpdateIntake_ChiefComplaintTooLong_Returns422()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 8);

        var req = new UpdateIntakeRequest { ChiefComplaint = new string('x', 1001) };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(8), db, CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
        // No new version.
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GET /intake/{intakeGroupId} — GetIntakeEndpoint
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-002: GET without version → latest version ──────────────────────────

    [Fact]
    public async Task GetIntake_NoVersionParam_ReturnsLatest()
    {
        await using var db = BuildDb();
        var groupId = Guid.NewGuid();

        // Seed two versions: v1 (not latest), v2 (latest).
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId = 10, Source = IntakeSource.Manual, IntakeGroupId = groupId,
            Version = 1, IsLatest = false, SubmittedAt = DateTime.UtcNow, ChiefComplaint = "v1",
        });
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId = 10, Source = IntakeSource.Manual, IntakeGroupId = groupId,
            Version = 2, IsLatest = true, SubmittedAt = DateTime.UtcNow, ChiefComplaint = "v2",
        });
        await db.SaveChangesAsync();

        var result = await GetIntakeEndpoint.HandleGetIntake(groupId, null, BuildPatientContext(10), db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Extract response body to check returned version number.
        var json = System.Text.Json.JsonSerializer.Serialize(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Contains("\"version\":2", json);
        Assert.Contains("\"v2\"", json);
    }

    // ── AC-003: GET ?version=1 → returns historical version 1 ────────────────

    [Fact]
    public async Task GetIntake_WithVersionParam_ReturnsHistoricalVersion()
    {
        await using var db = BuildDb();
        var groupId = Guid.NewGuid();

        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId = 11, Source = IntakeSource.Manual, IntakeGroupId = groupId,
            Version = 1, IsLatest = false, SubmittedAt = DateTime.UtcNow, ChiefComplaint = "original",
        });
        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId = 11, Source = IntakeSource.Manual, IntakeGroupId = groupId,
            Version = 2, IsLatest = true, SubmittedAt = DateTime.UtcNow, ChiefComplaint = "updated",
        });
        await db.SaveChangesAsync();

        var result = await GetIntakeEndpoint.HandleGetIntake(groupId, 1, BuildPatientContext(11), db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var json = System.Text.Json.JsonSerializer.Serialize(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"original\"", json);
    }

    // ── AC-003: GET ?version=N where N does not exist → 404 ──────────────────

    [Fact]
    public async Task GetIntake_NonExistentVersion_Returns404()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 12);

        var result = await GetIntakeEndpoint.HandleGetIntake(seed.IntakeGroupId, 99, BuildPatientContext(12), db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── Guard: unknown intakeGroupId → 404 ───────────────────────────────────

    [Fact]
    public async Task GetIntake_UnknownGroupId_Returns404()
    {
        await using var db = BuildDb();

        var result = await GetIntakeEndpoint.HandleGetIntake(Guid.NewGuid(), null, BuildStaffContext(), db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── F1 fix: patient-role caller cannot read another patient's intake ──────

    [Fact]
    public async Task GetIntake_PatientRole_CannotReadOtherPatientIntake_Returns403()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 20);

        // Patient 99 tries to read patient 20's intake.
        var result = await GetIntakeEndpoint.HandleGetIntake(
            seed.IntakeGroupId, null, BuildPatientContext(99), db, CancellationToken.None);

        Assert.True(IsForbid(result));
    }

    // ── F2 fix: empty-string ChiefComplaint PATCH treated as no change ────────

    [Fact]
    public async Task UpdateIntake_EmptyStringChiefComplaint_TreatedAsNoChange()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 21);

        // Empty string should not overwrite existing value — treated as null (no change).
        var req = new UpdateIntakeRequest { ChiefComplaint = "" };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(21), db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        // No new version — all fields unchanged.
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
        var latest = db.IntakeRecords.First(r => r.IntakeGroupId == seed.IntakeGroupId);
        Assert.Equal(seed.ChiefComplaint, latest.ChiefComplaint);
    }

    // ── F3: optional PATCH fields → 422 on MaxLength violation ───────────────

    [Fact]
    public async Task UpdateIntake_CurrentMedsTooLong_Returns422()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 22);

        var req = new UpdateIntakeRequest { CurrentMeds = new string('x', 2001) };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(22), db, CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
    }

    [Fact]
    public async Task UpdateIntake_AllergiesTooLong_Returns422()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 23);

        var req = new UpdateIntakeRequest { Allergies = new string('x', 2001) };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(23), db, CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
    }

    // ── ES-004: MedicalHistory exceeds MaxLength(4000) → 422; no new version ──

    [Fact]
    public async Task UpdateIntake_MedicalHistoryTooLong_Returns422()
    {
        await using var db = BuildDb();
        var seed = await SeedLatestRecord(db, patientId: 24);

        var req = new UpdateIntakeRequest { MedicalHistory = new string('x', 4001) };
        var result = await UpdateIntakeEndpoint.HandleUpdateIntake(
            seed.IntakeGroupId, req, BuildPatientContext(24), db, CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
        Assert.Equal(1, db.IntakeRecords.IgnoreQueryFilters().Count(r => r.IntakeGroupId == seed.IntakeGroupId));
    }
}
