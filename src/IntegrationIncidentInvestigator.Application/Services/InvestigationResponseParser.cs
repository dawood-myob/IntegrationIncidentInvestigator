using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Application.Services;

public sealed class InvestigationResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public LlmInvestigationResponse Parse(string json)
    {
        var candidate = StripCodeFence(json.Trim());
        LlmInvestigationResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<LlmInvestigationResponse>(candidate, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The AI returned malformed JSON or a value with an incorrect type (line {exception.LineNumber}, byte {exception.BytePositionInLine}). No output was generated.", exception);
        }

        if (response is null
            || string.IsNullOrWhiteSpace(response.IncidentSummary)
            || string.IsNullOrWhiteSpace(response.TeamsSummary)
            || string.IsNullOrWhiteSpace(response.RootCause)
            || string.IsNullOrWhiteSpace(response.JiraComment)
            || response.JiraStory is null
            || string.IsNullOrWhiteSpace(response.JiraStory.Title)
            || string.IsNullOrWhiteSpace(response.JiraStory.Objective)
            || string.IsNullOrWhiteSpace(response.JiraStory.UserStory)
            || string.IsNullOrWhiteSpace(response.JiraStory.Description)
            || response.JiraStory.ErrorsObserved is null
            || response.JiraStory.Scope is null || response.JiraStory.Scope.Count == 0
            || response.JiraStory.AcceptanceCriteria is null || response.JiraStory.AcceptanceCriteria.Count == 0
            || response.Evidence is null
            || response.RecommendedActions is null
            || response.AffectedSystems is null
            || response.ErrorMessages is null
            || response.CorrelationIds is null
            || response.Attachments is null
            || !Enum.TryParse<EvidenceConfidence>(response.RootCauseConfidence, true, out _))
        {
            throw new InvalidDataException("The AI response is missing required investigation fields. No output was generated.");
        }

        if (response.JiraStory.ErrorsObserved.Concat(response.JiraStory.Scope).Concat(response.JiraStory.AcceptanceCriteria).Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("The AI returned empty Jira story items. No output was generated.");
        }

        if (response.Evidence.Any(string.IsNullOrWhiteSpace)
            || response.RecommendedActions.Any(string.IsNullOrWhiteSpace)
            || response.Attachments.Any(item => item is null || string.IsNullOrWhiteSpace(item.FileName) || string.IsNullOrWhiteSpace(item.Reason)))
        {
            throw new InvalidDataException("The AI returned an invalid attachment recommendation. No output was generated.");
        }

        return response;
    }

    private static string StripCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstNewLine = value.IndexOf('\n');
        var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? value[(firstNewLine + 1)..lastFence].Trim()
            : value;
    }
}
