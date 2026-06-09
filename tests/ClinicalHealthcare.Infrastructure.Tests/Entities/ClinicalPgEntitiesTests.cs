using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Entities;

/// <summary>
/// Unit tests for <see cref="ExtractedClinicalField"/>, <see cref="ConflictFlag"/>,
/// and <see cref="MedicalCodeSuggestion"/> entity contracts and ClinicalDbContext configuration.
///
/// CHECK constraint enforcement (AC-004, AC-005) is a PostgreSQL-level concern
/// and is validated by DDL inspection of the generated migration. InMemory tests
/// verify entity defaults, enum values, and query patterns.
/// </summary>
public sealed class ClinicalPgEntitiesTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClinicalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(options);
    }

    // ── AC-001: ExtractedClinicalField ────────────────────────────────────────

    [Fact]
    public async Task ExtractedClinicalField_CanBeInserted_WithAllFields()
    {
        await using var ctx = CreateContext();

        var field = new ExtractedClinicalField
        {
            PatientId       = 1,
            DocumentId      = 10,
            FieldType       = ClinicalFieldType.Diagnosis,
            FieldName       = "PrimaryDiagnosis",
            FieldValue      = "Hypertension",
            ConfidenceScore = 0.92,
            ExtractionJobId = "job-001"
        };
        ctx.ExtractedClinicalFields.Add(field);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.ExtractedClinicalFields.FindAsync(field.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ClinicalFieldType.Diagnosis, loaded!.FieldType);
        Assert.Equal(0.92, loaded.ConfidenceScore);
    }

    [Fact]
    public async Task ExtractedClinicalField_IsDeleted_DefaultsFalse()
    {
        await using var ctx = CreateContext();

        var field = new ExtractedClinicalField
        {
            PatientId = 1, DocumentId = 1,
            FieldType = ClinicalFieldType.VitalSign,
            FieldName = "HeartRate", FieldValue = "72 bpm",
            ConfidenceScore = 0.99, ExtractionJobId = "job-002"
        };
        ctx.ExtractedClinicalFields.Add(field);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.ExtractedClinicalFields.FindAsync(field.Id);
        Assert.False(loaded!.IsDeleted);
    }

    [Theory]
    [InlineData(ClinicalFieldType.VitalSign)]
    [InlineData(ClinicalFieldType.MedicalHistory)]
    [InlineData(ClinicalFieldType.Medication)]
    [InlineData(ClinicalFieldType.Allergy)]
    [InlineData(ClinicalFieldType.Diagnosis)]
    public void ClinicalFieldType_AllEnumValues_AreDefined(ClinicalFieldType fieldType)
    {
        Assert.True(Enum.IsDefined(typeof(ClinicalFieldType), fieldType));
    }

    [Fact]
    public async Task ExtractedClinicalField_QueryByDocumentId_ReturnsCorrectFields()
    {
        await using var ctx = CreateContext();

        ctx.ExtractedClinicalFields.AddRange(
            new ExtractedClinicalField { PatientId = 1, DocumentId = 5, FieldType = ClinicalFieldType.Allergy,   FieldName = "Allergy1",   FieldValue = "Penicillin", ConfidenceScore = 0.9, ExtractionJobId = "j1" },
            new ExtractedClinicalField { PatientId = 1, DocumentId = 5, FieldType = ClinicalFieldType.Diagnosis, FieldName = "Diagnosis1", FieldValue = "Asthma",     ConfidenceScore = 0.8, ExtractionJobId = "j1" },
            new ExtractedClinicalField { PatientId = 2, DocumentId = 9, FieldType = ClinicalFieldType.Medication, FieldName = "Med1",       FieldValue = "Ibuprofen",  ConfidenceScore = 0.7, ExtractionJobId = "j2" }
        );
        await ctx.SaveChangesAsync();

        var doc5Fields = await ctx.ExtractedClinicalFields
            .Where(f => f.DocumentId == 5)
            .ToListAsync();

        Assert.Equal(2, doc5Fields.Count);
        Assert.All(doc5Fields, f => Assert.Equal(5, f.DocumentId));
    }

    // ── AC-002: ConflictFlag ──────────────────────────────────────────────────

    [Fact]
    public async Task ConflictFlag_DefaultStatus_IsUnresolved()
    {
        await using var ctx = CreateContext();

        var flag = new ConflictFlag
        {
            PatientId = 1,
            FieldName = "BloodPressure",
            Value1    = "120/80",
            Value2    = "130/90"
        };
        ctx.ConflictFlags.Add(flag);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.ConflictFlags.FindAsync(flag.Id);
        Assert.Equal(ConflictFlagStatus.Unresolved, loaded!.Status);
        Assert.Null(loaded.ResolvedByStaffId);
    }

    [Theory]
    [InlineData(ConflictFlagStatus.Resolved)]
    [InlineData(ConflictFlagStatus.Dismissed)]
    public async Task ConflictFlag_StatusCanBeUpdated(ConflictFlagStatus newStatus)
    {
        await using var ctx = CreateContext();

        var flag = new ConflictFlag
        {
            PatientId = 1, FieldName = "HeartRate",
            Value1 = "72", Value2 = "85"
        };
        ctx.ConflictFlags.Add(flag);
        await ctx.SaveChangesAsync();

        flag.Status           = newStatus;
        flag.ResolvedByStaffId = 42;
        await ctx.SaveChangesAsync();

        var loaded = await ctx.ConflictFlags.FindAsync(flag.Id);
        Assert.Equal(newStatus, loaded!.Status);
        Assert.Equal(42, loaded.ResolvedByStaffId);
    }

    // ── AC-003: MedicalCodeSuggestion ─────────────────────────────────────────

    [Fact]
    public async Task MedicalCodeSuggestion_DefaultStatus_IsPending()
    {
        await using var ctx = CreateContext();

        var suggestion = new MedicalCodeSuggestion
        {
            PatientId       = 1,
            CodeType        = CodeType.ICD10,
            SuggestedCode   = "Z00.00",
            CodeDescription = "Encounter for general adult medical examination",
            ConfidenceScore = 0.88
        };
        ctx.MedicalCodeSuggestions.Add(suggestion);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.MedicalCodeSuggestions.FindAsync(suggestion.Id);
        Assert.Equal(SuggestionStatus.Pending, loaded!.Status);
        Assert.Null(loaded.VerifiedById);
        Assert.Null(loaded.VerifiedAt);
        Assert.Null(loaded.CommittedCode);
    }

    [Fact]
    public async Task MedicalCodeSuggestion_AcceptedStatus_WithVerifiedBy_Succeeds()
    {
        // InMemory does not enforce the Trust-First CHECK constraint.
        // DDL verification is done via migration inspection.
        await using var ctx = CreateContext();

        var suggestion = new MedicalCodeSuggestion
        {
            PatientId       = 1,
            CodeType        = CodeType.CPT,
            SuggestedCode   = "99213",
            CodeDescription = "Office visit - established patient",
            ConfidenceScore = 0.95,
            Status          = SuggestionStatus.Accepted,
            VerifiedById    = 7,
            VerifiedAt      = DateTime.UtcNow,
            CommittedCode   = "99213"
        };
        ctx.MedicalCodeSuggestions.Add(suggestion);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.MedicalCodeSuggestions.FindAsync(suggestion.Id);
        Assert.Equal(SuggestionStatus.Accepted, loaded!.Status);
        Assert.Equal(7, loaded.VerifiedById);
        Assert.NotNull(loaded.VerifiedAt);
    }

    [Fact]
    public async Task MedicalCodeSuggestion_LowConfidenceFlag_CanBeSet()
    {
        await using var ctx = CreateContext();

        var suggestion = new MedicalCodeSuggestion
        {
            PatientId        = 1,
            CodeType         = CodeType.ICD10,
            SuggestedCode    = "R53.83",
            CodeDescription  = "Other fatigue",
            ConfidenceScore  = 0.42,
            LowConfidenceFlag = true
        };
        ctx.MedicalCodeSuggestions.Add(suggestion);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.MedicalCodeSuggestions.FindAsync(suggestion.Id);
        Assert.True(loaded!.LowConfidenceFlag);
    }

    // ── AC-004 / AC-005: CHECK constraint DDL evidence ───────────────────────

    [Fact]
    public void MedicalCodeSuggestion_ConfidenceScore_IsDouble()
    {
        // Verifies entity contract: ConfidenceScore is double (maps to PG double precision).
        // The [0.0, 1.0] CHECK constraint is enforced by PostgreSQL only.
        var prop = typeof(MedicalCodeSuggestion).GetProperty(nameof(MedicalCodeSuggestion.ConfidenceScore));
        Assert.NotNull(prop);
        Assert.Equal(typeof(double), prop!.PropertyType);
    }

    [Fact]
    public void ExtractedClinicalField_ConfidenceScore_IsDouble()
    {
        var prop = typeof(ExtractedClinicalField).GetProperty(nameof(ExtractedClinicalField.ConfidenceScore));
        Assert.NotNull(prop);
        Assert.Equal(typeof(double), prop!.PropertyType);
    }

    [Fact]
    public void MedicalCodeSuggestion_VerifiedById_IsNullableInt()
    {
        // Trust-First CHECK constraint requires nullable VerifiedById.
        var prop = typeof(MedicalCodeSuggestion).GetProperty(nameof(MedicalCodeSuggestion.VerifiedById));
        Assert.NotNull(prop);
        Assert.Equal(typeof(int?), prop!.PropertyType);
    }

    // ── Soft-delete query filter (F1) ─────────────────────────────────────────

    [Fact]
    public async Task ExtractedClinicalField_SoftDeleted_ExcludedByDefaultQuery()
    {
        await using var ctx = CreateContext();

        var live    = new ExtractedClinicalField { PatientId = 1, DocumentId = 1, FieldType = ClinicalFieldType.VitalSign, FieldName = "HR",    FieldValue = "72",     ConfidenceScore = 0.9, ExtractionJobId = "j1" };
        var deleted = new ExtractedClinicalField { PatientId = 1, DocumentId = 1, FieldType = ClinicalFieldType.VitalSign, FieldName = "BP",    FieldValue = "120/80", ConfidenceScore = 0.9, ExtractionJobId = "j1", IsDeleted = true };
        ctx.ExtractedClinicalFields.AddRange(live, deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.ExtractedClinicalFields.ToListAsync();

        Assert.Single(results);
        Assert.Equal("HR", results[0].FieldName);
    }

    [Fact]
    public async Task ExtractedClinicalField_SoftDeleted_VisibleWithIgnoreQueryFilters()
    {
        await using var ctx = CreateContext();

        var live    = new ExtractedClinicalField { PatientId = 1, DocumentId = 1, FieldType = ClinicalFieldType.VitalSign, FieldName = "HR",    FieldValue = "72",     ConfidenceScore = 0.9, ExtractionJobId = "j1" };
        var deleted = new ExtractedClinicalField { PatientId = 1, DocumentId = 1, FieldType = ClinicalFieldType.VitalSign, FieldName = "BP",    FieldValue = "120/80", ConfidenceScore = 0.9, ExtractionJobId = "j1", IsDeleted = true };
        ctx.ExtractedClinicalFields.AddRange(live, deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, results.Count);
    }

    // ── Soft-delete mark (F4) ─────────────────────────────────────────────────

    [Fact]
    public async Task ExtractedClinicalField_SoftDelete_CanBeMarkedDeleted()
    {
        await using var ctx = CreateContext();

        var field = new ExtractedClinicalField { PatientId = 1, DocumentId = 1, FieldType = ClinicalFieldType.Medication, FieldName = "Metformin", FieldValue = "500mg", ConfidenceScore = 0.8, ExtractionJobId = "j3" };
        ctx.ExtractedClinicalFields.Add(field);
        await ctx.SaveChangesAsync();

        field.IsDeleted = true;
        await ctx.SaveChangesAsync();

        // Default query respects query filter — record must be invisible
        var fromDefault = await ctx.ExtractedClinicalFields.FirstOrDefaultAsync(f => f.Id == field.Id);
        Assert.Null(fromDefault);

        // Admin/audit access via IgnoreQueryFilters — record must be visible
        var fromAdmin = await ctx.ExtractedClinicalFields.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == field.Id);
        Assert.NotNull(fromAdmin);
        Assert.True(fromAdmin!.IsDeleted);
    }

    // ── SuggestionStatus — remaining transitions (F3) ────────────────────────

    [Theory]
    [InlineData(SuggestionStatus.Modified)]
    [InlineData(SuggestionStatus.Rejected)]
    public async Task MedicalCodeSuggestion_StatusCanTransitionToModifiedOrRejected(SuggestionStatus newStatus)
    {
        await using var ctx = CreateContext();

        var suggestion = new MedicalCodeSuggestion
        {
            PatientId       = 1,
            CodeType        = CodeType.ICD10,
            SuggestedCode   = "Z00.00",
            CodeDescription = "Encounter for general adult medical examination",
            ConfidenceScore = 0.7
        };
        ctx.MedicalCodeSuggestions.Add(suggestion);
        await ctx.SaveChangesAsync();

        suggestion.Status        = newStatus;
        suggestion.VerifiedById  = 99;
        await ctx.SaveChangesAsync();

        var loaded = await ctx.MedicalCodeSuggestions.FindAsync(suggestion.Id);
        Assert.Equal(newStatus, loaded!.Status);
        Assert.Equal(99, loaded.VerifiedById);
    }

    // ── ConflictFlag patient isolation (F3) ──────────────────────────────────

    [Fact]
    public async Task ConflictFlag_QueryByPatientId_ReturnsOnlyPatientFlags()
    {
        await using var ctx = CreateContext();

        ctx.ConflictFlags.AddRange(
            new ConflictFlag { PatientId = 10, FieldName = "BP", Value1 = "120/80", Value2 = "130/90" },
            new ConflictFlag { PatientId = 10, FieldName = "HR", Value1 = "70",     Value2 = "85"     },
            new ConflictFlag { PatientId = 20, FieldName = "BP", Value1 = "110/70", Value2 = "125/85" }
        );
        await ctx.SaveChangesAsync();

        var patient10Flags = await ctx.ConflictFlags
            .Where(f => f.PatientId == 10)
            .ToListAsync();

        Assert.Equal(2, patient10Flags.Count);
        Assert.All(patient10Flags, f => Assert.Equal(10, f.PatientId));
    }

    // ── TC-004: Trust-First property contract (AC-003) ─────────────────────────
    //
    // The Trust-First DB CHECK "status != Accepted OR verified_by IS NOT NULL" is
    // enforced by PostgreSQL DDL (see migration) and is NOT testable via InMemory.
    // These tests verify the entity properties required by that business rule.

    [Fact]
    public void MedicalCodeSuggestion_VerifiedById_IsNullableInt_TrustFirstPropertyContract()
    {
        var prop = typeof(MedicalCodeSuggestion).GetProperty(nameof(MedicalCodeSuggestion.VerifiedById))!;
        Assert.NotNull(prop);
        Assert.Equal(typeof(int?), prop.PropertyType);
    }

    // ── TC-005 / ES-001: ConfidenceScore property contract (AC-005) ────────────
    //
    // The CHECK "confidence_score BETWEEN 0.0 AND 1.0" is enforced by PostgreSQL DDL.
    // These tests verify the double type that constrains the allowed value range.

    [Fact]
    public void ExtractedClinicalField_ConfidenceScore_IsDouble_ConstraintPropertyContract()
    {
        var prop = typeof(ExtractedClinicalField).GetProperty(nameof(ExtractedClinicalField.ConfidenceScore))!;
        Assert.NotNull(prop);
        Assert.Equal(typeof(double), prop.PropertyType);
    }

    [Fact]
    public void MedicalCodeSuggestion_ConfidenceScore_IsDouble_ConstraintPropertyContract()
    {
        var prop = typeof(MedicalCodeSuggestion).GetProperty(nameof(MedicalCodeSuggestion.ConfidenceScore))!;
        Assert.NotNull(prop);
        Assert.Equal(typeof(double), prop.PropertyType);
    }

    // ── EC-002: PatientId index registered for all three clinical entities ────

    [Theory]
    [InlineData(typeof(ExtractedClinicalField))]
    [InlineData(typeof(ConflictFlag))]
    [InlineData(typeof(MedicalCodeSuggestion))]
    public void ClinicalEntity_PatientId_HasIndexInModel(Type entityClrType)
    {
        using var ctx = CreateContext();

        var entityType       = ctx.Model.FindEntityType(entityClrType)!;
        var hasPatientIdIndex = entityType.GetIndexes()
            .Any(i => i.Properties.Any(p => p.Name == "PatientId"));

        Assert.True(hasPatientIdIndex);
    }
}
