using System.ComponentModel;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class AiProviderStatusService(
    IOptions<AiProviderSettings> settings,
    CopilotLlmClient copilotClient,
    ILogger<AiProviderStatusService> logger) : IAiProviderStatusService
{
    public async Task<IReadOnlyList<AiProviderAvailability>> CheckAsync(CancellationToken cancellationToken)
    {
        var configuration = settings.Value;
        var statuses = new List<AiProviderAvailability>
        {
            CheckApiKey("ChatGPT / OpenAI API", configuration.ApiKeyEnvironmentVariable),
            CheckApiKey("Claude API", configuration.Claude.ApiKeyEnvironmentVariable)
        };

        try
        {
            var isAuthenticated = await copilotClient.IsAuthenticatedAsync(cancellationToken);
            statuses.Add(isAuthenticated
                ? new AiProviderAvailability("GitHub Copilot", true, "Signed in", "Copilot model access has not been tested.")
                : new AiProviderAvailability("GitHub Copilot", false, "Not signed in", "Sign in to GitHub Copilot CLI to use this provider."));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception
            or UnauthorizedAccessException or TimeoutException)
        {
            logger.LogWarning("GitHub Copilot sign-in status could not be checked ({ErrorType}).", exception.GetType().Name);
            statuses.Add(new AiProviderAvailability("GitHub Copilot", null, "Status unavailable", "Could not check Copilot sign-in. Retry the status check."));
        }

        return statuses;
    }

    private static AiProviderAvailability CheckApiKey(string provider, string environmentVariable)
    {
        var isConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable));
        return isConfigured
            ? new AiProviderAvailability(provider, true, "API key configured", "Network connectivity and model access have not been tested.")
            : new AiProviderAvailability(provider, false, "API key missing", $"Set the {environmentVariable} environment variable to use this provider.");
    }
}
