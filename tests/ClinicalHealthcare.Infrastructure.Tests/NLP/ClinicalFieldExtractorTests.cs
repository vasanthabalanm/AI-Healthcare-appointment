using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.NLP;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.NLP;

public sealed class ClinicalFieldExtractorTests
{
    private static ClinicalFieldExtractor CreateSut() =>
        new(NullLogger<ClinicalFieldExtractor>.Instance);

    // ── VitalSign ──────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_VitalSign_BloodPressure_ReturnsVitalSignField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("BP: 120/80 mmHg");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.VitalSign, result[0].FieldType);
        Assert.Contains("VitalSign", result[0].FieldName);
        Assert.Contains("120/80", result[0].FieldValue);
    }

    [Fact]
    public void Extract_VitalSign_HeartRate_ReturnsVitalSignField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("HR: 72 bpm");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.VitalSign, result[0].FieldType);
    }

    [Fact]
    public void Extract_VitalSign_SpO2_ReturnsVitalSignField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("SpO2: 98%");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.VitalSign, result[0].FieldType);
    }

    // ── Medication ─────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_Medication_ReturnsMedicationField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("Metformin 500mg twice daily");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.Medication, result[0].FieldType);
        Assert.Contains("Metformin", result[0].FieldValue);
    }

    // ── Allergy ────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_Allergy_ReturnsAllergyField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("Allergy to penicillin");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.Allergy, result[0].FieldType);
        Assert.Contains("penicillin", result[0].FieldValue);
    }

    [Fact]
    public void Extract_Allergy_NKDA_ReturnsAllergyField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("NKDA");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.Allergy, result[0].FieldType);
    }

    // ── Diagnosis ──────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_Diagnosis_ReturnsDiagnosisField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("Diagnosis: Type 2 Diabetes Mellitus");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.Diagnosis, result[0].FieldType);
        Assert.Contains("Type 2 Diabetes", result[0].FieldValue);
    }

    [Fact]
    public void Extract_Diagnosis_Assessment_ReturnsDiagnosisField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("Assessment: Hypertension, controlled");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.Diagnosis, result[0].FieldType);
    }

    // ── MedicalHistory ─────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MedicalHistory_ICD10_ReturnsMedicalHistoryField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("E11.9 - Type 2 diabetes mellitus without complications");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.MedicalHistory, result[0].FieldType);
        Assert.Contains("E11.9", result[0].FieldValue);
    }

    [Fact]
    public void Extract_MedicalHistory_HistoryOf_ReturnsField()
    {
        var sut    = CreateSut();
        var result = sut.Extract("History of myocardial infarction");

        Assert.Single(result);
        Assert.Equal(ClinicalFieldType.MedicalHistory, result[0].FieldType);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_EmptyInput_ReturnsEmpty()
    {
        var sut = CreateSut();

        Assert.Empty(sut.Extract(string.Empty));
        Assert.Empty(sut.Extract("   "));
        Assert.Empty(sut.Extract(null!));
    }

    [Fact]
    public void Extract_UnrecognisedText_ReturnsEmpty()
    {
        var sut    = CreateSut();
        var result = sut.Extract("This line has no clinical signal at all.");

        // Unrecognised lines must not produce Unknown-type rows (AC-003).
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_MultiLine_ReturnsOneFieldPerMatchedLine()
    {
        const string text =
            "BP: 130/85\nMetformin 500mg daily\nThis is noise.\nDiagnosis: Hypertension";

        var sut    = CreateSut();
        var result = sut.Extract(text);

        Assert.Equal(3, result.Count);
    }
}
