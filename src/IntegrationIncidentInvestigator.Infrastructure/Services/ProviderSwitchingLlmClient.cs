using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class ProviderSwitchingLlmClient(
    IAiProviderSelector providerSelector,
    OpenAiCompatibleLlmClient openAiCompatibleClient,
    ClaudeLlmClient claudeClient,
    CopilotLlmClient copilotClient) : ILlmClient
{
    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var selectedRequest = request with { Model = providerSelector.CurrentModel };
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRequest.Model);
        return providerSelector.CurrentProvider switch
        {
            AiProviders.OpenAICompatible => openAiCompatibleClient.CompleteAsync(selectedRequest, cancellationToken),
            AiProviders.Claude => claudeClient.CompleteAsync(selectedRequest, cancellationToken),
            AiProviders.GitHubCopilot => copilotClient.CompleteAsync(selectedRequest, cancellationToken),
            _ => throw new InvalidOperationException("Select a supported AI provider before investigating.")
        };
    }
}
