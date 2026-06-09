using Xunit;

namespace ClinicalHealthcare.Api.Tests.Structure;

/// <summary>
/// Static inspection: confirms vertical-slice stub directories and placeholder files
/// exist in the API project (AC-003 — US_002).
/// </summary>
public sealed class FeatureDirectoryTests
{
    private static string FeaturesRoot { get; } = ResolveFeaturesRoot();

    private static string ResolveFeaturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !dir.GetFiles("*.slnx", SearchOption.TopDirectoryOnly).Any()
               && !dir.GetFiles("*.sln",  SearchOption.TopDirectoryOnly).Any())
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "src", "ClinicalHealthcare.Api", "Features");
    }

    // TC-004: Required vertical-slice directories are present
    [Theory]
    [InlineData("Auth")]
    [InlineData("Appointments")]
    [InlineData("Intake")]
    [InlineData("Staff")]
    [InlineData("ClinicalDocs")]
    [InlineData("Coding")]
    [InlineData("Admin")]
    [InlineData("Patients")]
    public void FeaturesDirectory_RequiredStubDirectoryExists(string stubName)
    {
        var path = Path.Combine(FeaturesRoot, stubName);
        Assert.True(
            Directory.Exists(path),
            $"Vertical-slice directory '{stubName}' not found at: {path}");
    }

    // ES-001: Each stub directory contains at least one file (git-trackable placeholder)
    [Theory]
    [InlineData("Auth")]
    [InlineData("Appointments")]
    [InlineData("Intake")]
    [InlineData("Staff")]
    [InlineData("ClinicalDocs")]
    [InlineData("Coding")]
    [InlineData("Admin")]
    [InlineData("Patients")]
    public void FeaturesDirectory_StubContainsAtLeastOneFile(string stubName)
    {
        var path  = Path.Combine(FeaturesRoot, stubName);
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        Assert.True(
            files.Length > 0,
            $"Stub directory '{stubName}' has no files. A placeholder is required for git tracking.");
    }
}
