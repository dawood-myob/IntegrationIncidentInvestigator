using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;

namespace IntegrationIncidentInvestigator.Tests;

internal sealed class FakeLlmClient(params string[] evidence) : ILlmClient
{
    private readonly string[] _evidence = evidence.Length == 0
        ? ["The problem description reports an integration failure."]
        : evidence;

    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = new LlmInvestigationResponse
        {
            IncidentSummary = "Synthetic integration incident",
            TeamsSummary = "Synthetic incident. Investigation in progress.",
            ProblemStatement = "Synthetic timeout",
            RootCause = "Unknown; no cause confirmed by the synthetic fixture.",
            RootCauseConfidence = "Unknown",
            Evidence = _evidence.ToList(),
            JiraStory = new JiraStory
            {
                Title = "Synthetic Integration Processing Errors",
                Objective = "Prevent rejected records and reprocess safely.",
                UserStory = "As an Integration Developer, I want valid records loaded, so that downstream data is accurate.",
                Description = "The problem description reports a synthetic timeout. Its cause is unknown.",
                ErrorsObserved = ["Synthetic connection timeout"],
                Scope = ["Establish the cause and correct it through review.", "Safely reprocess and reconcile rejected records."],
                AcceptanceCriteria = ["The cause is documented.", "Rejected records are loaded with no duplicates and reconciled."]
            },
            JiraComment = "Synthetic incident update. No remediation has been performed."
        };
        return Task.FromResult(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
