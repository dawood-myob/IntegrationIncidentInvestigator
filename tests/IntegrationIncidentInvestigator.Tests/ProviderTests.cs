using System.Net;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Application.Services;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Core.Services;
using IntegrationIncidentInvestigator.Infrastructure;
using IntegrationIncidentInvestigator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Tests;

public sealed class ProviderTests
{
    [Fact]
    public void ProductionDependencyGraphResolvesAllRealProviders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddIncidentInvestigation();
        services.AddIncidentInfrastructure();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<ProviderSwitchingLlmClient>(provider.GetRequiredService<ILlmClient>());
        Assert.IsType<ClaudeLlmClient>(provider.GetRequiredService<ClaudeLlmClient>());
        Assert.IsType<CopilotLlmClient>(provider.GetRequiredService<CopilotLlmClient>());
        Assert.IsType<AiProviderStatusService>(provider.GetRequiredService<IAiProviderStatusService>());
        Assert.DoesNotContain("Mock", AiProviders.Supported);
    }

    [Theory]
    [InlineData(AiProviders.OpenAICompatible, "custom-openai", "https://example.invalid/openai")]
    [InlineData(AiProviders.Claude, "custom-claude", "https://example.invalid/claude")]
    public async Task SelectedProviderAndModelReachCorrectWireProtocol(string provider, string model, string endpoint)
    {
        var variable = "INCIDENT_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "test-only-key");
        try
        {
            var settings = Options.Create(new AiProviderSettings
            {
                Endpoint = "https://example.invalid/openai", ApiKeyEnvironmentVariable = variable,
                Claude = new ClaudeProviderSettings { Endpoint = "https://example.invalid/claude", ApiKeyEnvironmentVariable = variable }
            });
            var request = BuildRequest();
            var content = await new FakeLlmClient().CompleteAsync(request, CancellationToken.None);
            using var handler = new Handler(provider == AiProviders.Claude
                ? JsonSerializer.Serialize(new { stop_reason = "end_turn", content = new[] { new { type = "text", text = content } } })
                : JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content } } } }));
            using var http = new HttpClient(handler);
            var factory = new Factory(http);
            var selector = new AiProviderSelector(settings);
            selector.Select(provider);
            selector.SelectModel(model);
            var client = new ProviderSwitchingLlmClient(selector,
                new OpenAiCompatibleLlmClient(factory, settings, NullLogger<OpenAiCompatibleLlmClient>.Instance),
                new ClaudeLlmClient(factory, settings, NullLogger<ClaudeLlmClient>.Instance),
                new CopilotLlmClient(settings, NullLogger<CopilotLlmClient>.Instance));

            var response = await client.CompleteAsync(request, CancellationToken.None);
            Assert.Equal(content, response);
            Assert.Equal(endpoint, handler.Uri);
            using var body = JsonDocument.Parse(handler.Body!);
            Assert.Equal(model, body.RootElement.GetProperty("model").GetString());
            Assert.DoesNotContain("temperature", handler.Body);
            if (provider == AiProviders.Claude)
            {
                Assert.True(handler.HasClaudeKey);
                Assert.False(handler.HasBearer);
                Assert.Equal("2023-06-01", handler.AnthropicVersion);
                Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
                var format = body.RootElement.GetProperty("output_config").GetProperty("format");
                Assert.Equal("json_schema", format.GetProperty("type").GetString());
                Assert.Equal(JsonSerializer.Serialize(request.ResponseSchema!.Value), JsonSerializer.Serialize(format.GetProperty("schema")));
            }
            else
            {
                Assert.True(handler.HasBearer);
                Assert.False(handler.HasClaudeKey);
                Assert.Equal(8192, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("max_tokens", "incomplete")]
    [InlineData("refusal", "refused")]
    [InlineData("tool_use", "incomplete")]
    public async Task ClaudeRejectsUnfinishedOrRefusedOutput(string stopReason, string expected)
    {
        var variable = "INCIDENT_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "test-only-key");
        try
        {
            using var handler = new Handler(JsonSerializer.Serialize(new
            {
                stop_reason = stopReason, content = new[] { new { type = "text", text = "private incident content" } }
            }));
            using var http = new HttpClient(handler);
            var client = new ClaudeLlmClient(new Factory(http), Options.Create(new AiProviderSettings
            {
                Claude = new ClaudeProviderSettings { ApiKeyEnvironmentVariable = variable }
            }), NullLogger<ClaudeLlmClient>.Instance);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(BuildRequest(), CancellationToken.None));
            Assert.Contains(expected, error.Message);
            Assert.DoesNotContain("private incident content", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task CopilotClientConstructsWithExplicitCliHomeInEmptyMode()
    {
        var options = CopilotLlmClient.CreateClientOptions();
        var expectedHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");

        Assert.NotNull(options.BaseDirectory);
        Assert.Equal(expectedHome, options.BaseDirectory);
        Assert.True(Path.IsPathFullyQualified(options.BaseDirectory));
        Assert.Equal(CopilotClientMode.Empty, options.Mode);
        Assert.Equal(string.IsNullOrWhiteSpace(options.GitHubToken), options.UseLoggedInUser);
        Assert.Equal(CopilotLogLevel.None, options.LogLevel);
        await using var client = new CopilotClient(options);
    }

    [Fact]
    public void CopilotHomeIgnoresInheritedIdeHomeAndSupportsExplicitConfiguration()
    {
        var inheritedHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        var ideHome = Path.Combine(Path.GetTempPath(), "ide-copilot-home");
        var home = Path.Combine(Path.GetTempPath(), "incident-copilot-home");
        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", ideHome);
            var defaults = CopilotLlmClient.CreateClientOptions();
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"), defaults.BaseDirectory);
            Assert.Equal(ideHome, Environment.GetEnvironmentVariable("COPILOT_HOME"));

            var options = CopilotLlmClient.CreateClientOptions(home);
            Assert.Equal(Path.GetFullPath(home), options.BaseDirectory);
            Assert.Equal(CopilotClientMode.Empty, options.Mode);
            Assert.Equal(string.IsNullOrWhiteSpace(options.GitHubToken), options.UseLoggedInUser);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", inheritedHome);
        }
    }

    [Fact]
    public void CopilotPreservesChildEnvironmentAndUsesOnlyConfiguredSdkToken()
    {
        var names = new[] { "COPILOT_GITHUB_TOKEN", "COPILOT_SDK_AUTH_TOKEN", "COPILOT_RUNTIME_PROCESS_FILE_LOGGING" };
        var originals = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var marker = "INCIDENT_ENVIRONMENT_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable("COPILOT_GITHUB_TOKEN", "configured-test-token");
            Environment.SetEnvironmentVariable("COPILOT_SDK_AUTH_TOKEN", "host-token-do-not-reuse");
            Environment.SetEnvironmentVariable("COPILOT_RUNTIME_PROCESS_FILE_LOGGING", "1");
            Environment.SetEnvironmentVariable(marker, "preserved");
            var options = CopilotLlmClient.CreateClientOptions();

            Assert.Equal("configured-test-token", options.GitHubToken);
            Assert.False(options.UseLoggedInUser);
            Assert.NotNull(options.Environment);
            Assert.Equal("preserved", options.Environment[marker]);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), options.Environment["SystemRoot"]);
            }
            Assert.Equal("true", options.Environment["COPILOT_GITHUB_TELEMETRY_DISABLED"]);
            Assert.False(options.Environment.ContainsKey("COPILOT_SDK_AUTH_TOKEN"));
            Assert.False(options.Environment.ContainsKey("COPILOT_RUNTIME_PROCESS_FILE_LOGGING"));
            Assert.Equal("host-token-do-not-reuse", Environment.GetEnvironmentVariable("COPILOT_SDK_AUTH_TOKEN"));
        }
        finally
        {
            foreach (var original in originals)
                Environment.SetEnvironmentVariable(original.Key, original.Value);
            Environment.SetEnvironmentVariable(marker, null);
        }
    }

    [Fact]
    public void CopilotSessionHasNoToolsMemoryOrSessionStore()
    {
        var configuration = CopilotLlmClient.CreateSessionConfig(BuildRequest());
        Assert.Equal("test-model", configuration.Model);
        Assert.NotNull(configuration.AvailableTools);
        Assert.Empty(configuration.AvailableTools);
        Assert.False(configuration.EnableSessionStore);
        Assert.False(configuration.EnableSessionTelemetry);
        Assert.False(configuration.Memory!.Enabled);
        Assert.False(configuration.InfiniteSessions!.Enabled);
        Assert.Equal(SystemMessageMode.Replace, configuration.SystemMessage!.Mode);
    }

    [Fact]
    public async Task JiraStoryRendersRequiredSectionsAndRejectsEmptyAcceptanceCriteria()
    {
        var parser = new InvestigationResponseParser();
        var result = parser.Parse(await new FakeLlmClient().CompleteAsync(BuildRequest(), CancellationToken.None));
        var story = InvestigationArtifactFormatter.FormatStory(result.JiraStory);
        Assert.StartsWith(result.JiraStory.Title, story);
        Assert.Contains("Objective" + Environment.NewLine + result.JiraStory.Objective, story);
        Assert.Contains("User story" + Environment.NewLine + result.JiraStory.UserStory, story);
        Assert.Contains("Description" + Environment.NewLine + result.JiraStory.Description, story);
        Assert.Contains("Errors observed" + Environment.NewLine + "- Synthetic connection timeout", story);
        Assert.Contains("Scope" + Environment.NewLine + "- Establish the cause", story);
        Assert.Contains("Acceptance criteria" + Environment.NewLine + "1. The cause is documented.", story);
        Assert.Contains("2. Rejected records are loaded with no duplicates and reconciled.", story);
        result.JiraStory.AcceptanceCriteria.Clear();
        Assert.Throws<InvalidDataException>(() => parser.Parse(JsonSerializer.Serialize(result)));
    }

    [Fact]
    public async Task DiagnosticHtmlIsIncludedAndExtractedWithoutRendering()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_Diagnostic_Audit.html");
        try
        {
            await File.WriteAllTextAsync(path, "<html><body>Invalid ID value. '821' is not a valid Custom_Organization_Reference_ID.</body></html>");
            var info = new FileInfo(path);
            var file = new FileClassifier().Classify(path, info.Length, info.LastWriteTimeUtc);
            Assert.Equal(FileCategory.Html, file.Category);
            Assert.True(file.IncludeInInvestigation);
            var excerpt = await new FileContentExtractor().ExtractAsync(file, 1024, 500, CancellationToken.None);
            Assert.Contains("'821'", excerpt.Content);
            Assert.Contains("Custom_Organization_Reference_ID", excerpt.Content);
            Assert.InRange(excerpt.SelectedCharacters, 1, 500);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LlmRequest BuildRequest() => new InvestigationPromptBuilder(Options.Create(new AiProviderSettings { Model = "test-model" }))
        .BuildInvestigationRequest(new IncidentContext("C:\\synthetic", "INC-SYNTHETIC", "Synthetic timeout", [], "Test", "Test"), [], []);

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Body { get; private set; }
        public bool HasClaudeKey { get; private set; }
        public bool HasBearer { get; private set; }
        public string? AnthropicVersion { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            HasClaudeKey = request.Headers.Contains("x-api-key");
            HasBearer = request.Headers.Authorization?.Scheme == "Bearer";
            AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
