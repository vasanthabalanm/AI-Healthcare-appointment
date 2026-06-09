using System.Net;
using System.Text;
using ClinicalHealthcare.Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>Unit tests for <see cref="OllamaCodeGenerationService"/> (TASK_045 AC-002/AC-003/AC-004, TASK_046 AC-001/AC-004).</summary>
public sealed class OllamaCodeGenerationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OllamaCodeGenerationService BuildService(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
               .Setup<Task<HttpResponseMessage>>(
                   "SendAsync",
                   ItExpr.IsAny<HttpRequestMessage>(),
                   ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync(response);

        var client = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Ollama")).Returns(client);

        return new OllamaCodeGenerationService(factory.Object, NullLogger<OllamaCodeGenerationService>.Instance);
    }

    private static string WrapInOllamaChatResponse(string contentJson) =>
        $$"""{"model":"biomistral","message":{"role":"assistant","content":{{System.Text.Json.JsonSerializer.Serialize(contentJson)}}},"done":true}""";

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateIcd10Async_ValidCodes_ReturnsAll()
    {
        var arrayJson = """[{"code":"J18.9","description":"Pneumonia","confidence":0.92},{"code":"I10","description":"Hypertension","confidence":0.88}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Equal(2, results.Count);
        Assert.Equal("J18.9", results[0].SuggestedCode);
        Assert.Equal("I10",   results[1].SuggestedCode);
    }

    [Fact]
    public async Task GenerateIcd10Async_MalformedCode_Rejected()
    {
        // "lowercase123" fails the regex
        var arrayJson = """[{"code":"lowercase123","description":"Bad","confidence":0.9},{"code":"Z00.0","description":"Good","confidence":0.85}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Single(results);
        Assert.Equal("Z00.0", results[0].SuggestedCode);
    }

    [Fact]
    public async Task GenerateIcd10Async_MoreThan20_Capped()
    {
        // Build 25 valid ICD-10 codes
        var items = Enumerable.Range(0, 25)
            .Select(i => $"{{\"code\":\"A{i:D2}.0\",\"description\":\"Desc{i}\",\"confidence\":0.8}}")
            .ToArray();
        var arrayJson = $"[{string.Join(',', items)}]";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Equal(20, results.Count);
    }

    [Fact]
    public async Task GenerateIcd10Async_LowConfidence_FlagAvailable()
    {
        var arrayJson = """[{"code":"F32.9","description":"Depression","confidence":0.45}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Single(results);
        Assert.Equal(0.45, results[0].ConfidenceScore, precision: 2);
        // The service returns the raw score; the job applies the < 0.60 threshold for LowConfidenceFlag.
    }

    [Fact]
    public async Task GenerateIcd10Async_HttpError_Throws()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var svc      = BuildService(response);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.GenerateIcd10Async("patient summary"));
    }

    [Fact]
    public async Task GenerateIcd10Async_EmptyArray_ReturnsEmpty()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse("[]"), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GenerateIcd10Async_MalformedResponseJson_ReturnsEmpty()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid json}", Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateIcd10Async("patient summary");

        Assert.Empty(results);
    }

    // ── TASK_046: CPT tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCptAsync_ValidCodes_ReturnsAll()
    {
        var arrayJson = """[{"code":"99213","description":"Office visit","confidence":0.90},{"code":"71046","description":"Chest X-ray","confidence":0.85}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateCptAsync("patient summary");

        Assert.Equal(2, results.Count);
        Assert.Equal("99213", results[0].SuggestedCode);
        Assert.Equal("71046", results[1].SuggestedCode);
    }

    [Fact]
    public async Task GenerateCptAsync_MalformedCode_Rejected()
    {
        // "ABC12" fails ^\d{5}$ (letters not allowed)
        var arrayJson = """[{"code":"ABC12","description":"Bad","confidence":0.9},{"code":"99213","description":"Good","confidence":0.85}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateCptAsync("patient summary");

        Assert.Single(results);
        Assert.Equal("99213", results[0].SuggestedCode);
    }

    [Fact]
    public async Task GenerateCptAsync_EmptyArray_ReturnsEmpty()
    {
        // AC-003: "No procedures identified" → Ollama returns [] → zero rows
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse("[]"), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateCptAsync("patient summary");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GenerateCptAsync_HttpError_Throws()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var svc      = BuildService(response);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => svc.GenerateCptAsync("patient summary"));
    }

    [Fact]
    public async Task GenerateCptAsync_ICD10CodeFormat_Rejected()
    {
        // A valid ICD-10 code "J18.9" must be rejected by the CPT validator (fails ^\d{5}$)
        var arrayJson = """[{"code":"J18.9","description":"Pneumonia","confidence":0.9},{"code":"99213","description":"Office visit","confidence":0.88}]""";
        var response  = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapInOllamaChatResponse(arrayJson), Encoding.UTF8, "application/json")
        };

        var svc     = BuildService(response);
        var results = await svc.GenerateCptAsync("patient summary");

        Assert.Single(results);
        Assert.Equal("99213", results[0].SuggestedCode);
    }
}
