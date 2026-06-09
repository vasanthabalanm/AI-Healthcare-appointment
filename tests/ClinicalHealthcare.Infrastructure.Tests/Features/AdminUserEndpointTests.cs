using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Admin;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_013: Admin user account lifecycle.
///
/// Authorization enforcement (401/403 for non-admin JWTs) requires an integration
/// test host and is verified by code inspection (.RequireAuthorization() declared
/// on both endpoints). All handler business-logic is tested here directly.
/// </summary>
public sealed class AdminUserEndpointTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // InMemory provider does not support transactions; suppress the warning so
            // tests exercise the same handler path as production without throwing.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IPasswordHasher<string> CreateHasher() => new PasswordHasher<string>();
    private static IEmailService CreateFakeEmail() => new FakeAdminEmailService();

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://localhost:7001"
            })
            .Build();

    private static HttpContext AdminContext(int actorId = 99) =>
        new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, "admin")
                ], authenticationType: "Test"))
        };

    private static CreateUserRequest ValidCreateRequest(
        string email = "newstaff@test.com",
        string role  = "staff") => new()
    {
        Email     = email,
        FirstName = "New",
        LastName  = "Staff",
        Role      = role
    };

    private static int StatusCode(IResult result)
    {
        var sc = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        return sc.StatusCode ?? throw new InvalidOperationException("StatusCode was null");
    }

    // Helper: seed a UserAccount directly into the context.
    private static async Task<UserAccount> SeedUser(
        ApplicationDbContext db,
        string email = "existing@test.com",
        string role  = "admin",
        bool isActive = true)
    {
        var account = new UserAccount
        {
            Email        = email,
            PasswordHash = "hash",
            Role         = role,
            IsActive     = isActive,
            FirstName    = "Existing",
            LastName     = "User"
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    // ── AC-001: POST /admin/users returns 201 ─────────────────────────────────

    [Fact]
    public async Task CreateUser_WithValidRequest_Returns201()
    {
        await using var db = CreateDb();

        var result = await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest(), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusCode(result));
    }

    [Fact]
    public async Task CreateUser_CreatesAccount_WithCorrectRole()
    {
        await using var db = CreateDb();

        await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("admin2@test.com", "admin"), db, CreateHasher(),
            CreateFakeEmail(), AdminContext(), CreateConfig(), CancellationToken.None);

        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "admin2@test.com");
        Assert.Equal("admin", account.Role);
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_Returns409()
    {
        await using var db = CreateDb();
        await SeedUser(db, "dup@test.com");

        var result = await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("dup@test.com"), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(result));
    }

    [Fact]
    public async Task CreateUser_InvalidRole_Returns422()
    {
        await using var db = CreateDb();

        var result = await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("x@test.com", "superuser"), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusCode(result));
    }

    // ── AC-004: AuditLog written on create ────────────────────────────────────

    [Fact]
    public async Task CreateUser_WritesAuditLog_InsertEntry()
    {
        await using var db = CreateDb();

        await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("audited@test.com"), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(actorId: 1), CreateConfig(), CancellationToken.None);

        var log = await db.AuditLogs.FirstAsync();
        Assert.Equal("UserAccount", log.EntityType);
        Assert.Equal("INSERT", log.Action);
        Assert.Null(log.BeforeValue);
        Assert.NotNull(log.AfterValue);
        Assert.Equal(1, log.ActorId);
    }

    // ── AC-005: Credential setup email sent ───────────────────────────────────

    [Fact]
    public async Task CreateUser_SendsCredentialEmail_ToCorrectAddress()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeAdminEmailService();

        await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("newuser@test.com"), db, CreateHasher(), fakeEmail,
            AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal("newuser@test.com", fakeEmail.LastToEmail);
        Assert.NotNull(fakeEmail.LastSubject);
        Assert.Contains("credentials", fakeEmail.LastSubject, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(fakeEmail.LastHtmlBody);
        Assert.Contains("/auth/setup-credentials?token=", fakeEmail.LastHtmlBody);
    }

    [Fact]
    public async Task CreateUser_SetsVerificationToken_With48hExpiry()
    {
        await using var db = CreateDb();
        var before = DateTime.UtcNow;

        await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("tokentest@test.com"), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(), CreateConfig(), CancellationToken.None);

        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "tokentest@test.com");
        Assert.NotNull(account.VerificationTokenHash);
        Assert.NotNull(account.VerificationTokenExpiry);
        var expiry = account.VerificationTokenExpiry!.Value;
        Assert.InRange(expiry, before.AddHours(47).AddMinutes(59), before.AddHours(48).AddSeconds(5));
    }

    // ── AC-002: PATCH updates details ─────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_UpdatesName_Returns200()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "staff1@test.com", "staff");

        var result = await UpdateUserEndpoint.HandleUpdateUser(
            user.Id,
            new UpdateUserRequest { FirstName = "Updated", LastName = "Name" },
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        var updated = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Id == user.Id);
        Assert.Equal("Updated", updated.FirstName);
        Assert.Equal("Name", updated.LastName);
    }

    [Fact]
    public async Task UpdateUser_DeactivatesNonLastAdmin_Returns200()
    {
        await using var db = CreateDb();
        // Two active admins — deactivating one is allowed.
        var admin1 = await SeedUser(db, "admin1@test.com", "admin");
        await SeedUser(db, "admin2@test.com", "admin");

        var result = await UpdateUserEndpoint.HandleUpdateUser(
            admin1.Id,
            new UpdateUserRequest { IsActive = false },
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        var updated = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Id == admin1.Id);
        Assert.False(updated.IsActive);
    }

    // ── AC-003: Last-Admin guard returns 409 ─────────────────────────────────

    [Fact]
    public async Task UpdateUser_DeactivateLastAdmin_Returns409()
    {
        await using var db = CreateDb();
        var onlyAdmin = await SeedUser(db, "solo-admin@test.com", "admin");

        var result = await UpdateUserEndpoint.HandleUpdateUser(
            onlyAdmin.Id,
            new UpdateUserRequest { IsActive = false },
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(result));
        // Account must remain active.
        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Id == onlyAdmin.Id);
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task UpdateUser_NotFound_Returns404()
    {
        await using var db = CreateDb();

        var result = await UpdateUserEndpoint.HandleUpdateUser(
            9999, new UpdateUserRequest { FirstName = "X" },
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusCode(result));
    }

    // ── AC-004: AuditLog written on update ────────────────────────────────────

    [Fact]
    public async Task UpdateUser_WritesAuditLog_WithBeforeAndAfterValues()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "staff2@test.com", "staff");

        await UpdateUserEndpoint.HandleUpdateUser(
            user.Id,
            new UpdateUserRequest { FirstName = "Changed", IsActive = false },
            db, AdminContext(actorId: 5), CancellationToken.None);

        var log = await db.AuditLogs.FirstAsync();
        Assert.Equal("UserAccount", log.EntityType);
        Assert.Equal("UPDATE", log.Action);
        Assert.NotNull(log.BeforeValue);
        Assert.NotNull(log.AfterValue);
        Assert.Equal(5, log.ActorId);
        Assert.Contains("Existing", log.BeforeValue);   // original FirstName
        Assert.Contains("Changed",  log.AfterValue);    // updated FirstName
    }

    [Fact]
    public async Task UpdateUser_NoChanges_StillWritesAuditLog()
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "nochange@test.com", "staff");

        // PATCH with no actual changes — audit entry still written (idempotent + auditable).
        await UpdateUserEndpoint.HandleUpdateUser(
            user.Id, new UpdateUserRequest(),
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    // ── F3: Email validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("a@")]
    [InlineData("@b")]
    [InlineData("notanemail")]
    [InlineData("missing-at-sign")]
    public async Task CreateUser_MalformedEmail_Returns422(string badEmail)
    {
        await using var db = CreateDb();

        var result = await CreateUserEndpoint.HandleCreateUser(
            new CreateUserRequest { Email = badEmail, FirstName = "X", LastName = "Y", Role = "staff" },
            db, CreateHasher(), CreateFakeEmail(), AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusCode(result));
    }

    [Fact]
    public async Task CreateUser_CaseInsensitiveDuplicateEmail_Returns409()
    {
        await using var db = CreateDb();
        await SeedUser(db, "user@test.com");

        // Send same email in uppercase — normalization must detect it as duplicate.
        var result = await CreateUserEndpoint.HandleCreateUser(
            ValidCreateRequest("USER@TEST.COM"), db, CreateHasher(), CreateFakeEmail(),
            AdminContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(result));
    }

    // ── F4: Blank-after-trim guard ────────────────────────────────────────────

    [Theory]
    [InlineData("   ", "ValidLast")]
    [InlineData("ValidFirst", "   ")]
    public async Task UpdateUser_WhitespaceOnlyName_Returns422(string firstName, string lastName)
    {
        await using var db = CreateDb();
        var user = await SeedUser(db, "ws@test.com", "staff");

        var result = await UpdateUserEndpoint.HandleUpdateUser(
            user.Id,
            new UpdateUserRequest { FirstName = firstName, LastName = lastName },
            db, AdminContext(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusCode(result));
    }
}

/// <summary>No-op email service for admin endpoint tests. Captures last sent message.</summary>
internal sealed class FakeAdminEmailService : IEmailService
{
    public string? LastToEmail  { get; private set; }
    public string? LastSubject  { get; private set; }
    public string? LastHtmlBody { get; private set; }

    public Task SendAsync(string toEmail, string subject, string htmlBody,
        CancellationToken cancellationToken = default)
    {
        LastToEmail  = toEmail;
        LastSubject  = subject;
        LastHtmlBody = htmlBody;
        return Task.CompletedTask;
    }
}
