using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Patients;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_043: conflict detection and staff resolution endpoints.
/// Covers AC-001 (GET all conflicts), AC-002 (resolve), AC-003 (dismiss),
/// and IConflictService query helpers.
/// </summary>
public sealed class ConflictEndpointsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClinicalDbContext CreatePgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static HttpContext BuildStaffContext(int staffId = 10)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static ConflictFlag SeedFlag(
        ClinicalDbContext pgDb,
        int patientId,
        ConflictFlagStatus status = ConflictFlagStatus.Unresolved)
    {
        var flag = new ConflictFlag
        {
            PatientId  = patientId,
            FieldName  = "BloodPressure",
            Value1     = "120/80",
            Value2     = "130/85",
            Status     = status
        };
        pgDb.ConflictFlags.Add(flag);
        pgDb.SaveChanges();
        return flag;
    }

    private static int StatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    // ── GET /patients/{id}/conflicts ──────────────────────────────────────────

    [Fact]
    public async Task GetConflicts_ReturnsAllStatusFlags()
    {
        await using var db = CreatePgDb();
        SeedFlag(db, patientId: 1, ConflictFlagStatus.Unresolved);
        SeedFlag(db, patientId: 1, ConflictFlagStatus.Resolved);
        SeedFlag(db, patientId: 1, ConflictFlagStatus.Dismissed);

        var result = await GetConflictsEndpoint.HandleGetConflicts(1, db, default);

        Assert.Equal(200, StatusCode(result));

        var valueProperty = result.GetType().GetProperty("Value");
        var flags = (List<ConflictFlag>?)valueProperty?.GetValue(result);
        Assert.NotNull(flags);
        Assert.Equal(3, flags.Count);
        Assert.Contains(flags, f => f.Status == ConflictFlagStatus.Unresolved);
        Assert.Contains(flags, f => f.Status == ConflictFlagStatus.Resolved);
        Assert.Contains(flags, f => f.Status == ConflictFlagStatus.Dismissed);
    }

    [Fact]
    public async Task GetConflicts_ReturnsOnlyFlagsForRequestedPatient()
    {
        await using var db = CreatePgDb();
        SeedFlag(db, patientId: 1);
        SeedFlag(db, patientId: 2);

        var result = await GetConflictsEndpoint.HandleGetConflicts(1, db, default);

        // Use reflection to get the value from Ok<List<ConflictFlag>>
        var valueProperty = result.GetType().GetProperty("Value");
        var flags = (List<ConflictFlag>?)valueProperty?.GetValue(result);
        Assert.NotNull(flags);
        Assert.Single(flags);
        Assert.All(flags, f => Assert.Equal(1, f.PatientId));
    }

    [Fact]
    public async Task GetConflicts_ReturnsEmptyList_WhenNoFlagsExist()
    {
        await using var db = CreatePgDb();

        var result = await GetConflictsEndpoint.HandleGetConflicts(99, db, default);

        var valueProperty = result.GetType().GetProperty("Value");
        var flags = (List<ConflictFlag>?)valueProperty?.GetValue(result);
        Assert.NotNull(flags);
        Assert.Empty(flags);
    }

    // ── PATCH /conflicts/{id}/resolve ─────────────────────────────────────────

    [Fact]
    public async Task ResolveConflict_SetsResolvedWithStaffId()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Unresolved);
        var ctx  = BuildStaffContext(staffId: 42);

        var result = await ResolveConflictEndpoint.HandleResolveConflict(flag.Id, ctx, db, default);

        Assert.Equal(200, StatusCode(result));

        var updated = await db.ConflictFlags.FindAsync(flag.Id);
        Assert.Equal(ConflictFlagStatus.Resolved, updated!.Status);
        Assert.Equal(42, updated.ResolvedByStaffId);
    }

    [Fact]
    public async Task ResolveConflict_Returns404_WhenFlagNotFound()
    {
        await using var db = CreatePgDb();
        var ctx = BuildStaffContext();

        var result = await ResolveConflictEndpoint.HandleResolveConflict(9999, ctx, db, default);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task ResolveConflict_Returns409_WhenAlreadyResolved()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Resolved);
        var ctx  = BuildStaffContext();

        var result = await ResolveConflictEndpoint.HandleResolveConflict(flag.Id, ctx, db, default);

        Assert.Equal(409, StatusCode(result));
    }

    [Fact]
    public async Task ResolveConflict_Returns409_WhenAlreadyDismissed()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Dismissed);
        var ctx  = BuildStaffContext();

        var result = await ResolveConflictEndpoint.HandleResolveConflict(flag.Id, ctx, db, default);

        Assert.Equal(409, StatusCode(result));
    }

    [Fact]
    public async Task ResolveConflict_Returns401_WhenSubClaimMissing()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1);
        var ctx  = new DefaultHttpContext(); // no claims

        var result = await ResolveConflictEndpoint.HandleResolveConflict(flag.Id, ctx, db, default);

        Assert.Equal(401, StatusCode(result));
    }

    [Fact]
    public async Task ResolveConflict_Returns401_WhenSubClaimNonInteger()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1);
        var ctx  = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "not-an-int")],
            "TestAuth"));

        var result = await ResolveConflictEndpoint.HandleResolveConflict(flag.Id, ctx, db, default);

        Assert.Equal(401, StatusCode(result));
    }

    // ── PATCH /conflicts/{id}/dismiss ─────────────────────────────────────────

    [Fact]
    public async Task DismissConflict_SetsDismissed()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Unresolved);

        var result = await DismissConflictEndpoint.HandleDismissConflict(flag.Id, db, default);

        Assert.Equal(200, StatusCode(result));

        var updated = await db.ConflictFlags.FindAsync(flag.Id);
        Assert.Equal(ConflictFlagStatus.Dismissed, updated!.Status);
    }

    [Fact]
    public async Task DismissConflict_Returns404_WhenFlagNotFound()
    {
        await using var db = CreatePgDb();

        var result = await DismissConflictEndpoint.HandleDismissConflict(9999, db, default);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task DismissConflict_Returns409_WhenAlreadyResolved()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Resolved);

        var result = await DismissConflictEndpoint.HandleDismissConflict(flag.Id, db, default);

        Assert.Equal(409, StatusCode(result));
    }

    [Fact]
    public async Task DismissConflict_Returns409_WhenAlreadyDismissed()
    {
        await using var db = CreatePgDb();
        var flag = SeedFlag(db, patientId: 1, ConflictFlagStatus.Dismissed);

        var result = await DismissConflictEndpoint.HandleDismissConflict(flag.Id, db, default);

        Assert.Equal(409, StatusCode(result));
    }

    // ── IConflictService ──────────────────────────────────────────────────────

    [Fact]
    public async Task HasUnresolvedConflicts_ReturnsTrueWhenExists()
    {
        await using var db = CreatePgDb();
        SeedFlag(db, patientId: 5, ConflictFlagStatus.Unresolved);

        var svc    = new ConflictService(db);
        var result = await svc.HasUnresolvedConflictsAsync(5);

        Assert.True(result);
    }

    [Fact]
    public async Task HasUnresolvedConflicts_ReturnsFalseWhenNone()
    {
        await using var db = CreatePgDb();

        var svc    = new ConflictService(db);
        var result = await svc.HasUnresolvedConflictsAsync(99);

        Assert.False(result);
    }

    [Fact]
    public async Task HasUnresolvedConflicts_IgnoresResolvedAndDismissed()
    {
        await using var db = CreatePgDb();
        SeedFlag(db, patientId: 7, ConflictFlagStatus.Resolved);
        SeedFlag(db, patientId: 7, ConflictFlagStatus.Dismissed);

        var svc    = new ConflictService(db);
        var result = await svc.HasUnresolvedConflictsAsync(7);

        Assert.False(result);
    }
}
