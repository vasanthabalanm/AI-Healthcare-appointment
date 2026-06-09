using ClinicalHealthcare.Infrastructure.Logging;
using Moq;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Logging;

/// <summary>
/// Unit tests for <see cref="PhiRedactingDestructuringPolicy"/> PHI-redaction logic (US_006).
/// </summary>
public sealed class PhiRedactingDestructuringPolicyTests
{
    // ── Test DTOs ─────────────────────────────────────────────────────────────

    private sealed class PatientDto
    {
        public int    UserId      { get; set; }
        public string Email       { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string FirstName   { get; set; } = "";
        public string LastName    { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        public string Address     { get; set; } = "";
        public string SSN         { get; set; } = "";
    }

    private sealed class NonPhiDto
    {
        public int    UserId      { get; set; }
        public string RequestPath { get; set; } = "";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ILogEventPropertyValueFactory BuildFactory()
    {
        // The factory is used only for non-PHI properties. For PHI, ScalarValue is used directly.
        var factory = new Mock<ILogEventPropertyValueFactory>();
        factory.Setup(f => f.CreatePropertyValue(It.IsAny<object?>(), It.IsAny<bool>()))
               .Returns<object?, bool>((v, _) => new ScalarValue(v));
        return factory.Object;
    }

    private static IDictionary<string, LogEventPropertyValue> Destructure(object dto)
    {
        var policy  = new PhiRedactingDestructuringPolicy();
        var factory = BuildFactory();

        var ok = policy.TryDestructure(dto, factory, out var result);
        Assert.True(ok, "TryDestructure should return true for a DTO with PHI properties.");

        var structure = Assert.IsType<StructureValue>(result);
        return structure.Properties.ToDictionary(p => p.Name, p => p.Value);
    }

    // ── TC-004: PHI property (Email) is redacted ──────────────────────────────

    [Fact]
    public void TryDestructure_PhiEmailProperty_IsRedacted()
    {
        var dto   = new PatientDto { UserId = 7, Email = "alice@example.com" };
        var props = Destructure(dto);

        var emailValue = Assert.IsType<ScalarValue>(props["Email"]);
        Assert.Equal("[REDACTED]", emailValue.Value);
    }

    // ── TC-005: All seven PHI field names are redacted ────────────────────────

    [Theory]
    [InlineData("Email")]
    [InlineData("PhoneNumber")]
    [InlineData("FirstName")]
    [InlineData("LastName")]
    [InlineData("DateOfBirth")]
    [InlineData("Address")]
    [InlineData("SSN")]
    public void TryDestructure_AllPhiFieldNames_AreRedacted(string fieldName)
    {
        var dto   = new PatientDto { Email = "x@x.com", PhoneNumber = "555", FirstName = "Alice",
                                     LastName = "Smith",  DateOfBirth = "1990-01-01",
                                     Address  = "123 St", SSN = "000-00-0000" };
        var props = Destructure(dto);

        var scalar = Assert.IsType<ScalarValue>(props[fieldName]);
        Assert.Equal("[REDACTED]", scalar.Value);
    }

    // ── TC-006: Non-PHI fields are preserved unchanged ────────────────────────

    [Fact]
    public void TryDestructure_NonPhiFields_ArePreserved()
    {
        var dto   = new PatientDto { UserId = 42, Email = "test@example.com" };
        var props = Destructure(dto);

        var userIdValue = Assert.IsType<ScalarValue>(props["UserId"]);
        Assert.Equal(42, userIdValue.Value);
    }

    // ── EC-001: null PHI property value is still redacted ────────────────────

    [Fact]
    public void TryDestructure_NullPhiPropertyValue_IsRedacted()
    {
        // Email = null (using nullable override for this test)
        var dto = new PatientDto { Email = null! };
        var props = Destructure(dto);

        var emailValue = Assert.IsType<ScalarValue>(props["Email"]);
        Assert.Equal("[REDACTED]", emailValue.Value);
    }

    // ── Boundary: raw string value → policy returns false (no interception) ──

    [Fact]
    public void TryDestructure_RawString_ReturnsFalse()
    {
        var policy  = new PhiRedactingDestructuringPolicy();
        var factory = BuildFactory();

        var ok = policy.TryDestructure("alice@example.com", factory, out _);

        Assert.False(ok, "Policy must not intercept raw string values.");
    }

    // ── Boundary: object without PHI properties → policy returns false ────────

    [Fact]
    public void TryDestructure_ObjectWithoutPhiProperties_ReturnsFalse()
    {
        var policy  = new PhiRedactingDestructuringPolicy();
        var factory = BuildFactory();
        var dto     = new NonPhiDto { UserId = 1, RequestPath = "/api/health" };

        var ok = policy.TryDestructure(dto, factory, out _);

        Assert.False(ok, "Policy must not intercept objects that have no PHI-named properties.");
    }
}
