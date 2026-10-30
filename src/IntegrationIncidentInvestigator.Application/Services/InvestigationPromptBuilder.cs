using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Application.Services;

public sealed class InvestigationPromptBuilder(IOptions<AiProviderSettings> settings) : IInvestigationPromptBuilder
{
    private const string PromptResourceName = "IntegrationIncidentInvestigator.Application.Prompts.IncidentInvestigationPrompt.v1.txt";
    private static readonly Lazy<string> SystemPrompt = new(LoadSystemPrompt);
    private static readonly Lazy<JsonElement> ResponseSchema = new(LoadResponseSchema);

    public LlmRequest BuildInvestigationRequest(IncidentContext context, IReadOnlyList<EvidenceExcerpt> evidence,
        IReadOnlyList<HistoricalMatch> historicalMatches)
    {
        var payload = new
        {
            context.IncidentId,
            context.SystemName,
            context.IntegrationName,
            context.ProblemDescription,
            Evidence = evidence.Select(item => new
            {
                item.FileName,
                Category = item.Category.ToString(),
                item.Content,
                item.SelectionReason,
                item.OriginalSizeBytes,
                item.SelectedCharacters,
                item.IsImage
            }),
            HistoricalMatches = historicalMatches.Select(match => new
            {
                match.Incident.IncidentId,
                match.Incident.Title,
                match.Incident.IntegrationName,
                match.Incident.ErrorSignature,
                match.Incident.RootCause,
                match.Incident.JiraKey,
                Similarity = Math.Round(match.Similarity, 3),
                match.Reasons
            })
        };

        var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new LlmRequest(SystemPrompt.Value, content, settings.Value.Model, ResponseSchema.Value);
    }

    private static JsonElement LoadResponseSchema()
    {
        using var stream = typeof(InvestigationPromptBuilder).Assembly.GetManifestResourceStream(
            "IntegrationIncidentInvestigator.Application.Prompts.IncidentInvestigationResponse.v1.schema.json")
            ?? throw new InvalidOperationException("The embedded investigation response schema was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static string LoadSystemPrompt()
    {
        using var stream = typeof(InvestigationPromptBuilder).Assembly.GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{PromptResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
