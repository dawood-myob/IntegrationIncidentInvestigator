using System.Net;
using System.Text;
using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Application.Services;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Tests;

public sealed class LlmResponseContractTests
{
    [Fact]
    public void PromptIncludesSchemaMatchingEveryResponseProperty()
    {
        var request = BuildRequest();
        Assert.True(request.ResponseSchema.HasValue);
        var schema = request.ResponseSchema.Value;
        var properties = schema.GetProperty("properties");
        var names = typeof(LlmInvestigationResponse).GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Order().ToArray();
        Assert.Equal(names, properties.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(names, schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order().ToArray());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("object", properties.GetProperty("jiraStory").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("evidence").GetProperty("items").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("jiraStory", "\"wrong type\"")]
    [InlineData("evidence", "[{}]")]
    public void ParserRejectsIncorrectFieldTypesWithoutExposingContent(string field, string value)
    {
        var error = Assert.Throws<InvalidDataException>(() => new InvestigationResponseParser()
            .Parse("{\"" + field + "\":" + value + ",\"incidentSummary\":\"private incident text\"}"));
        Assert.Contains("incorrect type", error.Message);
        Assert.DoesNotContain("private incident text", error.Message);
    }

    [Fact]
    public async Task CompatibleClientSendsStrictSchema()
    {
        var request = BuildRequest();
        var responseContent = await new FakeLlmClient().CompleteAsync(request, CancellationToken.None);
        using var handler = new RecordingHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = responseContent } } }
        }));
        var result = await CompleteAsync(handler, request);
        Assert.Equal(responseContent, result);
        Assert.Equal("Unknown", new InvestigationResponseParser().Parse(result).RootCauseConfidence);
        using var sent = JsonDocument.Parse(handler.RequestBody!);
        var format = sent.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal(JsonSerializer.Serialize(request.ResponseSchema!.Value),
            JsonSerializer.Serialize(format.GetProperty("json_schema").GetProperty("schema")));
    }

    [Theory]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{\"}}]}", "incomplete")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"content_filter\",\"message\":{\"content\":\"private response\"}}]}", "blocked")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"private refusal\",\"content\":null}}]}", "declined")]
    [InlineData("{\"choices\":{}}", "did not contain")]
    public async Task CompatibleClientRejectsIncompleteRefusedAndInvalidEnvelopes(string response, string expected)
    {
        using var handler = new RecordingHandler(response);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CompleteAsync(handler, BuildRequest()));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("private", error.Message);
    }

    private static LlmRequest BuildRequest() => new InvestigationPromptBuilder(
        Options.Create(new AiProviderSettings { Model = "test-model" }))
        .BuildInvestigationRequest(new IncidentContext("C:\\synthetic", "INC-SYNTHETIC",
            "Synthetic connectivity test only.", [], "Test system", "Test integration"), [], []);

    private static async Task<string> CompleteAsync(RecordingHandler handler, LlmRequest request)
    {
        var variable = "INCIDENT_AI_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "synthetic-test-key");
        try
        {
            using var httpClient = new HttpClient(handler, disposeHandler: false);
            var client = new OpenAiCompatibleLlmClient(new ClientFactory(httpClient),
                Options.Create(new AiProviderSettings
                {
                    Endpoint = "https://example.invalid/chat/completions",
                    ApiKeyEnvironmentVariable = variable
                }), NullLogger<OpenAiCompatibleLlmClient>.Instance);
            return await client.CompleteAsync(request, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
