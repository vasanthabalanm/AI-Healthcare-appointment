using ClinicalHealthcare.Infrastructure.Sms;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Sms;

/// <summary>
/// Unit tests for <see cref="PhoneNormalizer.ToE164"/>.
/// Verifies AC-002 (E.164 normalization) and AC-003 (null/invalid → null).
/// </summary>
public sealed class PhoneNormalizerTests
{
    // ── Happy path: US numbers in various formats ─────────────────────────────

    [Fact]
    public void ToE164_FormattedUsNumber_ReturnsE164()
    {
        var result = PhoneNormalizer.ToE164("(415) 555-2671");
        Assert.Equal("+14155552671", result);
    }

    [Fact]
    public void ToE164_AlreadyE164_ReturnsSame()
    {
        var result = PhoneNormalizer.ToE164("+14155552671");
        Assert.Equal("+14155552671", result);
    }

    [Fact]
    public void ToE164_UsNumberWithDashes_ReturnsE164()
    {
        var result = PhoneNormalizer.ToE164("415-555-2671");
        Assert.Equal("+14155552671", result);
    }

    [Fact]
    public void ToE164_InternationalE164_ReturnsNormalized()
    {
        // UK number with explicit country code — no region hint needed.
        var result = PhoneNormalizer.ToE164("+447911123456");
        Assert.Equal("+447911123456", result);
    }

    // ── Edge cases: null / empty / invalid → null (AC-003) ───────────────────

    [Fact]
    public void ToE164_Null_ReturnsNull()
    {
        Assert.Null(PhoneNormalizer.ToE164(null));
    }

    [Fact]
    public void ToE164_EmptyString_ReturnsNull()
    {
        Assert.Null(PhoneNormalizer.ToE164(string.Empty));
    }

    [Fact]
    public void ToE164_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(PhoneNormalizer.ToE164("   "));
    }

    [Fact]
    public void ToE164_InvalidText_ReturnsNull()
    {
        Assert.Null(PhoneNormalizer.ToE164("not-a-number"));
    }

    [Fact]
    public void ToE164_TooShort_ReturnsNull()
    {
        Assert.Null(PhoneNormalizer.ToE164("1234"));
    }
}
