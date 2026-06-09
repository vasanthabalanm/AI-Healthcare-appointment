using ClinicalHealthcare.Api.Features.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_013b: staff/admin credential setup via one-time token.
/// Covers AC-001 to AC-005 and all edge cases.
/// </summary>
public sealed class SetupCredentialsEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static IPasswordHasher<string> CreateHasher() => new PasswordHasher<string>();

    private static UserAccount SeedAccount(ApplicationDbContext db,
        string email    = "staff@clinic.com",
        string role     = "staff",
        bool   isActive = true)
    {
        var hasher = CreateHasher();
        var account = new UserAccount
        {
            Email        = email,
            Role         = role,
            IsActive     = isActive,
            FirstName    = "Jane",
            LastName     = "Doe",
            PasswordHash = hasher.HashPassword(email, "TempInternal99!")
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();
        return account;
    }

    /// <summary>
    /// Issues a valid 48-hour setup token and stores its SHA-256 hash on the account,
    /// mirroring exactly what <c>CreateUserEndpoint</c> does.
    /// </summary>
    private static string IssueSetupToken(ApplicationDbContext db, UserAccount account,
        int expiryOffsetMinutes = 0)
    {
        var rawToken = "SetupToken_" + Guid.NewGuid().ToString("N");
        account.VerificationTokenHash   = SetupCredentialsEndpoint.ComputeSha256Hash(rawToken);
        account.VerificationTokenExpiry = DateTime.UtcNow.AddHours(48).AddMinutes(expiryOffsetMinutes);
        db.SaveChanges();
        return rawToken;
    }

    private static int StatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    // ── AC-001: valid token sets password and returns 200 ─────────────────────

    [Fact]
    public async Task SetupCredentials_ValidToken_Returns200_PasswordUpdated()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var token   = IssueSetupToken(db, account);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var updated = await db.UserAccounts.FindAsync(account.Id);
        // New password must have been accepted — verify the new hash validates correctly.
        var hasher2 = CreateHasher();
        var verifyResult = hasher2.VerifyHashedPassword(account.Email, updated!.PasswordHash, "NewSecure1!");
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
        // Token fields must be cleared.
        Assert.Null(updated.VerificationTokenHash);
        Assert.Null(updated.VerificationTokenExpiry);
    }

    // ── AC-001: message field present in 200 response ────────────────────────

    [Fact]
    public async Task SetupCredentials_ValidToken_Returns200_WithMessage()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var token   = IssueSetupToken(db, account);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Verify the exact message body the FE and email templates depend on.
        var valueProp = result.GetType().GetProperty("Value");
        var value     = valueProp?.GetValue(result);
        var msgProp   = value?.GetType().GetProperty("message");
        var message   = msgProp?.GetValue(value) as string;
        Assert.Equal("Credentials set successfully. You can now log in.", message);
    }

    // ── AC-002: second call with same token returns 400 ───────────────────────

    [Fact]
    public async Task SetupCredentials_TokenUsedTwice_SecondCallReturns400()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var token   = IssueSetupToken(db, account);
        var hasher  = CreateHasher();

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");

        // First call — must succeed.
        var first = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, hasher, CancellationToken.None);
        Assert.Equal(200, StatusCode(first));

        // Second call with same token — hash is null now, lookup returns null.
        var second = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, hasher, CancellationToken.None);
        Assert.Equal(400, StatusCode(second));
    }

    // ── AC-003: expired token returns 400 ─────────────────────────────────────

    [Fact]
    public async Task SetupCredentials_ExpiredToken_Returns400()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        // Set expiry in the past by subtracting enough minutes to exceed the 48-hour window.
        var token   = IssueSetupToken(db, account, expiryOffsetMinutes: -3000);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-003 edge: null expiry treated as expired ────────────────────────────

    [Fact]
    public async Task SetupCredentials_NullExpiry_Returns400()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var rawToken = "SetupToken_" + Guid.NewGuid().ToString("N");
        account.VerificationTokenHash   = SetupCredentialsEndpoint.ComputeSha256Hash(rawToken);
        account.VerificationTokenExpiry = null;  // explicitly null
        db.SaveChanges();

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(rawToken, "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-004: password < 8 chars returns 422 ────────────────────────────────

    [Fact]
    public async Task SetupCredentials_ShortPassword_Returns422()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var token   = IssueSetupToken(db, account);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "Short1");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── AC-004: whitespace-only password returns 422 before DB query ──────────

    [Fact]
    public async Task SetupCredentials_WhitespacePassword_Returns422()
    {
        var db = CreateDb();

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest("anytoken", "        ");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── AC-005: audit log entry written on success ────────────────────────────

    [Fact]
    public async Task SetupCredentials_ValidToken_WritesAuditLog()
    {
        var db      = CreateDb();
        var account = SeedAccount(db);
        var token   = IssueSetupToken(db, account);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");
        await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        var log = await db.AuditLogs
            .FirstOrDefaultAsync(l => l.EntityId == account.Id && l.Action == "CREDENTIALS-SET");

        Assert.NotNull(log);
        Assert.Equal("UserAccount", log!.EntityType);
        Assert.Equal(account.Id, log.ActorId);
        Assert.Null(log.BeforeValue);
        Assert.NotNull(log.AfterValue);
    }

    // ── Edge: unknown token returns 400 (no enumeration) ─────────────────────

    [Fact]
    public async Task SetupCredentials_UnknownToken_Returns400()
    {
        var db = CreateDb();
        // No account seeded — nothing in DB.

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(
            "completelyfaketoken", "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Edge: IsActive=false account still gets credentials set ──────────────

    [Fact]
    public async Task SetupCredentials_InactiveAccount_Returns200()
    {
        var db      = CreateDb();
        var account = SeedAccount(db, isActive: false);
        var token   = IssueSetupToken(db, account);

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest(token, "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        // Inactive accounts can still set up credentials — IsActive is controlled by admin.
        Assert.Equal(200, StatusCode(result));
    }

    // ── Edge: blank token returns 422 ─────────────────────────────────────────

    [Fact]
    public async Task SetupCredentials_BlankToken_Returns422()
    {
        var db = CreateDb();

        var request = new SetupCredentialsEndpoint.SetupCredentialsRequest("", "NewSecure1!");
        var result  = await SetupCredentialsEndpoint.HandleSetupCredentials(
            request, db, CreateHasher(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }
}
