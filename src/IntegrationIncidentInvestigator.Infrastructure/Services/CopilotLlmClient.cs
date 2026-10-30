using System.ComponentModel;
using GitHub.Copilot;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class CopilotLlmClient(IOptions<AiProviderSettings> settings, ILogger<CopilotLlmClient> logger) : ILlmClient
{
    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            UseLoggedInUser = true,
            LogLevel = CopilotLogLevel.None,
            Environment = new Dictionary<string, string> { ["COPILOT_GITHUB_TELEMETRY_DISABLED"] = "true" },
            WorkingDirectory = AppContext.BaseDirectory
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));
        try
        {
            await client.StartAsync(timeout.Token);
            await using var session = await client.CreateSessionAsync(CreateSessionConfig(request), timeout.Token);
            try
            {
#pragma warning disable GHCP001 // Structured output is experimental in the pinned Copilot SDK.
                var response = await session.SendAndWaitAsync(new MessageOptions
                {
                    Prompt = request.UserContent,
                    ResponseSchema = request.ResponseSchema
                }, TimeSpan.FromSeconds(settings.Value.TimeoutSeconds), timeout.Token);
#pragma warning restore GHCP001
                return !string.IsNullOrWhiteSpace(response?.Data.Content)
                    ? response.Data.Content
                    : throw new InvalidDataException("GitHub Copilot did not return an investigation response.");
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await session.AbortAsync(cleanup.Token);
                throw;
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The GitHub Copilot investigation timed out. Check CLI sign-in and model access, or reduce evidence.", exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            logger.LogWarning("GitHub Copilot request failed ({ErrorType}).", exception.GetType().Name);
            throw new InvalidOperationException("GitHub Copilot could not complete the request. Install/sign in to Copilot CLI with 'copilot login', check your Copilot subscription and selected model, then retry. Microsoft 365 Copilot credentials cannot be used here.", exception);
        }
    }

    public static SessionConfig CreateSessionConfig(LlmRequest request) => new()
    {
        Model = request.Model,
        AvailableTools = [],
        SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = request.SystemPrompt },
        EnableSessionStore = false,
        EnableSessionTelemetry = false,
        InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
        Memory = new MemoryConfiguration { Enabled = false }
    };
}
