using System.Text.Json;

namespace IntegrationIncidentInvestigator.Application.Models;

public sealed record LlmRequest(string SystemPrompt, string UserContent, string Model, JsonElement? ResponseSchema = null);

public sealed class AiProviderSettings
{
    public string Provider { get; set; } = AiProviders.OpenAICompatible;
    public string Model { get; set; } = "gpt-4.1";
    public string[] Models { get; set; } = ["gpt-4.1", "gpt-4.1-mini", "gpt-5"];
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKeyEnvironmentVariable { get; set; } = "INCIDENT_AI_API_KEY";
    public int MaxOutputTokens { get; set; } = 8_192;
    public int TimeoutSeconds { get; set; } = 180;
    public ClaudeProviderSettings Claude { get; set; } = new();
    public CopilotProviderSettings Copilot { get; set; } = new();
}

public static class AiProviders
{
    public const string OpenAICompatible = "OpenAICompatible";
    public const string Claude = "Claude";
    public const string GitHubCopilot = "GitHubCopilot";
    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(new[] { OpenAICompatible, Claude, GitHubCopilot });
}

public sealed class ClaudeProviderSettings
{
    public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";
    public string ApiKeyEnvironmentVariable { get; set; } = "ANTHROPIC_API_KEY";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string[] Models { get; set; } = ["claude-sonnet-4-6", "claude-opus-4-6"];
}

public sealed class CopilotProviderSettings
{
    public string Model { get; set; } = "gpt-4.1";
    public string[] Models { get; set; } = ["gpt-4.1", "gpt-5", "claude-sonnet-4.6"];
    public string? HomeDirectory { get; set; }
}

public sealed class LlmInvestigationResponse
{
    public string IncidentSummary { get; set; } = "";
    public string TeamsSummary { get; set; } = "";
    public string ProblemStatement { get; set; } = "";
    public string RootCause { get; set; } = "";
    public string RootCauseConfidence { get; set; } = "Unknown";
    public List<string> Evidence { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public List<string> AffectedSystems { get; set; } = [];
    public List<string> ErrorMessages { get; set; } = [];
    public List<string> CorrelationIds { get; set; } = [];
    public List<AttachmentItem> Attachments { get; set; } = [];
    public JiraStory JiraStory { get; set; } = new();
    public string JiraComment { get; set; } = "";
}

public sealed class JiraStory
{
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string UserStory { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ErrorsObserved { get; set; } = [];
    public List<string> Scope { get; set; } = [];
    public List<string> AcceptanceCriteria { get; set; } = [];
}

public sealed class AttachmentItem
{
    public string FileName { get; set; } = "";
    public bool Recommended { get; set; }
    public string Reason { get; set; } = "";
}
