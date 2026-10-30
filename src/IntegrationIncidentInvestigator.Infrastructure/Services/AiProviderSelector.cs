using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class AiProviderSelector(IOptions<AiProviderSettings> settings) : IAiProviderSelector
{
    private readonly Dictionary<string, string> _selectedModels = new(StringComparer.Ordinal)
    {
        [AiProviders.OpenAICompatible] = settings.Value.Model,
        [AiProviders.Claude] = settings.Value.Claude.Model,
        [AiProviders.GitHubCopilot] = settings.Value.Copilot.Model
    };
    private string _currentProvider = Normalize(settings.Value.Provider);

    public string CurrentProvider => _currentProvider;
    public string CurrentModel => _selectedModels[_currentProvider];
    public IReadOnlyList<string> Models => (_currentProvider switch
    {
        AiProviders.Claude => settings.Value.Claude.Models,
        AiProviders.GitHubCopilot => settings.Value.Copilot.Models,
        _ => settings.Value.Models
    }).Append(CurrentModel).Where(model => !string.IsNullOrWhiteSpace(model)).Distinct(StringComparer.Ordinal).ToArray();

    public void Select(string provider) => _currentProvider = Normalize(provider);

    public void SelectModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _selectedModels[_currentProvider] = model.Trim();
    }

    private static string Normalize(string provider) =>
        AiProviders.Supported.FirstOrDefault(value => string.Equals(value, provider, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentOutOfRangeException(nameof(provider), "Choose OpenAICompatible, Claude, or GitHubCopilot.");
}
