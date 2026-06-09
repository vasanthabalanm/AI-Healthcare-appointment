using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.OCR;
using ClinicalHealthcare.Infrastructure.Security;
using ClinicalHealthcare.Infrastructure.Services;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Conventions;

/// <summary>
/// AC-005 � Startup convention test.
///
/// Every route registered by an <see cref="IEndpointDefinition"/> in the API
/// assembly MUST carry either <see cref="IAuthorizeData"/> or
/// <see cref="IAllowAnonymous"/> metadata.
///
/// If this test fails, a new endpoint was wired up without an explicit
/// authorization decision � treat this as a CI pipeline failure.
/// </summary>
public sealed class EndpointAuthorizationConventionTests
{
    [Fact]
    public void AllEndpoints_HaveAuthorizationOrAllowAnonymousMetadata()
    {
        // Build a minimal host with enough services for RequestDelegateFactory
        // to resolve all handler parameters so endpoint metadata can be built.
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(_ => { });

        // Register handler parameter types so RequestDelegateFactory classifies
        // them as "from DI" rather than throwing "UNKNOWN parameter" errors.
        builder.Services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseInMemoryDatabase("ConventionTest")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddDbContext<ClinicalDbContext>(opts =>
            opts.UseInMemoryDatabase("ClinicalConventionTest")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
        builder.Services.AddSingleton<IEmailService, NoOpEmailService>();
        builder.Services.AddSingleton<ICacheService, NoOpCacheService>();
        builder.Services.AddSingleton<IBackgroundJobClient, NoOpJobClient>();
        builder.Services.AddSingleton<INoShowRiskScoreService, NoOpRiskService>();
        builder.Services.AddSingleton<IRasaIntakeService, NoOpRasaIntakeService>();
        builder.Services.AddSingleton<IInsurancePreCheckService, NoOpInsurancePreCheckService>();
        builder.Services.AddSingleton<IClamAvScanService, NoOpClamAvScanService>();
        builder.Services.AddSingleton<IAesEncryptionService, NoOpAesEncryptionService>();
        builder.Services.AddSingleton<ITesseractOcrService, NoOpTesseractOcrService>();
        builder.Services.AddSingleton<IConflictService, NoOpConflictService>();

        var app = builder.Build();

        // Reflect over every concrete IEndpointDefinition in the API assembly
        // and map its routes onto the minimal host.
        var apiAssembly = typeof(IEndpointDefinition).Assembly;
        var definitions = apiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IEndpointDefinition).IsAssignableFrom(t))
            .Select(t => (IEndpointDefinition)Activator.CreateInstance(t)!)
            .ToList();

        foreach (var def in definitions)
            def.MapEndpoints(app);

        // Accessing DataSources.Endpoints triggers convention application � this
        // is the moment where .RequireAuthorization() and .AllowAnonymous()
        // metadata is written onto each RouteEndpoint.
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .ToList();

        var violations = endpoints
            .Where(ep => !ep.Metadata.Any(m => m is IAuthorizeData || m is IAllowAnonymous))
            .Select(ep => ep.DisplayName ?? "unknown")
            .ToList();

        // Known non-IEndpointDefinition routes excluded from this scan:
        //   /health  — MapHealthChecks; intentionally public (no auth metadata required)
        //   /hangfire — UseHangfireDashboard; guarded by HangfireDashboardAuthFilter (custom)
        // These are verified by code inspection and are not tracked as violations here.
        Assert.True(
            violations.Count == 0,
            $"The following endpoints are missing [Authorize] or [AllowAnonymous]:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v}")));
    }

    /// <summary>No-op email service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpEmailService : IEmailService
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>No-op cache service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>No-op Hangfire client used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => string.Empty;
        public bool ChangeState(string jobId, IState state, string? expectedCurrentStateName) => false;
    }

    /// <summary>No-op risk service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpRiskService : INoShowRiskScoreService
    {
        public Task<int> CalculateAsync(int patientId, DateTime slotTime, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    /// <summary>No-op Rasa service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpRasaIntakeService : IRasaIntakeService
    {
        public Task<RasaMessage> SendMessageAsync(string sessionId, string message, CancellationToken ct = default)
            => Task.FromResult(new RasaMessage("", 0.0));
    }

    /// <summary>No-op insurance pre-check service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpInsurancePreCheckService : IInsurancePreCheckService
    {
        public Task<InsuranceStatus> CheckAsync(string? insurerId, string? planCode, CancellationToken ct = default)
            => Task.FromResult(InsuranceStatus.Skipped);
    }

    /// <summary>No-op ClamAV scan service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpClamAvScanService : IClamAvScanService
    {
        public Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
            => Task.FromResult(ClamAvScanResult.Clean);
    }

    /// <summary>No-op AES encryption service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpAesEncryptionService : IAesEncryptionService
    {
        public (byte[] Ciphertext, byte[] Iv) Encrypt(Stream input)
            => (new byte[16], new byte[16]);
        public Stream Decrypt(string encryptedBlobPath)
            => new MemoryStream();
    }

    /// <summary>No-op Tesseract OCR service used only to satisfy DI during metadata inference.</summary>
    private sealed class NoOpTesseractOcrService : ITesseractOcrService
    {
        public Task<(string RawText, float AverageConfidence)> OcrAsync(
            Stream pdfStream, CancellationToken ct = default)
            => Task.FromResult((string.Empty, 0f));
    }

    /// <summary>No-op IConflictService for convention-scan DI.</summary>
    private sealed class NoOpConflictService : IConflictService
    {
        public Task<bool> HasUnresolvedConflictsAsync(int patientId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<int> GetUnresolvedCountAsync(int patientId, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
