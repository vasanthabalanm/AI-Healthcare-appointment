# ClinicalHealthcare — Deployment Runbook

## 1. Angular SPA — Netlify

### Prerequisites

- Node 20 LTS, npm 10+
- Netlify CLI or GitHub Actions with `NETLIFY_AUTH_TOKEN` + `NETLIFY_SITE_ID` secrets

### Build & Deploy

```bash
cd clinical-hub
npm ci
npx ng build --configuration production
# Output: clinical-hub/dist/clinical-hub/browser/
```

`netlify.toml` at the repo root of `clinical-hub/` handles the SPA fallback:

```toml
[[redirects]]
  from   = "/*"
  to     = "/index.html"
  status = 200
```

All Angular deep links work because Netlify serves `index.html` for every unmatched path.

---

## 2. ASP.NET Core API — IIS (Windows)

### Prerequisites

- Windows Server 2019/2022 with IIS 10+
- [.NET 8 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) installed (installs ASP.NET Core Module v2)
- Application pool: **No Managed Code** (in-process module manages the runtime)

### Publish

```bash
dotnet publish src/ClinicalHealthcare.Api \
  --configuration Release \
  --output C:\inetpub\clinicalhealthcare\api
```

Verify `web.config` is present in the publish output:

```bash
Test-Path "C:\inetpub\clinicalhealthcare\api\web.config"  # must return True
```

### IIS Site Setup

1. Open **IIS Manager** → **Sites** → **Add Website**
   - Site name: `ClinicalHealthcare`
   - Physical path: `C:\inetpub\clinicalhealthcare\api`
   - Binding: HTTPS, port 443 (see TLS section below)

2. **Application Pool** → **No Managed Code**

3. Set environment variables (do NOT hardcode in `web.config` — AC-005):
   - IIS Manager → Application Pools → `[pool name]` → Advanced Settings → **Environment Variables**
   - Required variables:

     | Variable | Description |
     |----------|-------------|
     | `ASPNETCORE_ENVIRONMENT` | `Production` |
     | `SQLSERVER_CONNECTION_STRING` | Full SQL Server connection string |
     | `POSTGRES_CONNECTION_STRING` | Full PostgreSQL connection string |
     | `REDIS_CONNECTION_STRING` | Upstash/Redis connection string |
     | `SEQ_SERVER_URL` | Seq CE ingestion URL, e.g. `http://seq-host:5341` |
     | `SEQ_API_KEY` | Seq API key (optional; leave unset for anonymous ingestion on private networks) |

### TLS Binding (AC-004)

1. Obtain a certificate (Let's Encrypt via `win-acme`, or a CA-issued PFX).
2. Import to **Local Machine → Personal** certificate store.
3. In IIS Manager → Site Bindings → **Add**:
   - Type: `https`
   - Port: `443`
   - SSL Certificate: select imported cert
4. **Enforce HTTPS** — add an HTTP binding (port 80) and a URL Rewrite rule for 301 redirect:

```xml
<!-- Insert inside <system.webServer> in web.config only on the HTTP site -->
<rewrite>
  <rules>
    <rule name="HTTP to HTTPS" stopProcessing="true">
      <match url="(.*)" />
      <conditions>
        <add input="{HTTPS}" pattern="^OFF$" />
      </conditions>
      <action type="Redirect"
              url="https://{HTTP_HOST}/{R:1}"
              redirectType="Permanent" />
    </rule>
  </rules>
</rewrite>
```

> `UseHttpsRedirection()` in `Program.cs` handles application-level redirect; the IIS rule handles the port-80 listener before ASP.NET Core processes the request.

---

## 3. Windows Service Hosting

`Program.cs` calls `builder.Host.UseWindowsService()` which is a no-op when not running as a service — safe for development and IIS deployments.

### Install as a Windows Service

```powershell
# Publish to a permanent path
dotnet publish src/ClinicalHealthcare.Api `
  --configuration Release `
  --output "C:\Services\ClinicalHealthcare"

# Create the service
sc.exe create "ClinicalHealthcareApi" `
  binPath= "C:\Services\ClinicalHealthcare\ClinicalHealthcare.Api.exe" `
  start= auto `
  obj= "NT AUTHORITY\NetworkService"

# Set environment variables for the service account
[System.Environment]::SetEnvironmentVariable(
  "SQLSERVER_CONNECTION_STRING", "<value>",
  [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable(
  "POSTGRES_CONNECTION_STRING", "<value>",
  [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable(
  "REDIS_CONNECTION_STRING", "<value>",
  [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable(
  "ASPNETCORE_ENVIRONMENT", "Production",
  [System.EnvironmentVariableTarget]::Machine)

# Start the service
sc.exe start "ClinicalHealthcareApi"
```

### Uninstall

```powershell
sc.exe stop "ClinicalHealthcareApi"
sc.exe delete "ClinicalHealthcareApi"
```

---

## 4. Secrets Policy (AC-005)

- All connection strings and API keys are read **exclusively from environment variables** at startup.
- No secrets appear in `appsettings.json`, `appsettings.Production.json`, or `web.config`.
- `appsettings.*.local.json` overrides are covered by `.gitignore` and must never be committed.
- Verify before any commit:

```bash
# Must return 0 results
grep -rn "Password=" src/ clinical-hub/src/ --include="*.json" --include="*.config"
```

---

## 5. Health Check Verification

After deployment, verify the API is running:

```bash
curl https://api.clinicalhub.app/health
# Expected: {"status":"Healthy"} or {"status":"Degraded"} (Redis unreachable)
```

A `Degraded` response means the API is functional but Redis is offline. Investigate Redis connectivity but do not treat it as a deployment failure.

---

## 6. TLS & Cipher Suite Hardening (AC-002 / AC-005)

### Kestrel (in-process, code-enforced)

`Program.cs` configures Kestrel to accept only TLS 1.2 and TLS 1.3:

```csharp
serverOptions.ConfigureHttpsDefaults(httpsOptions =>
{
    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
});
```

TLS 1.0 and TLS 1.1 are **not** in the bitmask — Kestrel will refuse handshakes for those versions.

> **Why code, not `appsettings.json`?** TLS protocol selection is security-critical. Configuring it in `Program.cs` — not a JSON file — means it cannot be accidentally overridden by a config transform or an environment-specific settings file without a code review. Any change to the allowed protocol bitmask is a visible, reviewable code change.

> **HSTS preload caution:** `Program.cs` sets `Preload = false`. Do **not** change this to `true` without first submitting the domain to [hstspreload.org](https://hstspreload.org). Once a domain is in browser preload lists it is inaccessible via HTTP across all browsers even after TLS removal, and removal from preload lists can take months to propagate.

### IIS (Windows Server — use IIS Crypto)

When the API runs behind IIS (reverse proxy or in-process), the Windows Schannel layer handles TLS negotiation. Apply the **Best Practices** preset using [IIS Crypto 3.3+](https://www.nartac.com/Products/IISCrypto):

1. Download and run **IIS Crypto** (administrator).
2. Click **Best Practices**, then **Apply**.
3. Reboot the server when prompted.

**What the Best Practices preset enforces:**

| Protocol / Cipher | Action |
|-------------------|--------|
| TLS 1.3 | Enabled |
| TLS 1.2 | Enabled |
| TLS 1.1 | **Disabled** |
| TLS 1.0 | **Disabled** |
| SSL 3.0 | **Disabled** |
| SSL 2.0 | **Disabled** |
| RC4 (all key lengths) | **Disabled** |
| 3DES (168-bit) | **Disabled** |
| DES (56-bit) | **Disabled** |
| NULL ciphers | **Disabled** |
| Export ciphers (40/56-bit) | **Disabled** |
| AES-128-CBC, AES-256-CBC | Enabled |
| AES-128-GCM, AES-256-GCM | Enabled |
| ECDHE key exchange | Enabled (preferred) |

### Manual Registry Alternative

If IIS Crypto is unavailable, disable TLS 1.0 and 1.1 via registry (requires Administrator and reboot):

```powershell
# Disable TLS 1.0
$tls10 = "HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.0\Server"
New-Item -Path $tls10 -Force | Out-Null
Set-ItemProperty -Path $tls10 -Name "Enabled"            -Value 0 -Type DWord
Set-ItemProperty -Path $tls10 -Name "DisabledByDefault"  -Value 1 -Type DWord

# Disable TLS 1.1
$tls11 = "HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.1\Server"
New-Item -Path $tls11 -Force | Out-Null
Set-ItemProperty -Path $tls11 -Name "Enabled"            -Value 0 -Type DWord
Set-ItemProperty -Path $tls11 -Name "DisabledByDefault"  -Value 1 -Type DWord

Restart-Computer
```

### Post-Hardening Verification

```powershell
# Verify TLS 1.2 is accepted (expect: TLS 1.2 shown in output)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri "https://api.clinicalhub.app/health" -UseBasicParsing

# Verify TLS 1.0 is refused (expect: connection error)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls
Invoke-WebRequest -Uri "https://api.clinicalhub.app/health" -UseBasicParsing
```

Use [SSL Labs Server Test](https://www.ssllabs.com/ssltest/) to obtain an independent A/A+ rating confirmation after deployment.
