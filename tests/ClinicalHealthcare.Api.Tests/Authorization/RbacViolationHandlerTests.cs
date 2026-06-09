using System.Security.Claims;
using ClinicalHealthcare.Api.Authorization;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="RbacViolationHandler"/> (AC-002) and
/// the named RBAC policies (AC-003 / AC-004).
/// </summary>
public sealed class RbacViolationHandlerTests
{
    // -- Helpers ----------------------------------------------------------

    private static ApplicationDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IServiceScopeFactory CreateScopeFactory(string dbName)
    {
        var services = new ServiceCollection();
        // Each scope creates its own DbContext instance for the named database.
        // This avoids the outer test's db instance being disposed when the handler
        // scope is disposed, while still sharing the same in-memory store.
        services.AddScoped<ApplicationDbContext>(_ => CreateDb(dbName));
        services.AddAuthorization();
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// Creates a DefaultHttpContext backed by a real MemoryStream response body
    /// so that OnStarting callbacks fire when <see cref="FireableResponseFeature.FireAsync"/> is called.
    /// </summary>
    private static (DefaultHttpContext ctx, FireableResponseFeature feature)
        BuildContext(int statusCode, string? role = null, int? actorId = null)
    {
        var ctx     = new DefaultHttpContext();
        var feature = new FireableResponseFeature { StatusCode = statusCode };
        ctx.Features.Set<IHttpResponseFeature>(feature);

        var claims = new List<Claim>();
        if (role is not null)    claims.Add(new Claim(ClaimTypes.Role, role));
        if (actorId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, actorId.Value.ToString()));

        // ClaimsPrincipal directly — IsInRole checks the claims collection.
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return (ctx, feature);
    }

    // -- AC-002: audit entry written on 403 -------------------------------

    [Fact]
    public async Task OnForbidden_WritesRbacViolationAuditEntry()
    {
        var dbName  = Guid.NewGuid().ToString();
        var handler = new RbacViolationHandler(_ => Task.CompletedTask, CreateScopeFactory(dbName));
        var (ctx, feature) = BuildContext(StatusCodes.Status403Forbidden, role: "staff", actorId: 42);

        await handler.InvokeAsync(ctx);
        await feature.FireAsync();

        // Use a fresh context on the same named store for the assertion;
        // the handler scope already disposed its own instance.
        await using var assertDb = CreateDb(dbName);
        var entry = await assertDb.AuditLogs.SingleAsync();
        Assert.Equal("RBAC-Violation", entry.Action);
        Assert.Equal("Endpoint", entry.EntityType);
        Assert.Equal(42, entry.ActorId);
        // Verify the AfterValue JSON contains all required fields (AC-002 shape contract).
        Assert.Contains("actualRole",        entry.AfterValue ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attemptedEndpoint", entry.AfterValue ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requiredRole",      entry.AfterValue ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnSuccess_NoAuditEntryWritten()
    {
        var dbName  = Guid.NewGuid().ToString();
        var handler = new RbacViolationHandler(_ => Task.CompletedTask, CreateScopeFactory(dbName));
        var (ctx, feature) = BuildContext(StatusCodes.Status200OK, role: "admin", actorId: 1);

        await handler.InvokeAsync(ctx);
        await feature.FireAsync();

        await using var assertDb = CreateDb(dbName);
        Assert.Equal(0, await assertDb.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task OnForbidden_AuditFailure_DoesNotAlterResponseStatus()
    {
        // Scope factory that throws when the DB is requested.
        var services = new ServiceCollection();
        services.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("DB unavailable"));
        services.AddAuthorization();
        services.AddLogging();
        var brokenFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var handler = new RbacViolationHandler(_ => Task.CompletedTask, brokenFactory);
        var (ctx, feature) = BuildContext(StatusCodes.Status403Forbidden);

        await handler.InvokeAsync(ctx);
        var ex = await Record.ExceptionAsync(() => feature.FireAsync());

        Assert.Null(ex);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    // -- AC-003: AdminOnly policy role enforcement ------------------------

    [Theory]
    [InlineData("staff")]
    [InlineData("patient")]
    public async Task AdminOnly_Policy_RejectsNonAdminRoles(string role)
    {
        var authService = BuildAuthorizationService(opts =>
            opts.AddPolicy("AdminOnly", p => p.RequireRole("admin")));

        var result = await authService.AuthorizeAsync(
            BuildPrincipal(role), null, "AdminOnly");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AdminOnly_Policy_AllowsAdminRole()
    {
        var authService = BuildAuthorizationService(opts =>
            opts.AddPolicy("AdminOnly", p => p.RequireRole("admin")));

        var result = await authService.AuthorizeAsync(
            BuildPrincipal("admin"), null, "AdminOnly");

        Assert.True(result.Succeeded);
    }

    // -- AC-004: StaffOrAdmin policy role enforcement ---------------------

    [Theory]
    [InlineData("patient")]
    public async Task StaffOrAdmin_Policy_RejectsPatient(string role)
    {
        var authService = BuildAuthorizationService(opts =>
            opts.AddPolicy("StaffOrAdmin", p => p.RequireRole("staff", "admin")));

        var result = await authService.AuthorizeAsync(
            BuildPrincipal(role), null, "StaffOrAdmin");

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("admin")]
    public async Task StaffOrAdmin_Policy_AllowsStaffAndAdmin(string role)
    {
        var authService = BuildAuthorizationService(opts =>
            opts.AddPolicy("StaffOrAdmin", p => p.RequireRole("staff", "admin")));

        var result = await authService.AuthorizeAsync(
            BuildPrincipal(role), null, "StaffOrAdmin");

        Assert.True(result.Succeeded);
    }

    // -- Private factories -------------------------------------------------

    private static IAuthorizationService BuildAuthorizationService(
        Action<AuthorizationOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddAuthorization(configure);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Returns an authenticated <see cref="ClaimsPrincipal"/> carrying
    /// the specified role claim. Uses <see cref="ClaimsPrincipal"/> directly
    /// (not <see cref="System.Security.Principal.GenericPrincipal"/>) so that
    /// <c>IsInRole</c> checks the claims collection.
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], authenticationType: "Test"));
}

// FireableResponseFeature moved to tests/ClinicalHealthcare.Api.Tests/TestHelpers/FireableResponseFeature.cs
