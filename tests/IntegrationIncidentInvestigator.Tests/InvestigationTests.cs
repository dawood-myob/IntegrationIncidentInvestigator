using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Application.Services;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Core.Services;
using IntegrationIncidentInvestigator.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Tests;

public sealed class InvestigationTests
{
    [Theory]
    [InlineData("problem.txt", FileCategory.ProblemDescription)]
    [InlineData("integration.log", FileCategory.IntegrationLog)]
    [InlineData("service.log", FileCategory.ApplicationLog)]
    [InlineData("failure.json", FileCategory.Json)]
    [InlineData("screen.PNG", FileCategory.Screenshot)]
    [InlineData("settings.ini", FileCategory.Configuration)]
    [InlineData("error.stacktrace", FileCategory.ErrorOrException)]
    public void ClassifierRecognizesCommonIncidentFiles(string name, FileCategory expected)
    {
        var file = new FileClassifier().Classify(name, 100, DateTime.UtcNow);

        Assert.Equal(expected, file.Category);
        if (expected == FileCategory.Configuration)
        {
            Assert.False(file.IncludeInInvestigation);
        }
    }

    [Fact]
    public void FingerprintNormalizesDynamicTimestampsAndIdentifiers()
    {
        var fingerprinter = new ErrorFingerprinter();
        var first = "Employee 92837 failed at 2026-10-30 09:14:31 Request ID 5f3a90d0-81d0-4b59-9ec0-0987abcdef12: Connection timeout after 30 seconds";
        var second = "Employee 77106 failed at 2026-11-02 12:45:22 Request ID 2a4b90d0-81d0-4b59-9ec0-0987abcdef99: Connection timeout after 30 seconds";

        Assert.Equal(fingerprinter.CreateFingerprint("Workday", "Absence", first), fingerprinter.CreateFingerprint("Workday", "Absence", second));
        Assert.Contains("<employee_id>", fingerprinter.Normalize(first));
        Assert.Contains("<timestamp>", fingerprinter.Normalize(first));
    }

    [Fact]
    public void InvestigationSettingsValidatorAcceptsDefaultsAndRejectsInvalidLimits()
    {
        var validator = new InvestigationSettingsValidator();

        Assert.True(validator.Validate(Options.DefaultName, new InvestigationSettings()).Succeeded);
        Assert.False(validator.Validate(Options.DefaultName, new InvestigationSettings { SimilarityThreshold = 1.5 }).Succeeded);
        Assert.False(validator.Validate(Options.DefaultName, new InvestigationSettings { MaxContextCharacters = 10 }).Succeeded);
    }

    [Fact]
    public void ProviderSelectorOffersRealProvidersAndRetainsProviderModels()
    {
        var selector = new AiProviderSelector(Options.Create(new AiProviderSettings { Provider = "openaicompatible" }));

        Assert.Equal(AiProviders.OpenAICompatible, selector.CurrentProvider);
        selector.SelectModel("gpt-5");
        selector.Select(AiProviders.Claude);
        Assert.Equal("claude-sonnet-4-6", selector.CurrentModel);
        selector.SelectModel("custom-claude-model");
        selector.Select(AiProviders.GitHubCopilot);
        Assert.Equal("gpt-4.1", selector.CurrentModel);
        selector.Select(AiProviders.OpenAICompatible);
        Assert.Equal("gpt-5", selector.CurrentModel);
        selector.Select(AiProviders.Claude);
        Assert.Equal("custom-claude-model", selector.CurrentModel);
        Assert.Contains("custom-claude-model", selector.Models);
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.Select("Mock"));
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.Select("Unknown"));
        Assert.Throws<ArgumentException>(() => selector.SelectModel(" "));
    }

    [Fact]
    public void RecurrenceMatcherReportsExplainableSimilarIntegrationMatch()
    {
        var history = new HistoricalIncident("INC-OLD", DateTime.UtcNow.AddDays(-4), "Timeout", "HR", "Absence",
            "fingerprint", "Connection timeout after 30 seconds", "Unknown", "INT-842", null, "Open", "timeout", "Retry");

        var matches = new RecurrenceMatcher().FindMatches("HR", "Absence", "Connection timeout after 30 seconds", [history], 0.75);

        Assert.Single(matches);
        Assert.True(matches[0].Similarity >= 0.75);
        Assert.Contains(matches[0].Reasons, reason => reason.Contains("Same integration", StringComparison.Ordinal));
        Assert.Empty(new RecurrenceMatcher().FindMatches("HR", "Payroll", "Connection timeout after 30 seconds", [history], 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecurrenceMatcher().FindMatches("HR", "Absence", "timeout", [history], 1.1));
    }

    [Fact]
    public async Task LogExtractorStreamsLargeLogAndSelectsBoundedErrorContext()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "integration.log");
            await using (var writer = new StreamWriter(path))
            {
                for (var index = 0; index < 10_000; index++)
                {
                    await writer.WriteLineAsync($"2026-10-30T09:14:{index % 60:00}Z INFO correlation={index} normal processing record");
                }

                await writer.WriteLineAsync("2026-10-30T09:15:00Z INFO correlation=ABC123 submitting request");
                await writer.WriteLineAsync("2026-10-30T09:15:01Z ERROR correlation=ABC123 Connection timeout after 30 seconds");
                await writer.WriteLineAsync("   at Integration.Worker.SendAsync() in Worker.cs:line 42");
                await writer.WriteLineAsync("2026-10-30T09:15:02Z INFO correlation=ABC123 retry scheduled");
            }

            var info = new FileInfo(path);
            var file = new FileClassifier().Classify(path, info.Length, info.LastWriteTimeUtc);
            var excerpt = await new FileContentExtractor().ExtractAsync(file, 1, 2_000, CancellationToken.None);

            Assert.True(info.Length > 100_000);
            Assert.True(excerpt.SelectedCharacters <= 2_000);
            Assert.Contains("Connection timeout", excerpt.Content);
            Assert.Contains("Worker.cs:line 42", excerpt.Content);
            Assert.Contains("Streamed", excerpt.SelectionReason);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LogExtractorBoundsAnExtremelyLongSingleLine()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "service.log");
            await File.WriteAllTextAsync(path, new string('x', 1_000_000) + " ERROR Connection timeout after 30 seconds");
            var info = new FileInfo(path);
            var file = new FileClassifier().Classify(path, info.Length, info.LastWriteTimeUtc);

            var excerpt = await new FileContentExtractor().ExtractAsync(file, 1, 1_000, CancellationToken.None);

            Assert.True(excerpt.SelectedCharacters <= 1_000);
            Assert.Contains("Connection timeout", excerpt.Content);
            Assert.Contains("line truncated", excerpt.Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractorRejectsOversizedNonLogFiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "payload.json");
            await File.WriteAllTextAsync(path, "{\"payload\":\"large enough\"}");
            var info = new FileInfo(path);
            var file = new FileClassifier().Classify(path, info.Length, info.LastWriteTimeUtc);

            await Assert.ThrowsAsync<InvalidDataException>(() => new FileContentExtractor().ExtractAsync(file, 1, 100, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResponseParserAcceptsJsonFenceAndRejectsMalformedOrIncompleteResponses()
    {
        var parser = new InvestigationResponseParser();
        var valid = JsonSerializer.Serialize(new LlmInvestigationResponse
        {
            IncidentSummary = "Incident summary",
            TeamsSummary = "Teams message",
            RootCause = "Unknown",
            RootCauseConfidence = "Unknown",
            JiraStory = new JiraStory
            {
                Title = "Integration Processing Errors", Objective = "Correct rejected data", UserStory = "As a developer, I want valid data.",
                Description = "The cause is unknown.", Scope = ["Investigate and correct."], AcceptanceCriteria = ["The cause is documented."]
            },
            JiraComment = "Jira comment"
        });

        Assert.Equal("Incident summary", parser.Parse($"```json\n{valid}\n```").IncidentSummary);
        Assert.Throws<InvalidDataException>(() => parser.Parse("{not-json"));
        Assert.Throws<InvalidDataException>(() => parser.Parse("{}"));
    }

    [Fact]
    public async Task SqliteHistoryRepositoryPersistsIncidentLocally()
    {
        var directory = CreateTempDirectory();
        try
        {
            var repository = new SqliteIncidentHistoryRepository(Path.Combine(directory, "incidents.db"));
            var incident = new HistoricalIncident("INC-TEST", DateTime.UtcNow, "Timeout", "HR", "Absence", "hash", "Connection timeout",
                "Unknown", "INT-842", null, "Open", "timeout", "Retry");

            await repository.SaveAsync(incident, CancellationToken.None);
            var loaded = await repository.GetRecentAsync(10, CancellationToken.None);

            Assert.Single(loaded);
            Assert.Equal(incident.IncidentId, loaded[0].IncidentId);
            Assert.Equal(incident.ErrorFingerprint, loaded[0].ErrorFingerprint);
            await repository.UpdateJiraReferenceAsync(incident.IncidentId, "INT-842", CancellationToken.None);
            Assert.Equal("INT-842", (await repository.GetRecentAsync(10, CancellationToken.None))[0].JiraKey);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ArtifactWriterSavesOnlySelectedJiraMode()
    {
        var directory = CreateTempDirectory();
        try
        {
            var result = SampleResult();
            await new InvestigationArtifactFormatter().SaveAsync(directory, result, JiraMode.CommentOnExisting, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(directory, "investigation.json")));
            Assert.True(File.Exists(Path.Combine(directory, "teams-summary.txt")));
            Assert.True(File.Exists(Path.Combine(directory, "jira-comment.txt")));
            Assert.Contains("trace.log", await File.ReadAllTextAsync(Path.Combine(directory, "jira-comment.txt")));
            Assert.False(File.Exists(Path.Combine(directory, "jira.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PromptBuilderResolvesFromConfiguredAiOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AiProviderSettings>(settings => settings.Model = "test-model");
        services.AddIncidentInvestigation();

        using var provider = services.BuildServiceProvider();
        var promptBuilder = provider.GetRequiredService<IInvestigationPromptBuilder>();
        var request = promptBuilder.BuildInvestigationRequest(
            new IncidentContext("C:\\incidents", "INC-TEST", "", [], null, null), [], []);

        Assert.Equal("test-model", request.Model);
    }

    [Fact]
    public async Task ScannerFindsNestedFilesAndSkipsGeneratedArtifacts()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllTextAsync(Path.Combine(directory, "nested", "integration.log"), "ERROR timeout");
            await File.WriteAllTextAsync(Path.Combine(directory, "investigation.json"), "{}");

            var files = await new IncidentFileScanner(new FileClassifier(), NullLogger<IncidentFileScanner>.Instance)
                .ScanAsync(directory, CancellationToken.None);

            Assert.Single(files);
            Assert.Equal("integration.log", files[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreparationPrioritizesAuditAndSharesBudgetAcrossLargePayloads()
    {
        var directory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a-large.xml"), new string('x', 5000));
            await File.WriteAllTextAsync(Path.Combine(directory, "z_Diagnostic_Audit.html"), "<p>ERROR Invalid ID '821'</p>");
            await File.WriteAllTextAsync(Path.Combine(directory, "z-source.csv"), "JournalKey,Product\n83,821");
            var files = await new IncidentFileScanner(new FileClassifier(), NullLogger<IncidentFileScanner>.Instance)
                .ScanAsync(directory, CancellationToken.None);
            var llm = new RecordingLlmClient();
            var service = new IncidentInvestigationService(new FileContentExtractor(), new InMemoryHistoryRepository(),
                new InvestigationPromptBuilder(Options.Create(new AiProviderSettings())), llm, new InvestigationResponseParser(),
                new ErrorFingerprinter(), new RecurrenceMatcher(), Options.Create(new InvestigationSettings { MaxContextCharacters = 1000 }),
                NullLogger<IncidentInvestigationService>.Instance);

            var prepared = await service.PrepareAsync(new IncidentContext(directory, "INC-TEST", "Timeout", files, "Test", "Test"), null, CancellationToken.None);

            Assert.Equal(0, llm.CallCount);
            Assert.Equal("z_Diagnostic_Audit.html", prepared.Evidence[0].FileName);
            Assert.Contains(prepared.Evidence, item => item.FileName == "z-source.csv" && item.Content.Contains("83,821"));
            Assert.Contains(prepared.Evidence, item => item.FileName == "a-large.xml" && item.SelectedCharacters > 0);
            Assert.True(prepared.Evidence.Sum(item => item.Content.Length) + prepared.Context.ProblemDescription.Length <= 1000);
            Assert.Contains("821", prepared.ErrorSignature);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreparationDoesNotCallLlmUntilExplicitInvestigation()
    {
        var directory = CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(directory, "integration.log");
            await File.WriteAllTextAsync(logPath, "ERROR Connection timeout after 30 seconds");
            var logInfo = new FileInfo(logPath);
            var logFile = new FileClassifier().Classify(logPath, logInfo.Length, logInfo.LastWriteTimeUtc);
            var history = new InMemoryHistoryRepository();
            history.Add(new HistoricalIncident("INC-TEST", DateTime.UtcNow.AddDays(-1), "Previous run", "HR", "Absence",
                new ErrorFingerprinter().CreateFingerprint("HR", "Absence", "ERROR Connection timeout after 30 seconds"),
                "ERROR Connection timeout after 30 seconds", "Unknown", null, null, "NewIssue", "HR", ""));
            var llm = new RecordingLlmClient();
            var service = new IncidentInvestigationService(
                new FileContentExtractor(), history, new InvestigationPromptBuilder(Options.Create(new AiProviderSettings())),
                llm, new InvestigationResponseParser(), new ErrorFingerprinter(), new RecurrenceMatcher(),
                Options.Create(new InvestigationSettings()), NullLogger<IncidentInvestigationService>.Instance);
            var context = new IncidentContext(directory, "INC-TEST", "Integration failed.", [logFile], "HR", "Absence");

            var prepared = await service.PrepareAsync(context, null, CancellationToken.None);
            Assert.Equal(0, llm.CallCount);
            Assert.Empty(prepared.HistoricalMatches);
            Assert.Equal("Integration failed.", prepared.Context.ProblemDescription);

            var result = await service.InvestigateAsync(prepared, null, CancellationToken.None);
            Assert.Equal(1, llm.CallCount);
            Assert.Equal(RecurrenceStatus.NewIssue, result.Recurrence);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IncidentInvestigationResult SampleResult() => new(
        "Summary", "Teams", "Problem", "Unknown", EvidenceConfidence.Unknown, [], [], [], [], [],
        [new AttachmentRecommendation("trace.log", true, "Contains the relevant exception.")],
        RecurrenceStatus.NewIssue, [], null, "Jira content", "Jira comment", "", "hash", DateTime.UtcNow);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "III-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InMemoryHistoryRepository : IIncidentHistoryRepository
    {
        private readonly List<HistoricalIncident> _incidents = [];
        public void Add(HistoricalIncident incident) => _incidents.Add(incident);
        public Task<IReadOnlyList<HistoricalIncident>> GetRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalIncident>>(_incidents.Take(limit).ToArray());
        public Task SaveAsync(HistoricalIncident incident, CancellationToken cancellationToken) { _incidents.Add(incident); return Task.CompletedTask; }
        public Task UpdateJiraReferenceAsync(string incidentId, string jiraKey, CancellationToken cancellationToken)
        {
            var index = _incidents.FindIndex(item => item.IncidentId == incidentId);
            if (index < 0) throw new KeyNotFoundException();
            _incidents[index] = _incidents[index] with { JiraKey = jiraKey };
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLlmClient : ILlmClient
    {
        public int CallCount { get; private set; }
        public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return new FakeLlmClient().CompleteAsync(request, cancellationToken);
        }
    }
}
