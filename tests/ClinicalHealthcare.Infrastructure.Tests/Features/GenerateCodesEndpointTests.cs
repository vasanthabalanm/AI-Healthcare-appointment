using ClinicalHealthcare.Api.Features.Coding;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>Unit tests for <see cref="GenerateCodesEndpoint.HandleGenerateCodes"/> (TASK_045 AC-001, TASK_046 AC-001).</summary>
public sealed class GenerateCodesEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static Mock<IBackgroundJobClient> BuildJobs()
    {
        var jobs = new Mock<IBackgroundJobClient>(MockBehavior.Loose);
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns(Guid.NewGuid().ToString());
        return jobs;
    }

    private static UserAccount SeedPatient(
        ApplicationDbContext db, int id = 1,
        VerificationStatus status = VerificationStatus.Unverified)
    {
        var p = new UserAccount
        {
            Id                 = id,
            FirstName          = "Jane",
            LastName           = "Doe",
            Email              = "jane@test.com",
            Role               = "patient",
            IsDeleted          = false,
            VerificationStatus = status
        };
        db.UserAccounts.Add(p);
        db.SaveChanges();
        return p;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCodes_Returns400_WhenTypeUnsupported()
    {
        await using var db = BuildDb();
        SeedPatient(db, 1, VerificationStatus.Verified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            1, "SNOMED", db, jobs.Object, CancellationToken.None);

        Assert.Equal(400, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_Returns404_WhenPatientNotFound()
    {
        await using var db = BuildDb();
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            999, "ICD10", db, jobs.Object, CancellationToken.None);

        Assert.Equal(404, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_Returns409_WhenPatientNotVerified()
    {
        await using var db = BuildDb();
        SeedPatient(db, 1, VerificationStatus.Unverified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            1, "ICD10", db, jobs.Object, CancellationToken.None);

        Assert.Equal(409, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_Returns202_WhenVerified()
    {
        await using var db = BuildDb();
        SeedPatient(db, 1, VerificationStatus.Verified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            1, "ICD10", db, jobs.Object, CancellationToken.None);

        Assert.Equal(202, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_EnqueuesGenerateIcd10CodesJob_WhenVerified()
    {
        await using var db = BuildDb();
        SeedPatient(db, 2, VerificationStatus.Verified);
        var jobs = BuildJobs();

        await GenerateCodesEndpoint.HandleGenerateCodes(
            2, "ICD10", db, jobs.Object, CancellationToken.None);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(GenerateIcd10CodesJob)),
            It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task GenerateCodes_CaseInsensitive_ICD10_Returns202()
    {
        await using var db = BuildDb();
        SeedPatient(db, 3, VerificationStatus.Verified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            3, "icd10", db, jobs.Object, CancellationToken.None);

        Assert.Equal(202, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    // ── TASK_046: CPT tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCodes_CPT_Returns202_WhenVerified()
    {
        await using var db = BuildDb();
        SeedPatient(db, 4, VerificationStatus.Verified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            4, "CPT", db, jobs.Object, CancellationToken.None);

        Assert.Equal(202, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_CPT_EnqueuesGenerateCptCodesJob()
    {
        await using var db = BuildDb();
        SeedPatient(db, 5, VerificationStatus.Verified);
        var jobs = BuildJobs();

        await GenerateCodesEndpoint.HandleGenerateCodes(
            5, "CPT", db, jobs.Object, CancellationToken.None);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(GenerateCptCodesJob)),
            It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task GenerateCodes_CPT_Returns409_WhenNotVerified()
    {
        await using var db = BuildDb();
        SeedPatient(db, 6, VerificationStatus.Unverified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            6, "CPT", db, jobs.Object, CancellationToken.None);

        Assert.Equal(409, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_CPT_CaseInsensitive_Returns202()
    {
        await using var db = BuildDb();
        SeedPatient(db, 7, VerificationStatus.Verified);
        var jobs = BuildJobs();

        var result = await GenerateCodesEndpoint.HandleGenerateCodes(
            7, "cpt", db, jobs.Object, CancellationToken.None);

        Assert.Equal(202, result.GetType().GetProperty("StatusCode")?.GetValue(result));
    }

    [Fact]
    public async Task GenerateCodes_ICD10_DoesNotEnqueueCptJob()
    {
        await using var db = BuildDb();
        SeedPatient(db, 8, VerificationStatus.Verified);
        var jobs = BuildJobs();

        await GenerateCodesEndpoint.HandleGenerateCodes(
            8, "ICD10", db, jobs.Object, CancellationToken.None);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(GenerateCptCodesJob)),
            It.IsAny<IState>()), Times.Never);
    }
}
