using System.Net.Mime;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Api.Authorization;
using ClinicalHealthcare.Api.Data;
using ClinicalHealthcare.Api.Infrastructure;
using ClinicalHealthcare.Api.Middleware;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Interceptors;
using ClinicalHealthcare.Infrastructure.Sms;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Logging;
using ClinicalHealthcare.Infrastructure.Services;
using ClinicalHealthcare.Infrastructure.NLP;
using ClinicalHealthcare.Infrastructure.OCR;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using DotNetEnv;

// ── Load .env file before anything else reads env vars ──────────────────────
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel TLS — enforce TLS 1.2 minimum; disallow TLS 1.0 / 1.1 (AC-002) ──
// Bitwise OR combines Tls12 + Tls13; Tls10 / Tls11 are intentionally absent.
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
});

// ── Windows Service hosting support (AC-003) ─────────────────────────────
// No-op on non-Windows and non-service environments; safe to leave unconditional.
builder.Host.UseWindowsService();

// ── Serilog — structured logging with file + Seq CE sinks (AC-001/AC-004) ──
var seqServerUrl = Environment.GetEnvironmentVariable("SEQ_SERVER_URL");

if (string.IsNullOrWhiteSpace(seqServerUrl))
{
    // Non-fatal: app runs without Seq, but operators must investigate.
    Console.WriteLine("[WARNING] SEQ_SERVER_URL is not set. Structured logs will route to localhost:5341 only.");
}

builder.Host.UseSerilog((ctx, services, config) =>
{
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<PhiRedactingEnricher>()
        .Destructure.With<PhiRedactingDestructuringPolicy>()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/clinical-hub-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
        .WriteTo.Seq(
            serverUrl: seqServerUrl ?? "http://localhost:5341",
            apiKey: Environment.GetEnvironmentVariable("SEQ_API_KEY"));
});

// ── Fail-fast: require PostgreSQL connection string at startup ──────────────
static string RequireConnectionString(string envVar) =>
    Environment.GetEnvironmentVariable(envVar)
    ?? throw new InvalidOperationException(
        $"Required environment variable '{envVar}' is not set. " +
        $"Set it before starting the application.");

var postgresConnectionString = RequireConnectionString("POSTGRES_CONNECTION_STRING");

// Redis is optional — blank/missing REDIS_CONNECTION_STRING falls back to NullCacheService.
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

// ── ApplicationDbContext — PostgreSQL (operational data) ──────────────────
// Interceptors are stateless singletons — safe and avoids per-request allocations.
builder.Services.AddSingleton<AppointmentFsmInterceptor>();
builder.Services.AddSingleton<WaitlistGuardInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options
        .UseNpgsql(postgresConnectionString,
            npgsql => npgsql
                .MigrationsAssembly("ClinicalHealthcare.Infrastructure.PgMigrations")
                .MigrationsHistoryTable("__EFMigrationsHistory_App"))
        .AddInterceptors(
            sp.GetRequiredService<AppointmentFsmInterceptor>(),
            sp.GetRequiredService<WaitlistGuardInterceptor>()));

// ── ClinicalDbContext — PostgreSQL (AI/clinical data) ─────────────────────
builder.Services.AddDbContext<ClinicalDbContext>(options =>
    options.UseNpgsql(postgresConnectionString,
        npgsql => npgsql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.PgMigrations")));

// ── Redis + Cache — optional; falls back to NullCacheService if not configured ──
builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection(CacheSettings.SectionName));
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        _ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<ICacheService, CacheService>();
}
else
{
    // No Redis — fall back to an in-process IMemoryCache so AI intake sessions
    // survive across requests within the same process lifetime.
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
}

// ── Email service ────────────────────────────────────────────────────────────
// MailKitEmailService requires env vars: SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM_ADDRESS.
// When SMTP_HOST is absent, DevEmailService is used: emails are NOT sent but the
// action URL is printed to the API console so developers can copy it from the terminal.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMTP_HOST")))
{
    builder.Services.AddSingleton<IEmailService, MailKitEmailService>();
}
else
{
    builder.Services.AddSingleton<IEmailService, DevEmailService>();
    Console.WriteLine("[Email] SMTP_HOST not set — DevEmailService active. " +
                      "Action URLs will be logged to the console instead of being emailed.");
}
// ── SMS gateway (TASK_027) ──────────────────────────────────────────
builder.Services.AddHttpClient("TwilioSms");
builder.Services.AddScoped<ISmsGateway, TwilioSandboxSmsGateway>();
// ── AI intake — Groq (OpenAI-compatible) ────────────────────────────
builder.Services.AddHttpClient("Groq");
builder.Services.AddScoped<IRasaIntakeService, GroqIntakeService>();
// ── Application settings (SwapOfferWindowHours etc.) ─────────────────────────
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.SectionName));builder.Services.AddSingleton<IValidateOptions<AppSettings>, AppSettingsValidator>();// ── No-show risk scoring (TASK_022) ──────────────────────────────────
builder.Services.AddScoped<INoShowRiskScoreService, NoShowRiskScoreService>();
// ── Insurance pre-check (TASK_032) ───────────────────────────────────
builder.Services.AddScoped<IInsurancePreCheckService, InsurancePreCheckService>();
// ── ClamAV scan + AES-256-CBC encryption (TASK_038) ─────────────────
// When CLAMAV_HOST is absent, DevClamAvScanService is used: scan is bypassed with a warning.
// In production, set CLAMAV_HOST to enable real ClamAV scanning.
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAMAV_HOST")))
{
    Console.WriteLine("[ClamAV] CLAMAV_HOST set — real ClamAV scan service active.");
    builder.Services.AddScoped<IClamAvScanService, ClamAvScanService>();
}
else
{
    Console.WriteLine("[ClamAV] CLAMAV_HOST not set — DevClamAvScanService active. Virus scan is BYPASSED.");
    builder.Services.AddScoped<IClamAvScanService, DevClamAvScanService>();
}
builder.Services.AddScoped<IAesEncryptionService, AesEncryptionService>();
// ── Tesseract OCR (TASK_040) ─────────────────────────────────────────
builder.Services.AddScoped<ITesseractOcrService, TesseractOcrService>();
// ── NLP field extraction (TASK_041) ──────────────────────────────────
builder.Services.AddSingleton<ClinicalFieldExtractor>(); // stateless — all Regex patterns are static readonly
// ── Conflict detection (TASK_043) ────────────────────────────────────
builder.Services.AddScoped<IConflictService, ConflictService>();
// ── ICD-10 generation via Ollama with keyword fallback (TASK_045) ────────────
builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout     = TimeSpan.FromSeconds(35); // 30s request + 5s buffer
});
// Registers concrete types so FallbackCodeGenerationService can inject them directly.
builder.Services.AddScoped<OllamaCodeGenerationService>();
builder.Services.AddScoped<RuleCodeGenerationService>();
// IOllamaCodeGenerationService → FallbackCodeGenerationService (tries Ollama, falls back to rules).
builder.Services.AddScoped<IOllamaCodeGenerationService, FallbackCodeGenerationService>();
// ── Hangfire — background jobs on PostgreSQL (AC-004) ────────────────────────
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(postgresConnectionString)));
builder.Services.AddHangfireServer();
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute
{
    Attempts = 3,
    DelaysInSeconds = [30, 60, 120]
});

// ── Health checks (AC-001 / AC-005) ────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── Swagger/OpenAPI — Development only (AC-002) ────────────────────────────
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "ClinicalHealthcare API", Version = "v1" });
    });
}

// ── JWT bearer authentication (AC-001 / TASK_015) ──────────────────────────────
// JWT_SECRET is required at startup; the JwtTokenService constructor enforces
// the same invariant so the application will refuse to start if it is absent.
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException(
        "Required environment variable 'JWT_SECRET' is not set. "
        + "Set it before starting the application.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Prevent ASP.NET's default inbound claim-type remapping (e.g. "role" → ClaimTypes.Role URI).
        // With mapping enabled, short JWT claim names are rewritten to long Microsoft URIs before
        // the ClaimsPrincipal is built, which means RoleClaimType = "role" would never match.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer          = false,
            ValidateAudience        = false,
            // ClockSkew = Zero enforces the exact 15-minute TTL without a 5-minute grace window.
            ClockSkew               = TimeSpan.Zero,
            // Use the short "role" claim name so Angular can read it directly from the JWT payload.
            RoleClaimType           = "role",
        };
    });

// ── RBAC named policies (AC-001) ─────────────────────────────────────────
// Central registration; per-slice AddServices guards check GetPolicy("...") is null
// so these definitions are always applied before the idempotent guards run.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",        p => p.RequireRole("admin"));
    options.AddPolicy("StaffOrAdmin",     p => p.RequireRole("staff", "admin"));
    options.AddPolicy("AnyAuthenticated", p => p.RequireAuthenticatedUser());
});

// ── Vertical-slice endpoint registration (AC-005) ──────────────────────────
builder.Services.AddEndpointDefinitions(typeof(Program).Assembly, builder.Configuration);

// ── DI startup validation (AC-003) ────────────────────────────────────────
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes  = true;
});

// ── HTTPS redirect — permanent 301 (AC-001) ─────────────────────────────────
// Default is 307 (temporary); explicitly set 301 for HIPAA TLS compliance.
builder.Services.AddHttpsRedirection(options =>
    options.RedirectStatusCode = StatusCodes.Status301MovedPermanently);

// ── HSTS — max-age 1 year, includeSubDomains (AC-003) ────────────────────────
// ExcludedHosts covers localhost, loopback, and IPv6 loopback so HSTS is
// never sent on local development traffic regardless of environment setting.
builder.Services.AddHsts(options =>
{
    options.MaxAge          = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload         = false;
    options.ExcludedHosts.Add("localhost");
    options.ExcludedHosts.Add("127.0.0.1");
    options.ExcludedHosts.Add("[::1]");
});

// ── CORS — allow Angular dev origin ────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // GetSection().Get<string[]>() works for JSON arrays and __0/__1 env-var notation.
        // Fall back to splitting a scalar string so that ALLOWED_ORIGINS=http://localhost:4200
        // (set by DotNetEnv from the .env file) also resolves to a single-element array.
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                   ?? builder.Configuration["AllowedOrigins"]
                              ?.Split(',', StringSplitOptions.RemoveEmptyEntries
                                         | StringSplitOptions.TrimEntries)
                   ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// ── Swagger UI — Development only (AC-002) ────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClinicalHealthcare API v1"));
}

// ── HSTS (AC-003) — must precede HTTPS redirect so the header is set on
// the HTTPS response before any redirect occurs. ExcludedHosts (configured
// above) prevents the header from being sent for localhost requests.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
// ── Session allowlist + TTL refresh (AC-002 / TASK_015) ────────────────────────
// Must run AFTER UseAuthentication (context.User populated) and
// BEFORE UseAuthorization (so invalid sessions are rejected with 401
// before policy evaluation).
app.UseMiddleware<SessionTtlMiddleware>();
// ── RBAC violation audit middleware (AC-002) ──────────────────────────────
// MUST be placed BEFORE UseAuthorization so InvokeAsync registers the
// Response.OnStarting callback before the authorization middleware may
// short-circuit with 403. If placed after, the middleware is never reached
// on a 403 short-circuit and the audit entry is never written.
app.UseMiddleware<RbacViolationHandler>();

app.UseAuthorization();

// ── Correlation ID propagation + Serilog request logging (AC-002) ───────────
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

// ── Hangfire Dashboard — admin role only (AC-004) ────────────────────────────
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()]
});

// ── Health endpoint (AC-001) — returns JSON {"status":"Healthy"} ───────────
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Healthy ? "Healthy"
                   : report.Status == HealthStatus.Degraded ? "Degraded"
                   : "Unhealthy"
        });
        await context.Response.WriteAsync(result);
    }
});

// ── Feature endpoints (AC-005) ────────────────────────────────────────────
app.MapEndpointDefinitions();
// ── Hangfire recurring jobs (AC-005 / TASK_021) ────────────────────────────────
// AC-005: expire outstanding swap offers every minute.
RecurringJob.AddOrUpdate<ExpireSwapOfferJob>(
    "expire-swap-offers",
    j => j.ExecuteAsync(null!),
    Cron.Minutely);

// ── Auto-migrate database on startup ──────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var clinicalDb = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
    
    app.Logger.LogInformation("Applying database migrations...");
    await appDb.Database.MigrateAsync();
    await clinicalDb.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations complete.");
}

// ── Dev seed: create default accounts if table is empty ──────────────────────
// Run in both Development AND Production (for initial deployment)
await DevDataSeeder.SeedAsync(app.Services, app.Logger);

app.Run();