<#
.SYNOPSIS
    Audits NuGet package licences for the ClinicalHealthcare solution.
    Exits with code 1 if any package carries a licence not in the allowed list.

.DESCRIPTION
    Uses the `dotnet-project-licenses` global tool (nuget-license).
    Allowed licences: MIT, Apache-2.0, LGPL-2.0, LGPL-2.1, LGPL-3.0, GPL-2.0, GPL-3.0,
                      BSD-2-Clause, BSD-3-Clause, ISC, MS-PL, Unlicense.

    Non-permissive or unknown licences cause a non-zero exit — CI will fail.

.EXAMPLE
    .\scripts\audit-oss-licences.ps1
    .\scripts\audit-oss-licences.ps1 -SolutionPath "src/ClinicalHealthcare.sln"
#>

[CmdletBinding()]
param (
    [string]$SolutionPath = "ClinicalHealthcare.slnx"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Allowed licence identifiers (SPDX) ─────────────────────────────────────
$AllowedLicences = @(
    "MIT",
    "Apache-2.0",
    "LGPL-2.0",
    "LGPL-2.0-only",
    "LGPL-2.1",
    "LGPL-2.1-only",
    "LGPL-3.0",
    "LGPL-3.0-only",
    "GPL-2.0",
    "GPL-2.0-only",
    "GPL-3.0",
    "GPL-3.0-only",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "ISC",
    "MS-PL",
    "Unlicense"
)

# ── Ensure nuget-license tool is available ──────────────────────────────────
$toolCheck = dotnet tool list --global 2>&1 | Select-String "nuget-license"
if (-not $toolCheck) {
    Write-Host "Installing nuget-license tool..."
    dotnet tool install --global nuget-license --ignore-failed-sources
}

# ── Run licence audit ───────────────────────────────────────────────────────
Write-Host "Running OSS licence audit on: $SolutionPath"

$outputFile = [System.IO.Path]::GetTempFileName() + ".json"

dotnet-project-licenses `
    --input $SolutionPath `
    --output-directory ([System.IO.Path]::GetTempPath()) `
    --output-file-name ([System.IO.Path]::GetFileName($outputFile)) `
    --json `
    --unique 2>&1 | Out-Null

if (-not (Test-Path $outputFile)) {
    # Fall back: run without --output and parse stdout
    $rawOutput = dotnet-project-licenses --input $SolutionPath --json --unique 2>&1
    $rawOutput | Out-File -FilePath $outputFile -Encoding utf8
}

# ── Parse results ────────────────────────────────────────────────────────────
$packages = Get-Content $outputFile -Raw | ConvertFrom-Json

$violations = @()

foreach ($pkg in $packages) {
    $licence = $pkg.License ?? $pkg.LicenseType ?? "UNKNOWN"

    # Normalise common variants (e.g. "Apache 2.0" → "Apache-2.0")
    $normLicence = $licence -replace " ", "-" -replace "v(\d)", '$1'

    $isAllowed = $AllowedLicences | Where-Object {
        $normLicence -like "*$_*" -or $_ -like "*$normLicence*"
    }

    if (-not $isAllowed) {
        $violations += [PSCustomObject]@{
            Package = $pkg.PackageName ?? $pkg.Id
            Version = $pkg.PackageVersion ?? $pkg.Version
            Licence = $licence
        }
    }
}

# ── Report ───────────────────────────────────────────────────────────────────
if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "LICENCE AUDIT FAILED — $($violations.Count) violation(s) found:" -ForegroundColor Red
    $violations | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Add a licence exception or replace the package." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "LICENCE AUDIT PASSED — all $($packages.Count) package(s) use approved licences." -ForegroundColor Green
exit 0
