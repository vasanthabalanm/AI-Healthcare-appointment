using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Deployment;

/// <summary>
/// Static file inspection tests: confirms deployment artefacts meet infrastructure
/// acceptance criteria (US_005).
/// No test doubles or live processes are needed — the source tree is read directly.
/// </summary>
public sealed class DeploymentConfigTests
{
    private static readonly string SolutionRoot = ResolveSolutionRoot();

    private static string ResolveSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
               && !dir.GetFiles("*.slnx", SearchOption.TopDirectoryOnly).Any()
               && !dir.GetFiles("*.sln",  SearchOption.TopDirectoryOnly).Any())
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }

    // ── TC-001: netlify.toml contains the SPA catch-all redirect ─────────────

    [Fact]
    public void NetlifyToml_ContainsSpaRedirectRule()
    {
        var path    = Path.Combine(SolutionRoot, "clinical-hub", "netlify.toml");
        var content = File.ReadAllText(path);

        Assert.Contains("[[redirects]]",  content, StringComparison.Ordinal);
        Assert.Contains("/*",             content, StringComparison.Ordinal);
        Assert.Contains("/index.html",    content, StringComparison.Ordinal);
        Assert.Contains("200",            content, StringComparison.Ordinal);
    }

    // ── TC-002: web.config configures in-process hosting via dotnet ──────────

    [Fact]
    public void WebConfig_ConfiguresInProcessHosting()
    {
        var path = Path.Combine(SolutionRoot, "src", "ClinicalHealthcare.Api", "web.config");
        var xml  = XDocument.Load(path);

        var aspNetCore = xml.Descendants("aspNetCore").FirstOrDefault();
        Assert.NotNull(aspNetCore);
        Assert.Equal("inprocess", aspNetCore!.Attribute("hostingModel")?.Value);
        Assert.Equal("dotnet",    aspNetCore.Attribute("processPath")?.Value);
    }

    // ── TC-003: Program.cs registers UseWindowsService() ────────────────────

    [Fact]
    public void ProgramCs_ContainsUseWindowsService()
    {
        var path    = Path.Combine(SolutionRoot, "src", "ClinicalHealthcare.Api", "Program.cs");
        var content = File.ReadAllText(path);

        Assert.Contains("UseWindowsService()", content, StringComparison.Ordinal);
    }

    // ── TC-004: appsettings.json contains no hard-coded secrets ──────────────

    private static readonly Regex SecretPattern = new(
        @"(?i)(password|secret|apikey|token|privatekey)\s*"":\s*""[^${\s][^""]{3,}""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void AppSettingsJson_ContainsNoHardCodedSecrets()
    {
        var path    = Path.Combine(SolutionRoot, "src", "ClinicalHealthcare.Api", "appsettings.json");
        var content = File.ReadAllText(path);

        Assert.False(
            SecretPattern.IsMatch(content),
            "appsettings.json appears to contain a hard-coded secret.");
    }

    // ── TC-005: Program.cs enforces HTTPS redirection ────────────────────────

    [Fact]
    public void ProgramCs_ContainsUseHttpsRedirection()
    {
        var path    = Path.Combine(SolutionRoot, "src", "ClinicalHealthcare.Api", "Program.cs");
        var content = File.ReadAllText(path);

        Assert.Contains("UseHttpsRedirection()", content, StringComparison.Ordinal);
    }

    // ── EC-001: appsettings.Production.json (if present) contains no secrets ─

    [Fact]
    public void AppSettingsProductionJson_ContainsNoHardCodedSecrets()
    {
        var path = Path.Combine(
            SolutionRoot, "src", "ClinicalHealthcare.Api", "appsettings.Production.json");

        if (!File.Exists(path))
            return; // file is optional

        var content = File.ReadAllText(path);
        Assert.False(
            SecretPattern.IsMatch(content),
            "appsettings.Production.json appears to contain a hard-coded secret.");
    }

    // ── ES-001: .gitignore includes a pattern that excludes .env files ────────

    [Fact]
    public void GitIgnore_ContainsDotEnvPattern()
    {
        var path    = Path.Combine(SolutionRoot, ".gitignore");
        var content = File.ReadAllText(path);

        Assert.Contains(".env", content, StringComparison.Ordinal);
    }

    // ── ES-002: web.config does not hard-code ASPNETCORE_ENVIRONMENT ─────────

    [Fact]
    public void WebConfig_DoesNotHardCodeAspNetCoreEnvironment()
    {
        var path = Path.Combine(SolutionRoot, "src", "ClinicalHealthcare.Api", "web.config");
        var xml  = XDocument.Load(path);

        var hasEnvVar = xml.Descendants("environmentVariable")
            .Any(e => string.Equals(
                e.Attribute("name")?.Value,
                "ASPNETCORE_ENVIRONMENT",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(hasEnvVar,
            "web.config must not hard-code ASPNETCORE_ENVIRONMENT — it is set at deploy time.");
    }
}
