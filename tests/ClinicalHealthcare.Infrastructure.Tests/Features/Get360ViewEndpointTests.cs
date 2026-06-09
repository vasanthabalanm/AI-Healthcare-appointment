using ClinicalHealthcare.Api.Features.Patients;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>Unit tests for <see cref="Get360ViewEndpoint.HandleGet360View"/>.</summary>
public sealed class Get360ViewEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildSqlDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static ClinicalDbContext BuildPgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static Mock<ICacheService> NullCache()
    {
        var cache = new Mock<ICacheService>(MockBehavior.Loose);
        cache.Setup(c => c.GetAsync<PatientView360Dto>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((PatientView360Dto?)null);
        return cache;
    }

    private static UserAccount SeedPatient(ApplicationDbContext sqlDb, int id = 1)
    {
        var patient = new UserAccount
        {
            Id        = id,
            FirstName = "Jane",
            LastName  = "Doe",
            Email     = "jane@test.com",
            Role      = "patient",
            IsDeleted = false
        };
        sqlDb.UserAccounts.Add(patient);
        sqlDb.SaveChanges();
        return patient;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get360View_PatientNotFound_Returns404()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        var cache  = NullCache();
        var result = await Get360ViewEndpoint.HandleGet360View(
            99, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var status = result.GetType().GetProperty("StatusCode")?.GetValue(result);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task Get360View_CacheHit_ReturnsCachedView()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        var cached = new PatientView360Dto { PatientId = 1, FirstName = "Cached" };

        var cache = new Mock<ICacheService>(MockBehavior.Loose);
        cache.Setup(c => c.GetAsync<PatientView360Dto>(
                "view360:1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(cached);

        var result = await Get360ViewEndpoint.HandleGet360View(
            1, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var status = result.GetType().GetProperty("StatusCode")?.GetValue(result);
        var value  = result.GetType().GetProperty("Value")?.GetValue(result) as PatientView360Dto;

        Assert.Equal(200, status);
        Assert.NotNull(value);
        Assert.Equal("Cached", value.FirstName);

        // SetAsync should NOT be called — already cached.
        cache.Verify(c => c.SetAsync(
            It.IsAny<string>(), It.IsAny<PatientView360Dto>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get360View_CacheMiss_AssemblesFromDb_And_Caches()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        SeedPatient(sqlDb, 1);

        // Seed one extracted field.
        pgDb.ExtractedClinicalFields.Add(new ExtractedClinicalField
        {
            Id              = 1,
            PatientId       = 1,
            FieldName       = "Diagnosis",
            FieldValue      = "Hypertension",
            ConfidenceScore = 0.95,
            ExtractedAt     = DateTime.UtcNow.AddHours(-1),
            IsDeleted       = false
        });
        pgDb.SaveChanges();

        var cache  = NullCache();
        var result = await Get360ViewEndpoint.HandleGet360View(
            1, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var status = result.GetType().GetProperty("StatusCode")?.GetValue(result);
        var value  = result.GetType().GetProperty("Value")?.GetValue(result) as PatientView360Dto;

        Assert.Equal(200, status);
        Assert.NotNull(value);
        Assert.Equal(1, value.PatientId);
        Assert.NotEmpty(value.ClinicalFields);

        // Should populate cache.
        cache.Verify(c => c.SetAsync(
            It.Is<string>(k => k == "view360:1"),
            It.IsAny<PatientView360Dto>(),
            It.Is<TimeSpan>(t => t.TotalSeconds == 300),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get360View_NoDocuments_SetsHint()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        SeedPatient(sqlDb, 2);

        var cache  = NullCache();
        var result = await Get360ViewEndpoint.HandleGet360View(
            2, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var value = result.GetType().GetProperty("Value")?.GetValue(result) as PatientView360Dto;

        Assert.NotNull(value);
        Assert.Equal("No clinical documents uploaded yet", value.Hint);
    }

    [Fact]
    public async Task Get360View_HasDocuments_NoHint()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        SeedPatient(sqlDb, 3);

        // Seed a clinical document.
        sqlDb.ClinicalDocuments.Add(new ClinicalDocument
        {
            Id              = 1,
            PatientId       = 3,
            UploadedByStaffId = 99,
            OriginalFileName = "doc.pdf",
            EncryptedBlobPath = "some/path.enc",
            UploadedAt      = DateTime.UtcNow
        });
        sqlDb.SaveChanges();

        var cache  = NullCache();
        var result = await Get360ViewEndpoint.HandleGet360View(
            3, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var value = result.GetType().GetProperty("Value")?.GetValue(result) as PatientView360Dto;

        Assert.NotNull(value);
        Assert.Null(value.Hint);
    }

    [Fact]
    public async Task Get360View_UnresolvedConflicts_ReflectedInCount()
    {
        var sqlDb  = BuildSqlDb();
        var pgDb   = BuildPgDb();
        SeedPatient(sqlDb, 4);

        pgDb.ConflictFlags.AddRange(
            new ConflictFlag { Id = 1, PatientId = 4, FieldName = "DOB", Value1 = "1990-01-01", Value2 = "1991-01-01", Status = ConflictFlagStatus.Unresolved },
            new ConflictFlag { Id = 2, PatientId = 4, FieldName = "Name", Value1 = "Jane", Value2 = "Janet", Status = ConflictFlagStatus.Unresolved }
        );
        pgDb.SaveChanges();

        var cache  = NullCache();
        var result = await Get360ViewEndpoint.HandleGet360View(
            4, sqlDb, pgDb, cache.Object, CancellationToken.None);

        var value = result.GetType().GetProperty("Value")?.GetValue(result) as PatientView360Dto;

        Assert.NotNull(value);
        Assert.Equal(2, value.UnresolvedConflicts);
    }
}

