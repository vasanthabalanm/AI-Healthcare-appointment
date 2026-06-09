using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Patients;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>Unit tests for <see cref="VerifyPatientEndpoint.HandleVerifyPatient"/>.</summary>
public sealed class VerifyPatientEndpointTests
{
    // -- Helpers ---------------------------------------------------------------

    private static ApplicationDbContext BuildSqlDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static HttpContext BuildHttpContext(string? sub)
    {
        var ctx = new DefaultHttpContext();
        if (sub is not null)
        {
            var identity = new ClaimsIdentity(new[] { new Claim("sub", sub) }, "test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        return ctx;
    }

    /// <summary>Returns a mock that reports 0 unresolved conflicts.</summary>
    private static Mock<IConflictService> NoConflicts()
    {
        var svc = new Mock<IConflictService>(MockBehavior.Loose);
        svc.Setup(s => s.GetUnresolvedCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(0);
        return svc;
    }

    private static UserAccount SeedPatient(
        ApplicationDbContext sqlDb, int id = 1,
        VerificationStatus status = VerificationStatus.Unverified)
    {
        var patient = new UserAccount
        {
            Id                 = id,
            FirstName          = "Jane",
            LastName           = "Doe",
            Email              = "jane@test.com",
            Role               = "patient",
            IsDeleted          = false,
            VerificationStatus = status
        };
        sqlDb.UserAccounts.Add(patient);
        sqlDb.SaveChanges();
        return patient;
    }

    // -- Tests -----------------------------------------------------------------

    [Fact]
    public async Task VerifyPatient_Returns401_WhenSubClaimMissing()
    {
        var sqlDb    = BuildSqlDb();
        var httpCtx  = BuildHttpContext(null);
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            1, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(401, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task VerifyPatient_Returns401_WhenSubClaimNonInteger()
    {
        var sqlDb    = BuildSqlDb();
        SeedPatient(sqlDb, 1);
        var httpCtx  = BuildHttpContext("not-an-int");
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            1, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(401, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task VerifyPatient_Returns404_WhenPatientNotFound()
    {
        var sqlDb    = BuildSqlDb();
        var httpCtx  = BuildHttpContext("99");
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            999, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(404, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task VerifyPatient_Returns409_WhenUnresolvedConflicts()
    {
        var sqlDb    = BuildSqlDb();
        SeedPatient(sqlDb, 1);
        var httpCtx  = BuildHttpContext("42");

        var conflict = new Mock<IConflictService>(MockBehavior.Loose);
        conflict.Setup(s => s.GetUnresolvedCountAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);

        var cache = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            1, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(409, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task VerifyPatient_Returns409_WhenAlreadyVerified()
    {
        var sqlDb    = BuildSqlDb();
        SeedPatient(sqlDb, 1, VerificationStatus.Verified);
        var httpCtx  = BuildHttpContext("42");
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            1, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(409, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task VerifyPatient_SetsVerifiedStatus_AndReturns200()
    {
        var sqlDb    = BuildSqlDb();
        SeedPatient(sqlDb, 1);
        var httpCtx  = BuildHttpContext("42");
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        var result = await VerifyPatientEndpoint.HandleVerifyPatient(
            1, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        Assert.Equal(200, result.GetType().GetProperty("StatusCode")?.GetValue(result));

        var patient = await sqlDb.UserAccounts.FindAsync(1);
        Assert.NotNull(patient);
        Assert.Equal(VerificationStatus.Verified, patient!.VerificationStatus);
        Assert.Equal(42, patient.VerifiedById);
        Assert.NotNull(patient.VerifiedAt);
    }

    [Fact]
    public async Task VerifyPatient_InvalidatesCacheOnSuccess()
    {
        var sqlDb    = BuildSqlDb();
        SeedPatient(sqlDb, 5);
        var httpCtx  = BuildHttpContext("42");
        var conflict = NoConflicts();
        var cache    = new Mock<ICacheService>(MockBehavior.Loose);

        await VerifyPatientEndpoint.HandleVerifyPatient(
            5, httpCtx, sqlDb, conflict.Object, cache.Object, CancellationToken.None);

        cache.Verify(c => c.DeleteAsync("view360:5", It.IsAny<CancellationToken>()), Times.Once);
    }
}
